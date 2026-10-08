using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TaskWidget.Core;

public sealed partial class AppModel
{
    const string AuthorizeEndpoint = "https://auth.openai.com/api/accounts/authorize";
    const string TokenEndpoint = "https://auth.openai.com/api/accounts/oauth/token";
    const string ModelsEndpoint = "https://api.openai.com/v1/models";
    const string Resource = "https://api.openai.com/v1";
    const string Scopes = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct";
    const string PlanScope = "chatgpt.tokens.use.direct";
    const string SignedInPage = """
        <!DOCTYPE html>
        <html><head><meta charset="utf-8"><title>Task Widget</title></head>
        <body><p>Signed in, you can close this tab</p></body></html>
        """;
    static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);

    Loopback? loopback;
    long signInTimer;
    int signingIn;
    TaskCompletionSource? cancelWait;

    public async Task SignIn()
    {
        if (Interlocked.CompareExchange(ref signingIn, 1, 0) != 0)
            return;

        SignInCause = "";
        SignInStatus = "Waiting for your browser…";
        SignInState = SignInPhase.Waiting;
        try
        {
            await RunSignIn();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or SocketException or JsonException)
        {
            if (SignInState == SignInPhase.Waiting)
                FailSignIn(ex.Message);
        }
        finally
        {
            clock.Cancel(signInTimer);
            cancelWait = null;
            loopback?.Dispose();
            loopback = null;
            Volatile.Write(ref signingIn, 0);
        }
    }

    public void CancelSignIn()
    {
        if (SignInState != SignInPhase.Waiting)
            return;

        FailSignIn("You cancelled sign-in.");
        cancelWait?.TrySetResult();
        loopback?.Stop();
    }

    async Task RunSignIn()
    {
        if (browser is null || httpClient is null || protector is null)
        {
            FailSignIn("Couldn't open the browser.");
            return;
        }

        EnsureHostId();
        var listener = new Loopback();
        loopback = listener;
        var redirect = listener.RedirectUri;
        var verifier = PkceVerifier();
        var state = UrlToken();
        var nonce = UrlToken();
        var authorize = AuthorizeUri(redirect, PkceChallenge(verifier), state, nonce);

        var timedOut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancelWait = cancelled;
        signInTimer = clock.Schedule(SignInTimeout, () => timedOut.TrySetResult());

        var accept = listener.AcceptAsync(query => CallbackPage(query, state));
        try
        {
            browser.Launch(authorize);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            listener.Stop();
            FailSignIn("Couldn't open the browser.");
            return;
        }

        var finished = await Task.WhenAny(accept, timedOut.Task, cancelled.Task);
        clock.Cancel(signInTimer);

        if (timedOut.Task.IsCompleted)
        {
            listener.Stop();
            if (SignInState == SignInPhase.Waiting)
                FailSignIn("Timed out after 5 minutes.");
            return;
        }

        if (finished == cancelled.Task || cancelled.Task.IsCompleted)
        {
            listener.Stop();
            if (SignInState == SignInPhase.Waiting)
                FailSignIn("You cancelled sign-in.");
            return;
        }

        var callback = await accept;
        if (SignInState != SignInPhase.Waiting)
            return;
        if (callback.Query is null)
        {
            FailSignIn("Couldn't exchange the sign-in code for tokens.");
            return;
        }

        var query = callback.Query;
        if (query.TryGetValue("error", out var error))
        {
            FailSignIn(error == "access_denied"
                ? "Access was denied in the browser."
                : "Couldn't exchange the sign-in code for tokens.");
            return;
        }

        if (!query.TryGetValue("state", out var returned) || returned != state)
        {
            FailSignIn("The browser returned an unexpected state.");
            return;
        }

        if (!query.TryGetValue("code", out var code) || code.Length == 0
            || !query.TryGetValue("client_id", out var clientId) || clientId.Length == 0)
        {
            FailSignIn("Couldn't exchange the sign-in code for tokens.");
            return;
        }

        var scope = query.TryGetValue("scope", out var granted) ? granted : "";
        if (!HasPlanScope(scope))
        {
            FailSignIn("ChatGPT didn't grant chatgpt.tokens.use.direct.");
            return;
        }

        settingsFile.IssuedClientId = clientId;
        if (SignInState != SignInPhase.Waiting)
            return;

        var tokens = await Exchange(clientId, code, verifier, redirect);
        if (tokens is null || SignInState != SignInPhase.Waiting)
            return;

        if (!await PlanAllowsWidget(tokens.Value.AccessToken) || SignInState != SignInPhase.Waiting)
            return;

        var now = clock.UtcNow;
        var record = new TokenFile
        {
            ClientId = clientId,
            ExtAgentHostId = settingsFile.ExtAgentHostId,
            AccessToken = tokens.Value.AccessToken,
            RefreshToken = tokens.Value.RefreshToken,
            IdToken = tokens.Value.IdToken,
            AccessExpiresAt = now.AddSeconds(tokens.Value.ExpiresIn),
            RefreshExpiresAt = now.AddDays(30),
        };
        var json = JsonSerializer.Serialize(record, WidgetJsonContext.Default.TokenFile);
        SwapIn(tokenPath, protector.Protect(Encoding.UTF8.GetBytes(json)));
        settingsFile.WelcomeRetired = true;
        accessToken = record.AccessToken;
        HasConnection = true;
        SignInStatus = "";
        SignInCause = "";
        SignInState = SignInPhase.Idle;
        RaiseWelcome();
        MarkDirty();
        BringToFront?.Invoke();
    }

    void EnsureHostId()
    {
        if (settingsFile.ExtAgentHostId.Length > 0)
            return;

        settingsFile.ExtAgentHostId = "urn:uuid:" + Guid.NewGuid().ToString();
        MarkDirty();
    }

    Uri AuthorizeUri(string redirect, string challenge, string state, string nonce)
    {
        var clientId = settingsFile.IssuedClientId.Length > 0
            ? settingsFile.IssuedClientId
            : "dynamic_agent_client";
        var pairs = new (string Key, string Value)[]
        {
            ("response_type", "code"),
            ("client_id", clientId),
            ("redirect_uri", redirect),
            ("scope", Scopes),
            ("resource", Resource),
            ("state", state),
            ("nonce", nonce),
            ("code_challenge", challenge),
            ("code_challenge_method", "S256"),
            ("agent_name_hint", "Task Widget"),
            ("ext_agent_host_id", settingsFile.ExtAgentHostId),
        };
        var query = string.Join("&", pairs.Select(pair =>
            Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        return new Uri(AuthorizeEndpoint + "?" + query);
    }

    async Task<(string AccessToken, string RefreshToken, string IdToken, int ExpiresIn)?> Exchange(
        string clientId, string code, string verifier, string redirect)
    {
        var body = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = redirect,
            ["resource"] = Resource,
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(body),
        };
        using var response = await httpClient!.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            FailSignIn("Couldn't exchange the sign-in code for tokens.");
            return null;
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        if (!root.TryGetProperty("access_token", out var access)
            || access.GetString() is not { Length: > 0 } accessToken
            || !root.TryGetProperty("refresh_token", out var refresh)
            || refresh.GetString() is not string refreshToken
            || !root.TryGetProperty("id_token", out var id)
            || id.GetString() is not string idToken)
        {
            FailSignIn("Couldn't exchange the sign-in code for tokens.");
            return null;
        }

        var expiresIn = 3600;
        if (root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds) && seconds > 0)
            expiresIn = seconds;
        return (accessToken, refreshToken, idToken, expiresIn);
    }

    async Task<bool> PlanAllowsWidget(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ModelsEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient!.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode == 403 && body.Contains("subscription_sharing_user_not_eligible", StringComparison.Ordinal))
        {
            RejectPlan();
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            FailSignIn("Couldn't check this ChatGPT plan.");
            return false;
        }

        var models = ReadModels(body);
        if (models.Count == 0)
        {
            RejectPlan();
            return false;
        }

        settingsFile.Models = models;
        settingsFile.ModelsCachedAt = clock.UtcNow;
        return true;
    }

    static List<CachedModel> ReadModels(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<CachedModel>();
        foreach (var item in models.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            if (!item.TryGetProperty("slug", out var slug) || slug.GetString() is not { Length: > 0 } name)
                continue;
            var priority = 0;
            if (item.TryGetProperty("priority", out var value) && value.TryGetInt32(out var number))
                priority = number;
            list.Add(new CachedModel { Slug = name, Priority = priority });
        }

        return list;
    }

    void FailSignIn(string cause)
    {
        SignInCause = cause;
        SignInStatus = "Sign-in didn't finish";
        SignInState = SignInPhase.Failed;
    }

    void RejectPlan()
    {
        if (File.Exists(tokenPath))
            File.Delete(tokenPath);
        HasConnection = false;
        SignInCause = "";
        SignInStatus = "This ChatGPT plan can't be used in Task Widget. Go, Plus or Pro works.";
        SignInState = SignInPhase.NotEligible;
    }

    static string CallbackPage(Dictionary<string, string> query, string state)
    {
        if (query.TryGetValue("error", out _))
            return "";
        if (!query.TryGetValue("state", out var returned) || returned != state)
            return "";
        if (!query.TryGetValue("code", out var code) || code.Length == 0)
            return "";
        return SignedInPage;
    }

    static bool HasPlanScope(string scope) =>
        scope.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(PlanScope, StringComparer.Ordinal);

    static string PkceVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    static string PkceChallenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    static string UrlToken() => Base64Url(RandomNumberGenerator.GetBytes(16));

    static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

sealed class Loopback : IDisposable
{
    readonly TcpListener listener;
    volatile bool stopped;

    public Loopback()
    {
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public int Port { get; }

    public string RedirectUri => $"http://127.0.0.1:{Port}/auth/callback";

    public void Stop()
    {
        stopped = true;
        try
        {
            listener.Stop();
        }
        catch (SocketException)
        {
        }
    }

    public async Task<LoopbackCallback> AcceptAsync(Func<Dictionary<string, string>, string> pageFor)
    {
        var pending = listener.AcceptTcpClientAsync();
        try
        {
            using var tcp = await pending;
            using var stream = tcp.GetStream();
            var query = await ReadQueryAsync(stream);
            await WriteAsync(stream, pageFor(query));
            return new LoopbackCallback(query);
        }
        catch (Exception) when (stopped)
        {
            return LoopbackCallback.Closed;
        }
    }

    public void Dispose() => Stop();

    static async Task<Dictionary<string, string>> ReadQueryAsync(NetworkStream stream)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        while (buffer.Length < 65536 && HeaderEnd(buffer) < 0)
        {
            var read = await stream.ReadAsync(chunk);
            if (read == 0)
                break;
            buffer.Write(chunk, 0, read);
        }

        var text = Encoding.ASCII.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        var lineEnd = text.IndexOf("\r\n", StringComparison.Ordinal);
        var line = lineEnd < 0 ? text : text[..lineEnd];
        var parts = line.Split(' ');
        if (parts.Length < 2)
            return [];
        var target = parts[1];
        var query = target.IndexOf('?');
        return query < 0 ? [] : ParseQuery(target[(query + 1)..]);
    }

    static int HeaderEnd(MemoryStream buffer)
    {
        var bytes = buffer.GetBuffer();
        var length = (int)buffer.Length;
        for (var i = 0; i + 3 < length; i++)
        {
            if (bytes[i] == '\r' && bytes[i + 1] == '\n' && bytes[i + 2] == '\r' && bytes[i + 3] == '\n')
                return i;
        }

        return -1;
    }

    static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var rawKey = (eq < 0 ? part : part[..eq]).Replace("+", "%20");
            var rawValue = (eq < 0 ? "" : part[(eq + 1)..]).Replace("+", "%20");
            result[Uri.UnescapeDataString(rawKey)] = Uri.UnescapeDataString(rawValue);
        }

        return result;
    }

    static async Task WriteAsync(NetworkStream stream, string body)
    {
        var payload = Encoding.UTF8.GetBytes(body);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head);
        await stream.WriteAsync(payload);
        await stream.FlushAsync();
    }
}

sealed class LoopbackCallback
{
    public LoopbackCallback(Dictionary<string, string>? query) => Query = query;

    public Dictionary<string, string>? Query { get; }

    public static LoopbackCallback Closed { get; } = new(null);
}

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TaskWidget.Core;
using Xunit;

namespace TaskWidget.Tests;

public sealed class SignInTests
{
    [Fact]
    public async Task A_successful_sign_in_saves_the_token_file_and_retires_the_welcome()
    {
        using var world = new SignInWorld();
        var start = world.Clock.UtcNow;

        Assert.True(world.Model.ShowWelcome);
        Assert.False(world.Model.ShowEmptyHotkey);
        Assert.False(world.Model.HasConnection);
        Assert.True(world.Model.SignInButton);

        await world.Model.SignIn();

        Assert.True(world.Model.HasConnection, world.Model.SignInCause);
        Assert.False(world.Model.ShowWelcome);
        Assert.True(world.Model.ShowEmptyHotkey);
        Assert.Equal(SignInPhase.Idle, world.Model.SignInState);
        Assert.Equal(1, world.BroughtToFront);
        Assert.Contains("Signed in, you can close this tab", await world.Browser.Page);

        var authorize = world.Browser.Authorize ?? throw new InvalidOperationException("The browser never opened.");
        var query = Query(authorize);
        Assert.Equal("https://auth.openai.com/api/accounts/authorize", authorize.GetLeftPart(UriPartial.Path));
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("dynamic_agent_client", query["client_id"]);
        Assert.Equal("openid profile email offline_access resource.invoke chatgpt.tokens.use.direct", query["scope"]);
        Assert.Equal("https://api.openai.com/v1", query["resource"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("Task Widget", query["agent_name_hint"]);
        Assert.Matches("^urn:uuid:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", query["ext_agent_host_id"]);
        Assert.Matches("^http://127\\.0\\.0\\.1:\\d+/auth/callback$", query["redirect_uri"]);
        Assert.False(string.IsNullOrEmpty(query["state"]));
        Assert.False(string.IsNullOrEmpty(query["nonce"]));
        Assert.NotEqual(query["state"], query["nonce"]);
        Assert.False(query.ContainsKey("login_hint"));
        Assert.False(query.ContainsKey("id_token_hint"));

        var verifier = Form(world.Http.TokenForm)["code_verifier"];
        Assert.Equal(query["code_challenge"], Challenge(verifier));
        var token = Form(world.Http.TokenForm);
        Assert.Equal("https://auth.openai.com/api/accounts/oauth/token", world.Http.TokenUri);
        Assert.Equal("authorization_code", token["grant_type"]);
        Assert.Equal("oaiapp_test", token["client_id"]);
        Assert.Equal("authcode-1", token["code"]);
        Assert.Equal(query["redirect_uri"], token["redirect_uri"]);
        Assert.Equal("https://api.openai.com/v1", token["resource"]);

        Assert.Equal("https://api.openai.com/v1/models", world.Http.ModelsUri);
        Assert.Equal("Bearer access-1", world.Http.ModelsAuthorization);

        var tokenPath = Path.Combine(world.Folder, AppModel.TokenFileName);
        using (var saved = JsonDocument.Parse(File.ReadAllText(tokenPath)))
        {
            var root = saved.RootElement;
            Assert.Equal("oaiapp_test", root.GetProperty("clientId").GetString());
            Assert.Equal(query["ext_agent_host_id"], root.GetProperty("extAgentHostId").GetString());
            Assert.Equal("access-1", root.GetProperty("accessToken").GetString());
            Assert.Equal("refresh-1", root.GetProperty("refreshToken").GetString());
            Assert.Equal("id-1", root.GetProperty("idToken").GetString());
            Assert.Equal(start.AddHours(1), root.GetProperty("accessExpiresAt").GetDateTimeOffset());
            Assert.Equal(start.AddDays(30), root.GetProperty("refreshExpiresAt").GetDateTimeOffset());
        }

        world.Model.Dispose();

        var againClock = new ManualClock();
        var again = new AppModel(world.Folder, againClock, world.Http, world.Browser, world.Protector);
        try
        {
            Assert.True(again.HasConnection);
            Assert.False(again.ShowWelcome);
            Assert.True(again.ShowEmptyHotkey);

            again.UpdateDraft("buy milk");
            await again.CommitCapture();
            againClock.Advance(TimeSpan.FromMilliseconds(300));

            Assert.Equal("buy milk", Assert.Single(again.Tasks).Title);
            using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(world.Folder, "settings.json")));
            Assert.True(settings.RootElement.GetProperty("welcomeRetired").GetBoolean());
            Assert.True(again.HasConnection);
            Assert.False(again.ShowWelcome);
        }
        finally
        {
            again.Dispose();
        }
    }

    [Fact]
    public async Task Cancelling_sign_in_says_it_did_not_finish()
    {
        using var world = new SignInWorld(_ => null);
        var signingIn = world.Model.SignIn();

        Assert.Equal(SignInPhase.Waiting, world.Model.SignInState);
        Assert.Equal("Waiting for your browser…", world.Model.SignInStatus);

        world.Model.CancelSignIn();
        await signingIn;

        Assert.Equal(SignInPhase.Failed, world.Model.SignInState);
        Assert.Equal("Sign-in didn't finish", world.Model.SignInStatus);
        Assert.Equal("You cancelled sign-in.", world.Model.SignInCause);
        Assert.True(world.Model.SignInFailed);
        Assert.True(world.Model.ShowWelcome);
        Assert.False(world.Model.HasConnection);
        Assert.False(File.Exists(Path.Combine(world.Folder, AppModel.TokenFileName)));
    }

    [Fact]
    public async Task Sign_in_times_out_after_five_minutes()
    {
        using var world = new SignInWorld(_ => null);
        var signingIn = world.Model.SignIn();
        Assert.Equal(SignInPhase.Waiting, world.Model.SignInState);

        world.Clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromMilliseconds(1));
        Assert.Equal(SignInPhase.Waiting, world.Model.SignInState);

        world.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await signingIn;

        Assert.Equal("Sign-in didn't finish", world.Model.SignInStatus);
        Assert.Equal("Timed out after 5 minutes.", world.Model.SignInCause);
        Assert.True(world.Model.ShowWelcome);
        Assert.False(world.Model.HasConnection);
        Assert.False(File.Exists(Path.Combine(world.Folder, AppModel.TokenFileName)));
    }

    [Fact]
    public async Task A_denied_sign_in_says_access_was_denied()
    {
        using var world = new SignInWorld(authorize =>
            "error=access_denied&state=" + Uri.EscapeDataString(SignInWorld.QueryOf(authorize, "state")));
        await world.Model.SignIn();

        Assert.Equal("Sign-in didn't finish", world.Model.SignInStatus);
        Assert.Equal("Access was denied in the browser.", world.Model.SignInCause);
        Assert.True(world.Model.ShowWelcome);
        Assert.False(world.Model.HasConnection);
        Assert.False(File.Exists(Path.Combine(world.Folder, AppModel.TokenFileName)));
        Assert.DoesNotContain("Signed in, you can close this tab", await world.Browser.Page);
    }

    [Fact]
    public async Task A_callback_with_the_wrong_state_does_not_sign_in()
    {
        using var world = new SignInWorld(_ =>
            "code=authcode-1&state=not-the-state&scope=openid%20profile%20email%20offline_access%20resource.invoke%20chatgpt.tokens.use.direct&client_id=oaiapp_test");
        await world.Model.SignIn();

        Assert.Equal("Sign-in didn't finish", world.Model.SignInStatus);
        Assert.Equal("The browser returned an unexpected state.", world.Model.SignInCause);
        Assert.True(world.Model.ShowWelcome);
        Assert.False(File.Exists(Path.Combine(world.Folder, AppModel.TokenFileName)));
        Assert.DoesNotContain("Signed in, you can close this tab", await world.Browser.Page);
    }

    [Fact]
    public async Task A_failed_token_exchange_does_not_sign_in()
    {
        using var world = new SignInWorld(configure: http => http.TokenStatus = 400);
        await world.Model.SignIn();

        Assert.Equal("Sign-in didn't finish", world.Model.SignInStatus);
        Assert.Equal("Couldn't exchange the sign-in code for tokens.", world.Model.SignInCause);
        Assert.True(world.Model.ShowWelcome);
        Assert.False(world.Model.HasConnection);
        Assert.Null(world.Http.ModelsUri);
        Assert.False(File.Exists(Path.Combine(world.Folder, AppModel.TokenFileName)));
    }

    [Fact]
    public async Task A_sign_in_without_the_plan_scope_does_not_finish()
    {
        using var world = new SignInWorld(authorize =>
        {
            var state = Uri.EscapeDataString(SignInWorld.QueryOf(authorize, "state"));
            return $"code=authcode-1&state={state}&scope=openid%20profile%20email&client_id=oaiapp_test";
        });
        await world.Model.SignIn();

        Assert.Equal("Sign-in didn't finish", world.Model.SignInStatus);
        Assert.Equal("ChatGPT didn't grant chatgpt.tokens.use.direct.", world.Model.SignInCause);
        Assert.True(world.Model.ShowWelcome);
        Assert.False(world.Model.HasConnection);
        Assert.Null(world.Http.TokenUri);
        Assert.False(File.Exists(Path.Combine(world.Folder, AppModel.TokenFileName)));
    }

    [Fact]
    public async Task An_ineligible_plan_stays_signed_out_with_the_plan_message()
    {
        using var world = new SignInWorld(configure: http =>
        {
            http.ModelsStatus = 403;
            http.ModelsBody = """{"error":{"code":"subscription_sharing_user_not_eligible"}}""";
        });
        await world.Model.SignIn();

        Assert.Equal(SignInPhase.NotEligible, world.Model.SignInState);
        Assert.Equal("This ChatGPT plan can't be used in Task Widget. Go, Plus or Pro works.", world.Model.SignInStatus);
        Assert.Equal("", world.Model.SignInCause);
        Assert.True(world.Model.SignInButton);
        Assert.True(world.Model.ShowWelcome);
        Assert.False(world.Model.HasConnection);
        Assert.False(File.Exists(Path.Combine(world.Folder, AppModel.TokenFileName)));

        world.Model.Dispose();
        var again = new AppModel(world.Folder, new ManualClock(), world.Http, world.Browser, world.Protector);
        try
        {
            Assert.False(again.HasConnection);
            Assert.True(again.ShowWelcome);
        }
        finally
        {
            again.Dispose();
        }
    }

    [Fact]
    public async Task An_empty_model_list_stays_signed_out_with_the_plan_message()
    {
        using var world = new SignInWorld(configure: http => http.ModelsBody = """{"models":[]}""");
        await world.Model.SignIn();

        Assert.Equal(SignInPhase.NotEligible, world.Model.SignInState);
        Assert.Equal("This ChatGPT plan can't be used in Task Widget. Go, Plus or Pro works.", world.Model.SignInStatus);
        Assert.True(world.Model.ShowWelcome);
        Assert.False(world.Model.HasConnection);
        Assert.False(File.Exists(Path.Combine(world.Folder, AppModel.TokenFileName)));
    }

    [Fact]
    public async Task A_second_sign_in_waits_for_the_one_already_running()
    {
        using var world = new SignInWorld(_ => null);
        var first = world.Model.SignIn();
        Assert.Equal(SignInPhase.Waiting, world.Model.SignInState);

        var second = world.Model.SignIn();
        await second;

        Assert.Equal(1, world.Browser.Launches);
        Assert.Equal(SignInPhase.Waiting, world.Model.SignInState);
        world.Model.CancelSignIn();
        await first;
    }

    static Dictionary<string, string> Query(Uri uri) => Split(uri.Query.TrimStart('?'), unescapePlus: false);

    static Dictionary<string, string> Form(string body) => Split(body, unescapePlus: true);

    static Dictionary<string, string> Split(string text, bool unescapePlus)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var rawKey = eq < 0 ? part : part[..eq];
            var rawValue = eq < 0 ? "" : part[(eq + 1)..];
            if (unescapePlus)
            {
                rawKey = rawKey.Replace("+", "%20");
                rawValue = rawValue.Replace("+", "%20");
            }

            result[Uri.UnescapeDataString(rawKey)] = Uri.UnescapeDataString(rawValue);
        }

        return result;
    }

    static string Challenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

sealed class SignInWorld : IDisposable
{
    public string Folder { get; } = Directory.CreateTempSubdirectory("tw-signin").FullName;
    public ManualClock Clock { get; } = new();
    public OpenAiHttp Http { get; } = new();
    public CallbackBrowser Browser { get; }
    public PassThroughProtector Protector { get; } = new();
    public AppModel Model { get; }
    public int BroughtToFront { get; private set; }
    public Func<Uri, string?> Callback { get; set; }

    public SignInWorld(Func<Uri, string?>? callback = null, Action<OpenAiHttp>? configure = null)
    {
        Callback = callback ?? SuccessCallback;
        configure?.Invoke(Http);
        Browser = new CallbackBrowser(authorize => Callback(authorize));
        Model = new AppModel(Folder, Clock, Http, Browser, Protector);
        Model.BringToFront += () => BroughtToFront++;
    }

    public static string SuccessCallback(Uri authorize)
    {
        var state = QueryOf(authorize, "state");
        var scope = Uri.EscapeDataString("openid profile email offline_access resource.invoke chatgpt.tokens.use.direct");
        return $"code=authcode-1&state={Uri.EscapeDataString(state)}&scope={scope}&client_id=oaiapp_test";
    }

    public void Dispose()
    {
        Model.Dispose();
        Directory.Delete(Folder, recursive: true);
    }

    public static string QueryOf(Uri uri, string key)
    {
        var text = uri.Query.TrimStart('?');
        foreach (var part in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq < 0)
                continue;
            if (Uri.UnescapeDataString(part[..eq]) == key)
                return Uri.UnescapeDataString(part[(eq + 1)..]);
        }

        return "";
    }
}

sealed class PassThroughProtector : IDataProtector
{
    public byte[] Protect(byte[] data) => data;
    public byte[] Unprotect(byte[] data) => data;
}

sealed class CallbackBrowser : IBrowserLauncher
{
    readonly Func<Uri, string?> callbackQuery;
    readonly TaskCompletionSource<string> page = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CallbackBrowser(Func<Uri, string?> callbackQuery) => this.callbackQuery = callbackQuery;

    public Uri? Authorize { get; private set; }
    public int Launches { get; private set; }
    public Task<string> Page => page.Task;

    public void Launch(Uri authorize)
    {
        Launches++;
        Authorize = authorize;
        var query = callbackQuery(authorize);
        if (query is null)
            return;

        var redirect = QueryOf(authorize);
        var url = redirect + "?" + query;
        _ = Task.Run(async () =>
        {
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
            {
                Timeout = TimeSpan.FromSeconds(5),
            };
            client.DefaultRequestHeaders.ExpectContinue = false;
            for (var attempt = 0; attempt < 50; attempt++)
            {
                try
                {
                    page.TrySetResult(await client.GetStringAsync(url));
                    return;
                }
                catch (HttpRequestException)
                {
                    await Task.Delay(20);
                }
            }

            page.TrySetException(new HttpRequestException("The loopback callback never answered."));
        });
    }

    static string QueryOf(Uri authorize)
    {
        var text = authorize.Query.TrimStart('?');
        foreach (var part in text.Split('&'))
        {
            var eq = part.IndexOf('=');
            if (eq < 0)
                continue;
            if (Uri.UnescapeDataString(part[..eq]) == "redirect_uri")
                return Uri.UnescapeDataString(part[(eq + 1)..]);
        }

        throw new InvalidOperationException("The authorize URL has no redirect_uri.");
    }
}

sealed class OpenAiHttp : HttpMessageHandler
{
    public string TokenForm { get; private set; } = "";
    public string? TokenUri { get; private set; }
    public string? ModelsUri { get; private set; }
    public string? ModelsAuthorization { get; private set; }
    public int TokenStatus { get; set; } = 200;
    public int ModelsStatus { get; set; } = 200;
    public string ModelsBody { get; set; } = """{"models":[{"slug":"gpt-5.6-sol"}]}""";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        if (path.Contains("/oauth/token", StringComparison.Ordinal))
        {
            TokenUri = request.RequestUri!.GetLeftPart(UriPartial.Path);
            TokenForm = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            if (TokenStatus != 200)
                return new HttpResponseMessage((HttpStatusCode)TokenStatus);

            var json = """
                {"access_token":"access-1","refresh_token":"refresh-1","id_token":"id-1","expires_in":3600,"token_type":"Bearer"}
                """;
            return Json(json);
        }

        if (path.Contains("/v1/models", StringComparison.Ordinal))
        {
            ModelsUri = request.RequestUri!.GetLeftPart(UriPartial.Path);
            ModelsAuthorization = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage((HttpStatusCode)ModelsStatus)
            {
                Content = new StringContent(ModelsBody, Encoding.UTF8, "application/json"),
            };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };
}

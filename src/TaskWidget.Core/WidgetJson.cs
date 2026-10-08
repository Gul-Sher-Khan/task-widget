using System.Text.Json.Serialization;

namespace TaskWidget.Core;

sealed class TaskFile
{
    public int SchemaVersion { get; set; }
    public string Draft { get; set; } = "";
    public bool Manual { get; set; }
    public List<TaskRecord> Tasks { get; set; } = [];
}

sealed class TaskRecord
{
    public string Title { get; set; } = "";
    public string Details { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Effort { get; set; } = "";
    public DateTimeOffset Created { get; set; }
    public int Spoken { get; set; }
}

sealed class SettingsFile
{
    public int SchemaVersion { get; set; }
    public bool Docked { get; set; }
    public int RowsBeforeScrolling { get; set; } = 8;
    public bool WelcomeRetired { get; set; }
    public string ExtAgentHostId { get; set; } = "";
    public string IssuedClientId { get; set; } = "";
}

sealed class TokenFile
{
    public string ClientId { get; set; } = "";
    public string ExtAgentHostId { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string IdToken { get; set; } = "";
    public DateTimeOffset AccessExpiresAt { get; set; }
    public DateTimeOffset RefreshExpiresAt { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(TaskFile))]
[JsonSerializable(typeof(SettingsFile))]
[JsonSerializable(typeof(TokenFile))]
partial class WidgetJsonContext : JsonSerializerContext;

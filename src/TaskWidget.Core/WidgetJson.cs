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
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(TaskFile))]
[JsonSerializable(typeof(SettingsFile))]
partial class WidgetJsonContext : JsonSerializerContext;

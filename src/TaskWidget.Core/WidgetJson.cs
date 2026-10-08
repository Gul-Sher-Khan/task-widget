using System.Text.Json.Serialization;

namespace TaskWidget.Core;

sealed class TaskFile
{
    public int SchemaVersion { get; set; }
    public string Draft { get; set; } = "";
    public List<TaskRecord> Tasks { get; set; } = [];
}

sealed class TaskRecord
{
    public string Title { get; set; } = "";
    public string Details { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Effort { get; set; } = "";
}

sealed class SettingsFile
{
    public int SchemaVersion { get; set; }
    public int RowsBeforeScrolling { get; set; } = 8;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(TaskFile))]
[JsonSerializable(typeof(SettingsFile))]
partial class WidgetJsonContext : JsonSerializerContext;

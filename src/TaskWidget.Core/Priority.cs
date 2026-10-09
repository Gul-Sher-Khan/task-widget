namespace TaskWidget.Core;

public enum Priority
{
    High,
    Medium,
    Low,
}

static class Scale
{
    public static double ListMaxHeight(int rows) => rows * 37d + 4d;

    public static T Cycle<T>(T value) where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        var index = Array.IndexOf(values, value);
        if (index < 0)
            return value;
        return values[(index + 1) % values.Length];
    }

    public static bool TryPriority(string value, out Priority priority)
    {
        switch (value)
        {
            case "high":
                priority = Priority.High;
                return true;
            case "medium":
                priority = Priority.Medium;
                return true;
            case "low":
                priority = Priority.Low;
                return true;
            default:
                priority = default;
                return false;
        }
    }

    public static bool TryEffort(string value, out Effort effort)
    {
        switch (value)
        {
            case "quick":
                effort = Effort.Quick;
                return true;
            case "short":
                effort = Effort.Short;
                return true;
            case "long":
                effort = Effort.Long;
                return true;
            default:
                effort = default;
                return false;
        }
    }
}

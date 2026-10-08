using System.Text;
using System.Text.Json;

namespace TaskWidget.Core;

static class CaptureContract
{
    public readonly record struct ParsedTask(string Title, string Details, Priority Priority, Effort Effort);

    public const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["tasks"],
          "properties": {
            "tasks": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["title", "details", "priority", "effort"],
                "properties": {
                  "title": { "type": "string" },
                  "details": { "type": "string" },
                  "priority": { "type": "string", "enum": ["high", "medium", "low"] },
                  "effort": { "type": "string", "enum": ["quick", "short", "long"] }
                }
              }
            }
          }
        }
        """;

    public static string Instructions(DateTimeOffset today) => Prefix + FormatToday(today) + ".";

    public static string FormatToday(DateTimeOffset today) =>
        Weekdays[(int)today.DayOfWeek] + " " + today.Day + " " + Months[today.Month - 1] + " " + today.Year;

    static readonly string[] Weekdays = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
    static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    const string Prefix = """
        You turn one dictated note (a "Capture") into tasks for the user's personal to-do list.

        The Capture was dictated, so it may contain filler, false starts, self-corrections and transcription slips. When the user corrects themselves ("Tuesday, no, Thursday"), keep only the correction. Ignore filler.

        Splitting
        - Make one task per action the user would tick off on its own.
        - Never break an action into steps or sub-tasks, even if the user lists the steps. Put steps they said into details instead.
        - One action with several objects stays one task ("Buy milk, eggs and bread").
        - Thinking, deciding or checking something counts as an action ("Decide whether to move to Postgres").
        - Drop chatter, feelings, and facts that ask for no action. If nothing in the Capture asks for action, return an empty tasks list.
        - When the Capture names a meeting or appointment and also something to do for it, make a task only for that action and put the meeting in its details. A meeting mentioned on its own is a task.
        - Keep the tasks in the order they were said.

        title
        - One line, at most 60 characters, starting with a verb in the imperative ("Email Sarah the invoice").
        - Use the Capture's own words wherever you can. Add nothing that the Capture doesn't say.
        - Keep the user's point of view: "I" and "my" as they said them, never "you". Write it in the language of the Capture. Sentence case, no full stop at the end.

        details
        - Only context stated in the Capture that the title leaves out: who, deadlines, where, why, amounts, steps the user listed.
        - Never invent steps, advice or anything else the user didn't say. If there is nothing to add, use "".
        - Turn relative dates into a short weekday and date, using today's date: "before Friday" becomes "Before Fri 9 Oct". No year unless it isn't this year. Times as the user said them ("3pm", "before 9"). If a relative date could mean two dates ("next Saturday", "this weekend"), keep the user's words instead, and don't treat it as within 3 days.
        - Write details as one short phrase or sentence fragment, with no full stop at the end.

        priority
        - high: only when the Capture itself gives a reason: a deadline within 3 days of today, someone waiting on or blocked by the user, or a stated bad consequence (money, health, legal, something breaking).
        - medium: it matters, but nothing breaks this week. A deadline more than 3 days away is medium. This is the default: an ordinary errand, email or chore with no reason given is medium.
        - low: nice to do; nothing happens if it slips ("someday", "at some point", "if I get time").
        - Words the user says about urgency ("urgent", "no rush", "whenever") win over your own judgement.

        effort (how long the action itself takes)
        - quick: 15 minutes or less (a call, a short message, a purchase on the way).
        - short: up to an hour.
        - long: more than an hour (writing, building, refactoring, studying).

        Today is 
        """;

    public static (bool Ok, List<ParsedTask> Tasks) Parse(string text)
    {
        var attempts = new List<string> { text, StripFence(text) };
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
            attempts.Add(text[start..(end + 1)]);

        foreach (var attempt in attempts)
        {
            try
            {
                using var doc = JsonDocument.Parse(attempt);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    continue;
                if (!doc.RootElement.TryGetProperty("tasks", out var tasks) || tasks.ValueKind != JsonValueKind.Array)
                    continue;

                var list = new List<ParsedTask>();
                foreach (var item in tasks.EnumerateArray())
                {
                    if (!TryTask(item, out var task))
                        return (false, []);
                    list.Add(task);
                }

                return (true, list);
            }
            catch (JsonException)
            {
            }
        }

        return (false, []);
    }

    public static string OutputText(string body)
    {
        var deltas = new StringBuilder();
        string? done = null;
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r').TrimStart();
            if (!trimmed.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var data = trimmed[5..].TrimStart();
            if (data == "[DONE]")
                continue;

            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : null;
                if (type == "response.output_text.delta" && root.TryGetProperty("delta", out var delta))
                    deltas.Append(delta.GetString());
                else if (type == "response.output_text.done" && root.TryGetProperty("text", out var text))
                    done = text.GetString();
            }
            catch (JsonException)
            {
            }
        }

        if (done is not null)
            return done;
        if (deltas.Length > 0)
            return deltas.ToString();
        return body;
    }

    static bool TryTask(JsonElement item, out ParsedTask task)
    {
        task = default;
        if (item.ValueKind != JsonValueKind.Object)
            return false;
        if (!item.TryGetProperty("title", out var title) || title.GetString() is not string titleText)
            return false;
        if (!item.TryGetProperty("details", out var details) || details.GetString() is not string detailsText)
            return false;
        if (!item.TryGetProperty("priority", out var priority) || priority.GetString() is not string priorityText)
            return false;
        if (!item.TryGetProperty("effort", out var effort) || effort.GetString() is not string effortText)
            return false;
        if (!TryPriority(priorityText, out var parsedPriority) || !TryEffort(effortText, out var parsedEffort))
            return false;

        task = new ParsedTask(titleText, detailsText, parsedPriority, parsedEffort);
        return true;
    }

    static bool TryPriority(string value, out Priority priority)
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

    static bool TryEffort(string value, out Effort effort)
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

    // Same replacements as the v4 parse: a leading ``` or ```json fence, and a trailing fence.
    static string StripFence(string text)
    {
        var start = 0;
        while (start < text.Length && char.IsWhiteSpace(text[start]))
            start++;
        if (text.AsSpan(start).StartsWith("```"))
        {
            start += 3;
            if (text.AsSpan(start).StartsWith("json"))
                start += 4;
            while (start < text.Length && char.IsWhiteSpace(text[start]))
                start++;
        }
        else
        {
            start = 0;
        }

        var end = text.Length;
        while (end > start && char.IsWhiteSpace(text[end - 1]))
            end--;
        if (end - start >= 3 && text.AsSpan(end - 3, 3).SequenceEqual("```"))
        {
            end -= 3;
            while (end > start && char.IsWhiteSpace(text[end - 1]))
                end--;
        }
        else if (start == 0)
        {
            return text;
        }

        return text[start..end];
    }
}

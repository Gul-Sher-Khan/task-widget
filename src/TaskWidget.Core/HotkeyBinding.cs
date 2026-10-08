namespace TaskWidget.Core;

public sealed class HotkeyBinding : IEquatable<HotkeyBinding>
{
    public const string LanguageToggleWarning =
        "A modifier pair (like Ctrl + Shift) fires on a tap: press both, release, no other key. Some keyboards use Ctrl + Shift to switch language.";

    HotkeyBinding(bool control, bool alt, bool shift, bool windows, int? chordKey, string? chordName)
    {
        Control = control;
        Alt = alt;
        Shift = shift;
        Windows = windows;
        ChordKey = chordKey;
        ChordName = chordName;
        Mask = (control ? 1 : 0) | (alt ? 2 : 0) | (shift ? 4 : 0) | (windows ? 8 : 0);
        var parts = new List<string>(5);
        if (control)
            parts.Add("Ctrl");
        if (alt)
            parts.Add("Alt");
        if (shift)
            parts.Add("Shift");
        if (windows)
            parts.Add("Win");
        if (chordName is not null)
            parts.Add(chordName);
        Text = string.Join(" + ", parts);
    }

    public bool Control { get; }

    public bool Alt { get; }

    public bool Shift { get; }

    public bool Windows { get; }

    public int? ChordKey { get; }

    public string? ChordName { get; }

    public int Mask { get; }

    public string Text { get; }

    public bool IsTap => ChordKey is null;

    public bool IsChord => ChordKey is not null;

    public bool IsReserved =>
        (Control && Windows && Alt && !Shift && ChordKey is null)
        || (Control && Windows && !Alt && !Shift && ChordKey is null)
        || (Control && Windows && !Alt && !Shift && ChordKey == 0x20)
        || (Control && Alt && !Windows && !Shift && ChordKey == 0x20)
        || (Shift && Alt && !Control && !Windows && ChordKey is 0x5A or 0x58);

    public static HotkeyBinding CtrlShift { get; } = Parse("Ctrl + Shift");

    public static HotkeyBinding Parse(string text)
    {
        if (!TryParse(text, out var binding))
            throw new FormatException($"Unknown hotkey \"{text}\".");
        return binding;
    }

    public static bool TryParse(string? text, out HotkeyBinding binding)
    {
        binding = null!;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Split(" + ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        bool control = false, alt = false, shift = false, windows = false;
        int? key = null;
        string? keyName = null;
        foreach (var part in parts)
        {
            switch (part)
            {
                case "Ctrl":
                    control = true;
                    break;
                case "Alt":
                    alt = true;
                    break;
                case "Shift":
                    shift = true;
                    break;
                case "Win":
                    windows = true;
                    break;
                default:
                    if (key is not null || !TryKey(part, out var code, out var name))
                        return false;
                    key = code;
                    keyName = name;
                    break;
            }
        }

        var modifiers = (control ? 1 : 0) + (alt ? 1 : 0) + (shift ? 1 : 0) + (windows ? 1 : 0);
        if (key is null && modifiers < 2)
            return false;
        if (key is not null && modifiers < 1)
            return false;

        binding = new HotkeyBinding(control, alt, shift, windows, key, keyName);
        return true;
    }

    public bool Equals(HotkeyBinding? other) =>
        other is not null
        && Control == other.Control
        && Alt == other.Alt
        && Shift == other.Shift
        && Windows == other.Windows
        && ChordKey == other.ChordKey;

    public override bool Equals(object? obj) => obj is HotkeyBinding other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Control, Alt, Shift, Windows, ChordKey);

    static bool TryKey(string part, out int code, out string name)
    {
        if (part.Equals("Space", StringComparison.OrdinalIgnoreCase))
        {
            code = 0x20;
            name = "Space";
            return true;
        }

        if (part.Length == 1)
        {
            var c = char.ToUpperInvariant(part[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                code = c;
                name = c.ToString();
                return true;
            }
        }

        if (part.Length is >= 2 and <= 3
            && (part[0] is 'F' or 'f')
            && int.TryParse(part.AsSpan(1), out var n)
            && n is >= 1 and <= 24)
        {
            code = 0x70 + n - 1;
            name = "F" + n;
            return true;
        }

        code = 0;
        name = "";
        return false;
    }
}

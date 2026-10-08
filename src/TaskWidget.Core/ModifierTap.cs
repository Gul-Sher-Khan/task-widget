namespace TaskWidget.Core;

public readonly record struct TapState(int Down, bool Watching, bool Armed, bool Extra, long ArmedAt);

public static class ModifierTap
{
    public const long WindowMs = 1000;

    public static (TapState State, bool Fired) Apply(
        TapState state,
        bool down,
        int virtualKey,
        long timeMs,
        HotkeyBinding binding)
    {
        if (!binding.IsTap)
            return (state, false);

        var bit = Bit(virtualKey);
        var needed = binding.Mask;
        if (bit != 0 && (needed & bit) != 0)
        {
            state = down
                ? state with { Down = state.Down | bit }
                : state with { Down = state.Down & ~bit };
        }
        else if (down && state.Watching)
        {
            state = state with { Extra = true };
        }

        if ((state.Down & needed) != 0 && !state.Watching)
            state = state with { Watching = true };

        if ((state.Down & needed) == needed && needed != 0 && !state.Armed)
            state = state with { Armed = true, ArmedAt = timeMs };

        if ((state.Down & needed) != 0)
            return (state, false);

        var fired = state.Armed && !state.Extra && (uint)(timeMs - state.ArmedAt) <= WindowMs;
        return (default, fired);
    }

    public static int Bit(int virtualKey) => virtualKey switch
    {
        0x11 or 0xA2 or 0xA3 => 1,
        0x12 or 0xA4 or 0xA5 => 2,
        0x10 or 0xA0 or 0xA1 => 4,
        0x5B or 0x5C => 8,
        _ => 0,
    };
}

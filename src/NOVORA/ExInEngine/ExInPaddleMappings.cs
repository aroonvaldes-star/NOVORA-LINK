namespace NOVORA.ExInEngine;

public enum ExInPaddle
{
    P1,
    P2,
    P3,
    P4
}

/// <summary>Asignaciones persistentes de los cuatro paddles posteriores del mando.</summary>
public sealed record ExInPaddleMappings(
    ExInButtons P1,
    ExInButtons P2,
    ExInButtons P3,
    ExInButtons P4)
{
    public static ExInPaddleMappings DefaultVE { get; } = new(
        ExInButtons.None, ExInButtons.None, ExInButtons.None, ExInButtons.None);

    private const ExInButtons PaddleFlagsVE =
        ExInButtons.P1 | ExInButtons.P2 | ExInButtons.P3 | ExInButtons.P4;

    private const ExInButtons AssignableButtonsVE =
        ExInButtons.South | ExInButtons.East | ExInButtons.West | ExInButtons.North |
        ExInButtons.LeftShoulder | ExInButtons.RightShoulder |
        ExInButtons.Back | ExInButtons.Start | ExInButtons.Guide |
        ExInButtons.LeftStick | ExInButtons.RightStick |
        ExInButtons.DPadUp | ExInButtons.DPadDown | ExInButtons.DPadLeft | ExInButtons.DPadRight |
        ExInButtons.Touchpad;

    public ExInButtons GetVE(ExInPaddle paddle)
        => paddle switch
        {
            ExInPaddle.P1 => P1,
            ExInPaddle.P2 => P2,
            ExInPaddle.P3 => P3,
            ExInPaddle.P4 => P4,
            _ => ExInButtons.None
        };

    public ExInPaddleMappings WithVE(ExInPaddle paddle, ExInButtons target)
        => paddle switch
        {
            ExInPaddle.P1 => this with { P1 = target },
            ExInPaddle.P2 => this with { P2 = target },
            ExInPaddle.P3 => this with { P3 = target },
            ExInPaddle.P4 => this with { P4 = target },
            _ => this
        };

    public ExInState ApplyVE(ExInState state)
    {
        ExInButtons source = state.Buttons;
        ExInButtons repeated = ExInButtons.None;
        if (source.HasFlag(ExInButtons.P1)) repeated |= P1;
        if (source.HasFlag(ExInButtons.P2)) repeated |= P2;
        if (source.HasFlag(ExInButtons.P3)) repeated |= P3;
        if (source.HasFlag(ExInButtons.P4)) repeated |= P4;
        return state with { Buttons = (source & ~PaddleFlagsVE) | repeated };
    }

    public bool IsValidVE()
        => IsAssignableVE(P1) && IsAssignableVE(P2) && IsAssignableVE(P3) && IsAssignableVE(P4);

    public static bool IsAssignableVE(ExInButtons target)
    {
        if (target == ExInButtons.None) return true;
        uint value = (uint)target;
        return (value & (value - 1)) == 0 && ((uint)target & ~(uint)AssignableButtonsVE) == 0;
    }
}

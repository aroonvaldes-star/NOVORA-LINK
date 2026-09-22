namespace NOVORA.ExInEngine;

/// <summary>
/// Estado normalizado con el mismo rango de ejes usado por SDL/scrcpy.
/// Sticks: -32768..32767. Triggers: 0..32767.
/// </summary>
public readonly record struct ExInState(
    short LeftX,
    short LeftY,
    short RightX,
    short RightY,
    short LeftTrigger,
    short RightTrigger,
    ExInButtons Buttons);

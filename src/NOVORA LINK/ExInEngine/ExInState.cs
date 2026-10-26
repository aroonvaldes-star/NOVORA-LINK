namespace NOVORA.ExInEngine;

/// <summary>
/// Estado normalizado con el rango de ejes de SDL y UHID.
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

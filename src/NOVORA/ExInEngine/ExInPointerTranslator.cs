namespace NOVORA.ExInEngine;

public enum ExInUiAction
{
    None,
    Up,
    Down,
    Left,
    Right,
    Select,
    Back
}

public readonly record struct ExInPointerReport(
    sbyte X,
    sbyte Y,
    sbyte Wheel,
    sbyte HorizontalWheel,
    byte Buttons,
    ExInUiAction Action);

public sealed class ExInPointerTranslator
{
    private readonly double _deadzoneVE;
    private readonly Dictionary<TouchKeyVE, TouchPointVE> _touchesVE = [];
    private ExInButtons _previousButtonsVE;
    private TouchPointVE? _lastCentroidVE;

    public ExInPointerTranslator(double deadzone = 0.08)
    {
        if (!double.IsFinite(deadzone) || deadzone is < 0 or >= 0.5)
            throw new ArgumentOutOfRangeException(nameof(deadzone));
        _deadzoneVE = deadzone;
    }

    public ExInPointerReport FromXboxVE(ExInState state, bool inputAlreadyCalibrated = false)
    {
        ExInButtons pressed = state.Buttons & ~_previousButtonsVE;
        _previousButtonsVE = state.Buttons;
        ExInUiAction action = GetActionVE(pressed);
        double deadzone = inputAlreadyCalibrated ? 0 : _deadzoneVE;
        return new(
            ScalePointerVE(state.LeftX, deadzone),
            ScalePointerVE(state.LeftY, deadzone),
            ScaleWheelVE(state.RightY, invert: true),
            ScaleWheelVE(state.RightX, invert: false),
            state.Buttons.HasFlag(ExInButtons.South) ? (byte)1 : (byte)0,
            action);
    }

    public ExInPointerReport FromAxesVE(ExInState state, bool inputAlreadyCalibrated = false)
    {
        double deadzone = inputAlreadyCalibrated ? 0 : _deadzoneVE;
        return new(
            ScalePointerVE(state.LeftX, deadzone),
            ScalePointerVE(state.LeftY, deadzone),
            ScaleWheelVE(state.RightY, invert: true),
            ScaleWheelVE(state.RightX, invert: false),
            state.Buttons.HasFlag(ExInButtons.South) ? (byte)1 : (byte)0,
            ExInUiAction.None);
    }

    public void PrimeButtonsVE(ExInButtons buttons) => _previousButtonsVE = buttons;

    public ExInPointerReport TouchDownVE(int touchpadId, long fingerId, float x, float y, bool physicalClick)
    {
        ValidateIdentityVE(touchpadId, fingerId);
        ValidateTouchVE(x, y);
        TouchKeyVE key = new(touchpadId, fingerId);
        if (_touchesVE.ContainsKey(key)) FailGestureVE("Down duplicado.");
        _touchesVE[key] = new(x, y);
        UpdateCentroidVE();
        return new(0, 0, 0, 0, physicalClick ? (byte)1 : (byte)0, ExInUiAction.None);
    }

    public ExInPointerReport TouchMotionVE(int touchpadId, long fingerId, float x, float y, bool physicalClick)
    {
        ValidateTouchVE(x, y);
        ValidateIdentityVE(touchpadId, fingerId);
        TouchKeyVE key = new(touchpadId, fingerId);
        if (!_touchesVE.TryGetValue(key, out TouchPointVE previous))
        {
            ResetTouchVE();
            return default;
        }

        _touchesVE[key] = new(x, y);
        float deltaX = x - previous.X;
        float deltaY = y - previous.Y;
        if (_touchesVE.Count == 1)
        {
            _lastCentroidVE = new(x, y);
            return new(ScaleTouchVE(deltaX), ScaleTouchVE(deltaY), 0, 0,
                physicalClick ? (byte)1 : (byte)0, ExInUiAction.None);
        }

        if (_touchesVE.Count == 2)
        {
            TouchPointVE centroid = GetCentroidVE();
            TouchPointVE old = _lastCentroidVE ?? centroid;
            _lastCentroidVE = centroid;
            return new(0, 0, ScaleTouchWheelVE(centroid.Y - old.Y, invert: true),
                ScaleTouchWheelVE(centroid.X - old.X, invert: false), physicalClick ? (byte)1 : (byte)0, ExInUiAction.None);
        }

        ResetTouchVE();
        return default;
    }

    public ExInPointerReport TouchUpVE(int touchpadId, long fingerId, bool physicalClick)
    {
        ValidateIdentityVE(touchpadId, fingerId);
        if (!_touchesVE.Remove(new(touchpadId, fingerId))) FailGestureVE("Up sin Down.");
        UpdateCentroidVE();
        return new(0, 0, 0, 0, physicalClick ? (byte)1 : (byte)0, ExInUiAction.None);
    }

    public void ResetTouchVE()
    {
        _touchesVE.Clear();
        _lastCentroidVE = null;
    }

    private static sbyte ScalePointerVE(short value, double deadzone)
    {
        double normalized = value / 32767d;
        double magnitude = Math.Abs(normalized);
        if (magnitude <= deadzone) return 0;
        double active = (magnitude - deadzone) / (1 - deadzone);
        double accelerated = 1 + active * active * 63;
        return (sbyte)Math.Clamp((int)Math.Round(Math.CopySign(accelerated, normalized)), -24, 24);
    }

    private static sbyte ScaleWheelVE(short value, bool invert)
    {
        if (Math.Abs((int)value) < 7000) return 0;
        int scaled = Math.Clamp((int)Math.Round(value / 12000d), -3, 3);
        return (sbyte)(invert ? -scaled : scaled);
    }

    private static sbyte ScaleTouchVE(float delta)
        => (sbyte)Math.Clamp((int)Math.Round(delta * 900), -48, 48);

    private static sbyte ScaleTouchWheelVE(float delta, bool invert)
    {
        int scaled = Math.Clamp((int)Math.Round(delta * 40), -4, 4);
        return (sbyte)(invert ? -scaled : scaled);
    }

    private static ExInUiAction GetActionVE(ExInButtons buttons)
    {
        if (buttons.HasFlag(ExInButtons.DPadUp)) return ExInUiAction.Up;
        if (buttons.HasFlag(ExInButtons.DPadDown)) return ExInUiAction.Down;
        if (buttons.HasFlag(ExInButtons.DPadLeft)) return ExInUiAction.Left;
        if (buttons.HasFlag(ExInButtons.DPadRight)) return ExInUiAction.Right;
        if (buttons.HasFlag(ExInButtons.East)) return ExInUiAction.Back;
        return ExInUiAction.None;
    }

    private void ValidateTouchVE(float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || x is < 0 or > 1 || y is < 0 or > 1)
        {
            ResetTouchVE();
            throw new ArgumentOutOfRangeException(nameof(x), "Coordenada de touchpad inválida.");
        }
    }

    private void ValidateIdentityVE(int touchpadId, long fingerId)
    {
        if (touchpadId < 0 || fingerId < 0) FailGestureVE("Identidad de touchpad inválida.");
    }

    private void FailGestureVE(string message)
    {
        ResetTouchVE();
        throw new InvalidOperationException(message);
    }

    private TouchPointVE GetCentroidVE()
        => new(_touchesVE.Values.Average(value => value.X), _touchesVE.Values.Average(value => value.Y));

    private void UpdateCentroidVE()
        => _lastCentroidVE = _touchesVE.Count == 0 ? null : GetCentroidVE();

    private readonly record struct TouchKeyVE(int Touchpad, long Finger);
    private readonly record struct TouchPointVE(float X, float Y);
}

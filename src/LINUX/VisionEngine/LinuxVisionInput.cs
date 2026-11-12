using System.Buffers.Binary;
using System.Text.Json;

namespace NOVORA.Linux.VisionEngine;

/// <summary>AppControl's framed JSON input protocol. Coordinates are in the visible Android orientation.</summary>
internal sealed class LinuxVisionInput
{
    private VideoSession? _session;
    private int _windowWidth, _windowHeight, _pixelWidth, _pixelHeight;
    private bool _dragging;
    private readonly HashSet<byte> _navigationButtons = new();
    private int _lastX, _lastY;
    private ushort _lastWidth, _lastHeight;
    private readonly Dictionary<int, uint> _pressedKeys = new();
    public event Action<byte[]>? Frame;

    public void UpdateViewport(VideoSession session, int windowWidth, int windowHeight, int pixelWidth, int pixelHeight)
    {
        if (_session is not null && (_session.Width != session.Width || _session.Height != session.Height ||
            _session.RotationDegrees != session.RotationDegrees)) ReleaseAll();
        _session = session;
        (_windowWidth, _windowHeight, _pixelWidth, _pixelHeight) = (windowWidth, windowHeight, pixelWidth, pixelHeight);
    }

    public bool Button(byte button, bool down, float x, float y)
    {
        if (button is 3 or 2)
        {
            if (down)
            {
                if (!TryMap(x, y, false, out _, out _, out _, out _)) return false;
                _navigationButtons.Add(button);
            }
            else if (!_navigationButtons.Remove(button)) return false;
            if (!down) { Emit(new InputCommand(0, KeyAction: 0, Keycode: button == 3 ? 4u : 3u)); Emit(new InputCommand(0, KeyAction: 1, Keycode: button == 3 ? 4u : 3u)); }
            return true;
        }
        if (button != 1 || (!down && !_dragging)) return false;
        if (!TryMap(x, y, _dragging, out int px, out int py, out ushort width, out ushort height)) return false;
        _dragging = down;
        (_lastX, _lastY, _lastWidth, _lastHeight) = (px, py, width, height);
        Emit(new InputCommand(2, MotionAction: down ? 0 : 1, PointerId: 1, X: px, Y: py,
            ScreenWidth: width, ScreenHeight: height, Pressure: down ? 1 : 0,
            ActionButton: 1, Buttons: down ? 1u : 0u));
        return true;
    }

    public void Motion(float x, float y)
    {
        if (!_dragging || !TryMap(x, y, true, out int px, out int py, out ushort width, out ushort height)) return;
        (_lastX, _lastY, _lastWidth, _lastHeight) = (px, py, width, height);
        Emit(new InputCommand(2, MotionAction: 2, PointerId: 1, X: px, Y: py,
            ScreenWidth: width, ScreenHeight: height, Pressure: 1, Buttons: 1));
    }

    public void Scroll(float x, float y, float horizontal, float vertical, bool flipped)
    {
        if (!float.IsFinite(horizontal) || !float.IsFinite(vertical) || horizontal == 0 && vertical == 0 ||
            !TryMap(x, y, false, out int px, out int py, out ushort width, out ushort height)) return;
        float sign = flipped ? -1 : 1;
        Emit(new InputCommand(3, X: px, Y: py, ScreenWidth: width, ScreenHeight: height,
            HorizontalScroll: Math.Clamp(horizontal * sign, -10, 10), VerticalScroll: Math.Clamp(vertical * sign, -10, 10)));
    }

    public void Text(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        // A UTF-8 scalar is at most four bytes. Bound each frame below the 64 KiB wire limit.
        foreach (string chunk in ChunkText(text, 4096)) Emit(new InputCommand(1, Text: chunk));
    }

    private static IEnumerable<string> ChunkText(string text, int limit)
    {
        int start = 0;
        while (start < text.Length)
        {
            int count = Math.Min(limit, text.Length - start);
            if (start + count < text.Length && char.IsHighSurrogate(text[start + count - 1])) count--;
            yield return text.Substring(start, count);
            start += count;
        }
    }

    public void Key(int scancode, ushort modifiers, bool down, bool repeat)
    {
        uint keycode;
        if (down)
        {
            keycode = MapKey(scancode, modifiers);
            if (keycode == 0) return; // Text composition is handled by SDL_TEXT_INPUT, never duplicated here.
            _pressedKeys[scancode] = keycode;
        }
        else if (!_pressedKeys.Remove(scancode, out keycode)) return;
        Emit(new InputCommand(0, KeyAction: down ? 0 : 1, Keycode: keycode,
            Repeat: repeat ? 1u : 0u, MetaState: MapModifiers(modifiers)));
    }

    public void ReleaseAll()
    {
        if (_dragging)
            Emit(new InputCommand(2, MotionAction: 3, PointerId: 1, X: _lastX, Y: _lastY,
                ScreenWidth: _lastWidth, ScreenHeight: _lastHeight));
        _dragging = false;
        _navigationButtons.Clear();
        foreach (uint key in _pressedKeys.Values) Emit(new InputCommand(0, KeyAction: 1, Keycode: key));
        _pressedKeys.Clear();
    }

    internal bool TryMap(float x, float y, bool clamp, out int px, out int py, out ushort width, out ushort height)
    {
        px = py = 0; width = height = 0;
        if (_session is null || _windowWidth <= 0 || _windowHeight <= 0 || _pixelWidth <= 0 || _pixelHeight <= 0 ||
            !float.IsFinite(x) || !float.IsFinite(y)) return false;
        int visibleWidth = _session.RotationDegrees is 90 or 270 ? _session.Height : _session.Width;
        int visibleHeight = _session.RotationDegrees is 90 or 270 ? _session.Width : _session.Height;
        if (visibleWidth <= 0 || visibleHeight <= 0 || visibleWidth > ushort.MaxValue || visibleHeight > ushort.MaxValue) return false;
        double scale = Math.Min(_pixelWidth / (double)visibleWidth, _pixelHeight / (double)visibleHeight);
        double vw = visibleWidth * scale, vh = visibleHeight * scale;
        double left = (_pixelWidth - vw) / 2, top = (_pixelHeight - vh) / 2;
        double clientX = x * _pixelWidth / _windowWidth, clientY = y * _pixelHeight / _windowHeight;
        if (!clamp && (clientX < left || clientX >= left + vw || clientY < top || clientY >= top + vh)) return false;
        px = Math.Clamp((int)Math.Round((clientX - left) / Math.Max(1, vw - 1) * (visibleWidth - 1)), 0, visibleWidth - 1);
        py = Math.Clamp((int)Math.Round((clientY - top) / Math.Max(1, vh - 1) * (visibleHeight - 1)), 0, visibleHeight - 1);
        width = (ushort)visibleWidth; height = (ushort)visibleHeight;
        return true;
    }

    private void Emit(InputCommand command)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(command);
        byte[] frame = new byte[json.Length + 4];
        BinaryPrimitives.WriteInt32BigEndian(frame, json.Length);
        json.CopyTo(frame, 4);
        Frame?.Invoke(frame);
    }

    private static uint MapKey(int code, ushort modifiers)
    {
        if ((modifiers & 0x0fc0) != 0 && code is >= 4 and <= 29) return (uint)(code - 4 + 29); // Ctrl/Alt letters
        return code switch
        {
            40 or 88 => 66, 41 => 4, 42 => 67, 43 => 61, 76 => 112,
            79 => 22, 80 => 21, 81 => 20, 82 => 19,
            74 => 122, 77 => 123, 75 => 92, 78 => 93,
            224 or 228 => 113, 225 or 229 => 59, 226 or 230 => 57, 227 or 231 => 117,
            _ => 0
        };
    }

    private static uint MapModifiers(ushort value) =>
        ((value & 0x0003) != 0 ? 1u : 0) | ((value & 0x00c0) != 0 ? 0x1000u : 0) |
        ((value & 0x0300) != 0 ? 2u : 0) | ((value & 0x0c00) != 0 ? 0x10000u : 0);

    private sealed record InputCommand(int Type, int KeyAction = 0, uint Keycode = 0, uint Repeat = 0,
        uint MetaState = 0, string? Text = null, int MotionAction = 0, ulong PointerId = 0,
        int X = 0, int Y = 0, ushort ScreenWidth = 0, ushort ScreenHeight = 0,
        float Pressure = 0, uint ActionButton = 0, uint Buttons = 0,
        float HorizontalScroll = 0, float VerticalScroll = 0);
}

using Forms = System.Windows.Forms;

namespace NOVORA.VisionEngine.Control;

/// <summary>
/// Mapa WinForms/Windows -> Android KeyEvent.
/// Los valores son android.view.KeyEvent.KEYCODE_*.
/// </summary>
public static class KeycodeControlVE
{
    public static bool TryMapVE(
        Forms.Keys key,
        out uint androidKeycode)
    {
        Forms.Keys code =
            key & Forms.Keys.KeyCode;

        if (code is >= Forms.Keys.A and <= Forms.Keys.Z)
        {
            androidKeycode =
                29u +
                (uint)(code - Forms.Keys.A);

            return true;
        }

        if (code is >= Forms.Keys.D0 and <= Forms.Keys.D9)
        {
            androidKeycode =
                7u +
                (uint)(code - Forms.Keys.D0);

            return true;
        }

        if (code is >= Forms.Keys.NumPad0 and <= Forms.Keys.NumPad9)
        {
            androidKeycode =
                144u +
                (uint)(code - Forms.Keys.NumPad0);

            return true;
        }

        if (code is >= Forms.Keys.F1 and <= Forms.Keys.F12)
        {
            androidKeycode =
                131u +
                (uint)(code - Forms.Keys.F1);

            return true;
        }

        androidKeycode =
            code switch
            {
                Forms.Keys.Back => 67,
                Forms.Keys.Tab => 61,
                Forms.Keys.Enter => 66,
                Forms.Keys.Pause => 121,
                Forms.Keys.CapsLock => 115,
                Forms.Keys.Escape => 111,
                Forms.Keys.Space => 62,
                Forms.Keys.PageUp => 92,
                Forms.Keys.PageDown => 93,
                Forms.Keys.End => 123,
                Forms.Keys.Home => 122,
                Forms.Keys.Left => 21,
                Forms.Keys.Up => 19,
                Forms.Keys.Right => 22,
                Forms.Keys.Down => 20,
                Forms.Keys.Insert => 124,
                Forms.Keys.Delete => 112,
                Forms.Keys.LShiftKey => 59,
                Forms.Keys.RShiftKey => 60,
                Forms.Keys.ShiftKey => 59,
                Forms.Keys.LControlKey => 113,
                Forms.Keys.RControlKey => 114,
                Forms.Keys.ControlKey => 113,
                Forms.Keys.LMenu => 57,
                Forms.Keys.RMenu => 58,
                Forms.Keys.Menu => 57,
                Forms.Keys.LWin => 117,
                Forms.Keys.RWin => 118,
                Forms.Keys.NumLock => 143,
                Forms.Keys.Scroll => 116,
                Forms.Keys.PrintScreen => 120,
                Forms.Keys.Multiply => 155,
                Forms.Keys.Add => 157,
                Forms.Keys.Subtract => 156,
                Forms.Keys.Decimal => 158,
                Forms.Keys.Divide => 154,
                Forms.Keys.OemSemicolon => 74,
                Forms.Keys.Oemplus => 70,
                Forms.Keys.Oemcomma => 55,
                Forms.Keys.OemMinus => 69,
                Forms.Keys.OemPeriod => 56,
                Forms.Keys.OemQuestion => 76,
                Forms.Keys.Oemtilde => 68,
                Forms.Keys.OemOpenBrackets => 71,
                Forms.Keys.OemPipe => 73,
                Forms.Keys.OemCloseBrackets => 72,
                Forms.Keys.OemQuotes => 75,
                Forms.Keys.VolumeDown => 25,
                Forms.Keys.VolumeUp => 24,
                Forms.Keys.VolumeMute => 91,
                Forms.Keys.MediaPlayPause => 85,
                Forms.Keys.MediaStop => 86,
                Forms.Keys.MediaNextTrack => 87,
                Forms.Keys.MediaPreviousTrack => 88,
                Forms.Keys.Apps => 82,
                _ => 0
            };

        return androidKeycode != 0;
    }

    public static uint GetMetaStateVE(
        Forms.Keys keyData)
    {
        const uint MetaShiftOnVE = 0x00000001;
        const uint MetaAltOnVE = 0x00000002;
        const uint MetaCtrlOnVE = 0x00001000;
        const uint MetaMetaOnVE = 0x00010000;

        uint result =
            0;

        if ((keyData & Forms.Keys.Shift) != 0)
        {
            result |=
                MetaShiftOnVE;
        }

        if ((keyData & Forms.Keys.Alt) != 0)
        {
            result |=
                MetaAltOnVE;
        }

        if ((keyData & Forms.Keys.Control) != 0)
        {
            result |=
                MetaCtrlOnVE;
        }

        if ((Forms.Control.ModifierKeys & Forms.Keys.LWin) != 0 ||
            (Forms.Control.ModifierKeys & Forms.Keys.RWin) != 0)
        {
            result |=
                MetaMetaOnVE;
        }

        return result;
    }
}

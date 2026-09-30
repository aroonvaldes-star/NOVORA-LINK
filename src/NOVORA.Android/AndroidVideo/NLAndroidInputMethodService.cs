using Android.App;
using Android.Content;
using Android.InputMethodServices;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Views.InputMethods;
using NOVORA.Control;

namespace NOVORA.AndroidVideo;

[Service(Name = "com.novora.appcontrol.NovoraInputMethodService", Exported = true,
    Permission = "android.permission.BIND_INPUT_METHOD", Label = "Teclado NOVORA")]
[IntentFilter(["android.view.InputMethod"])]
[MetaData("android.view.im", Resource = "@xml/novora_input_method")]
public sealed class NLAndroidInputMethodService : InputMethodService
{
    private static readonly object Gate = new();
    private static WeakReference<NLAndroidInputMethodService>? _current;

    public override void OnCreate()
    {
        base.OnCreate();
        lock (Gate) _current = new(this);
    }

    public override void OnDestroy()
    {
        lock (Gate)
        {
            if (_current is not null && _current.TryGetTarget(out var service) &&
                ReferenceEquals(service, this))
                _current = null;
        }
        base.OnDestroy();
    }

    public override View? OnCreateInputView() => null;

    public static bool IsEnabled(Context context)
    {
        var manager = (InputMethodManager?)context.GetSystemService(InputMethodService);
        return manager?.EnabledInputMethodList?.Any(IsNovora) == true;
    }

    public static bool IsSelected(Context context)
    {
        string? selected = Settings.Secure.GetString(
            context.ContentResolver, Settings.Secure.DefaultInputMethod);
        return selected?.Contains("NovoraInputMethodService", StringComparison.Ordinal) == true;
    }

    public static bool TryExecute(NLControlInputCommand command)
    {
        NLAndroidInputMethodService service;
        lock (Gate)
        {
            if (_current is null || !_current.TryGetTarget(out service!))
                return false;
        }

        return service.Execute(command);
    }

    private bool Execute(NLControlInputCommand command)
    {
        IInputConnection? connection = CurrentInputConnection;
        if (connection is null)
            return false;

        if (command.Type == 1)
            return connection.CommitText(command.Text ?? string.Empty, 1);

        if (command.Type == 9 && command.Paste)
            return connection.CommitText(command.Text ?? string.Empty, 1);

        if (command.Type is not (0 or 4))
            return false;

        if (command.Keycode == 67 && command.KeyAction == 1)
            return connection.DeleteSurroundingText(1, 0);
        if (command.Keycode == 112 && command.KeyAction == 1)
            return connection.DeleteSurroundingText(0, 1);

        KeyEventActions action = command.KeyAction == 0
            ? KeyEventActions.Down
            : KeyEventActions.Up;
        using var keyEvent = new KeyEvent(action, (Android.Views.Keycode)command.Keycode);
        return connection.SendKeyEvent(keyEvent);
    }

    private static bool IsNovora(InputMethodInfo info) =>
        info.ServiceInfo?.Name?.Contains(
            "NovoraInputMethodService", StringComparison.Ordinal) == true;
}

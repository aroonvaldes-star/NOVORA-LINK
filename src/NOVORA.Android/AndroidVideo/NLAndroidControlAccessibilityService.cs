using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views.Accessibility;
using NOVORA.Control;
using Android.Provider;

namespace NOVORA.AndroidVideo;

[Service(Name = "com.novora.appcontrol.ControlAccessibilityService", Exported = true,
    Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE")]
[IntentFilter(["android.accessibilityservice.AccessibilityService"])]
[MetaData("android.accessibilityservice", Resource = "@xml/novora_accessibility")]
public sealed class NLAndroidControlAccessibilityService : AccessibilityService
{
    private static readonly object Gate = new();
    private static WeakReference<NLAndroidControlAccessibilityService>? _current;
    private readonly Dictionary<ulong, PointF> _pointers = [];

    protected override void OnServiceConnected()
    {
        base.OnServiceConnected();
        lock (Gate) _current = new(this);
    }

    public override void OnAccessibilityEvent(AccessibilityEvent? e) { }
    public override void OnInterrupt() { }

    public override void OnDestroy()
    {
        lock (Gate)
        {
            if (_current is not null && _current.TryGetTarget(out var service) && ReferenceEquals(service, this))
                _current = null;
        }
        base.OnDestroy();
    }

    public static Task ExecuteAsync(NLControlInputCommand command, CancellationToken cancellationToken)
    {
        NLAndroidControlAccessibilityService service;
        lock (Gate)
        {
            if (_current is null || !_current.TryGetTarget(out service!))
                throw new InvalidOperationException("Activa Control de NOVORA en Accesibilidad de Android.");
        }
        return service.ExecuteOnMainAsync(command, cancellationToken);
    }

    public static bool IsEnabled(Context context)
    {
        string component = new ComponentName(context,
            Java.Lang.Class.FromType(typeof(NLAndroidControlAccessibilityService)))
            .FlattenToString();
        string? enabled = Settings.Secure.GetString(
            context.ContentResolver, Settings.Secure.EnabledAccessibilityServices);
        return enabled?.Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Any(value => string.Equals(value, component, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private Task ExecuteOnMainAsync(NLControlInputCommand command, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        new Handler(Looper.MainLooper!).Post(() =>
        {
            try
            {
                Execute(command, completion);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        return completion.Task;
    }

    private void Execute(NLControlInputCommand command, TaskCompletionSource completion)
    {
        switch (command.Type)
        {
            case 0:
            case 4:
                ExecuteKey(command);
                completion.SetResult();
                return;
            case 1:
                SetFocusedText(command.Text ?? string.Empty);
                completion.SetResult();
                return;
            case 2:
                ExecuteTouch(command, completion);
                return;
            case 3:
                ExecuteScroll(command, completion);
                return;
            case 5:
                RequireGlobalAction(GlobalAction.Notifications, "panel de notificaciones");
                completion.SetResult();
                return;
            case 6:
                RequireGlobalAction(GlobalAction.QuickSettings, "ajustes rápidos");
                completion.SetResult();
                return;
            case 7:
                RequireGlobalAction(GlobalAction.Back, "cerrar paneles");
                completion.SetResult();
                return;
            case 9:
                SetClipboard(command.Text ?? string.Empty);
                if (command.Paste) SetFocusedText(command.Text ?? string.Empty);
                completion.SetResult();
                return;
            case 15:
                StartActivity(new Intent(Android.Provider.Settings.ActionHardKeyboardSettings)
                    .AddFlags(ActivityFlags.NewTask));
                completion.SetResult();
                return;
            case 16:
                StartApp(command.Name);
                completion.SetResult();
                return;
            case 17:
                completion.SetResult();
                return;
            case 22:
                if (!string.IsNullOrWhiteSpace(command.Text))
                    Android.Media.MediaScannerConnection.ScanFile(this, [command.Text], null, null);
                completion.SetResult();
                return;
            default:
                throw new NotSupportedException($"La orden de control {command.Type} no está disponible en AppControl.");
        }
    }

    private void ExecuteKey(NLControlInputCommand command)
    {
        if (command.KeyAction != 1) return;
        GlobalAction? action = command.Keycode switch
        {
            3 => GlobalAction.Home,
            4 => GlobalAction.Back,
            187 => GlobalAction.Recents,
            _ when command.Type == 4 => GlobalAction.Back,
            _ => null
        };
        if (action is null)
            throw new NotSupportedException($"Android no permite inyectar la tecla {command.Keycode} desde Accesibilidad.");
        RequireGlobalAction(action.Value, "tecla global");
    }

    private void ExecuteTouch(NLControlInputCommand command, TaskCompletionSource completion)
    {
        PointF point = Scale(command);
        if (command.MotionAction is 0 or 5)
        {
            _pointers[command.PointerId] = point;
            completion.SetResult();
            return;
        }
        if (command.MotionAction == 2)
        {
            if (!_pointers.ContainsKey(command.PointerId)) _pointers[command.PointerId] = point;
            completion.SetResult();
            return;
        }
        if (command.MotionAction is not (1 or 6))
        {
            _pointers.Remove(command.PointerId);
            completion.SetResult();
            return;
        }

        PointF start = _pointers.Remove(command.PointerId, out PointF? saved) ? saved ?? point : point;
        DispatchPath(start, point, start == point ? 1 : 120, completion);
    }

    private void ExecuteScroll(NLControlInputCommand command, TaskCompletionSource completion)
    {
        PointF start = Scale(command);
        float distance = Math.Max(120, Resources?.DisplayMetrics?.HeightPixels / 4f ?? 300);
        PointF end = new(start.X, Math.Clamp(start.Y + Math.Sign(command.VerticalScroll) * distance,
            0, (Resources?.DisplayMetrics?.HeightPixels ?? 1) - 1));
        DispatchPath(start, end, 140, completion);
    }

    private void DispatchPath(PointF start, PointF end, long durationMs, TaskCompletionSource completion)
    {
        using var path = new Android.Graphics.Path();
        path.MoveTo(start.X, start.Y);
        if (start != end) path.LineTo(end.X, end.Y);
        using var builder = new GestureDescription.Builder();
        builder.AddStroke(new GestureDescription.StrokeDescription(path, 0, durationMs));
        using GestureDescription gesture = builder.Build()
            ?? throw new InvalidOperationException("Android no creó el gesto de NOVORA.");
        if (!DispatchGesture(gesture, new GestureCallback(completion), new Handler(Looper.MainLooper!)))
            throw new InvalidOperationException("Android rechazó el gesto de NOVORA.");
    }

    private PointF Scale(NLControlInputCommand command)
    {
        var metrics = Resources?.DisplayMetrics ?? throw new InvalidOperationException("Pantalla no disponible.");
        if (command.ScreenWidth == 0 || command.ScreenHeight == 0)
            throw new InvalidDataException("La orden no incluye el tamaño de la pantalla.");
        return new PointF(
            Math.Clamp(command.X * metrics.WidthPixels / (float)command.ScreenWidth, 0, metrics.WidthPixels - 1),
            Math.Clamp(command.Y * metrics.HeightPixels / (float)command.ScreenHeight, 0, metrics.HeightPixels - 1));
    }

    private void SetFocusedText(string text)
    {
        AccessibilityNodeInfo? node = RootInActiveWindow?.FindFocus(NodeFocus.Input);
        if (node is null) throw new InvalidOperationException("No hay un campo de texto activo.");
        using (node)
        using (var args = new Bundle())
        {
            args.PutCharSequence(AccessibilityNodeInfo.ActionArgumentSetTextCharsequence,
                new Java.Lang.String(text));
            if (!node.PerformAction(Android.Views.Accessibility.Action.SetText, args))
                throw new InvalidOperationException("La aplicación no aceptó el texto.");
        }
    }

    private void SetClipboard(string text)
    {
        var clipboard = (ClipboardManager)GetSystemService(ClipboardService)!;
        clipboard.PrimaryClip = ClipData.NewPlainText("NOVORA", text);
    }

    private void StartApp(string? packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName)) throw new InvalidDataException("Paquete Android vacío.");
        Intent intent = PackageManager?.GetLaunchIntentForPackage(packageName)
            ?? throw new InvalidOperationException("La aplicación no está instalada.");
        StartActivity(intent.AddFlags(ActivityFlags.NewTask));
    }

    private void RequireGlobalAction(GlobalAction action, string label)
    {
        if (!PerformGlobalAction(action))
            throw new InvalidOperationException($"Android rechazó la acción {label}.");
    }

    private sealed class GestureCallback(TaskCompletionSource completion) : GestureResultCallback
    {
        public override void OnCompleted(GestureDescription? gestureDescription) => completion.TrySetResult();
        public override void OnCancelled(GestureDescription? gestureDescription) =>
            completion.TrySetException(new InvalidOperationException("Android canceló el gesto."));
    }
}

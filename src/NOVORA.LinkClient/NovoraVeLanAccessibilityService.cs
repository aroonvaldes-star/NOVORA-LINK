using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Android.Views.Accessibility;
using NOVORA.Control;

namespace NOVORA.LinkClient;

[Service(Name = "com.novora.linkclient.VeAccessibilityService", Exported = true,
    Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE")]
[IntentFilter(["android.accessibilityservice.AccessibilityService"])]
[MetaData("android.accessibilityservice", Resource = "@xml/novora_link_accessibility")]
internal sealed class NovoraVeLanAccessibilityService : AccessibilityService
{
    private static readonly object Gate = new();
    private static WeakReference<NovoraVeLanAccessibilityService>? _current;
    private readonly Dictionary<ulong, PointerGesture> _pointers = [];

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
            if (_current is not null && _current.TryGetTarget(out var service) &&
                ReferenceEquals(service, this))
                _current = null;
        }
        ResetPointers();
        base.OnDestroy();
    }

    internal static async Task<NLControlInputResponse?> ExecuteWithResponseAsync(
        NLControlInputCommand command,
        CancellationToken cancellationToken)
    {
        NovoraVeLanAccessibilityService service = GetCurrent();
        if (command.Type == 8)
            return await service.ReadClipboardOnMainAsync(
                command.CopyKey == 1, cancellationToken).ConfigureAwait(false);
        await service.ExecuteOnMainAsync(command, cancellationToken).ConfigureAwait(false);
        return command.Type == 9
            ? new NLControlInputResponse(
                NLControlInputResponse.ClipboardAckType, Sequence: command.Sequence)
            : null;
    }

    internal static Task ResetPointersAsync(CancellationToken cancellationToken)
    {
        lock (Gate)
        {
            if (_current is null || !_current.TryGetTarget(out var service))
                return Task.CompletedTask;
            return service.ResetPointersOnMainAsync(cancellationToken);
        }
    }

    internal static bool IsEnabled(Context context)
    {
        string component = new ComponentName(context,
            Java.Lang.Class.FromType(typeof(NovoraVeLanAccessibilityService)))
            .FlattenToString();
        string? enabled = Settings.Secure.GetString(
            context.ContentResolver, Settings.Secure.EnabledAccessibilityServices);
        return enabled?.Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Any(value => string.Equals(value, component, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static NovoraVeLanAccessibilityService GetCurrent()
    {
        lock (Gate)
        {
            if (_current is null || !_current.TryGetTarget(out var service))
                throw new InvalidOperationException(
                    "Activa Control de NOVORA-LINK en Accesibilidad de Android.");
            return service;
        }
    }

    private Task ExecuteOnMainAsync(
        NLControlInputCommand command,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        new Handler(Looper.MainLooper!).Post(() =>
        {
            try { Execute(command, completion); }
            catch (Exception ex) { completion.TrySetException(ex); }
        });
        return completion.Task;
    }

    private Task ResetPointersOnMainAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        new Handler(Looper.MainLooper!).Post(() =>
        {
            ResetPointers();
            completion.TrySetResult();
        });
        return completion.Task;
    }

    private Task<NLControlInputResponse?> ReadClipboardOnMainAsync(
        bool copyFocusedSelection,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<NLControlInputResponse?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        new Handler(Looper.MainLooper!).Post(() =>
        {
            try { completion.TrySetResult(ReadClipboard(copyFocusedSelection)); }
            catch (Exception ex) { completion.TrySetException(ex); }
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
                break;
            case 1:
                EditFocusedText(command.Text ?? string.Empty);
                completion.SetResult();
                break;
            case 2:
                ExecuteTouch(command, completion);
                break;
            case 3:
                ExecuteScroll(command, completion);
                break;
            case 5:
                RequireGlobalAction(GlobalAction.Notifications);
                completion.SetResult();
                break;
            case 6:
                RequireGlobalAction(GlobalAction.QuickSettings);
                completion.SetResult();
                break;
            case 7:
                RequireGlobalAction(GlobalAction.Back);
                completion.SetResult();
                break;
            case 9:
                SetClipboard(command.Text ?? string.Empty);
                if (command.Paste) EditFocusedText(command.Text ?? string.Empty);
                completion.SetResult();
                break;
            case 15:
                StartActivity(new Intent(Settings.ActionHardKeyboardSettings)
                    .AddFlags(ActivityFlags.NewTask));
                completion.SetResult();
                break;
            case 16:
                StartApp(command.Name);
                completion.SetResult();
                break;
            case 17:
                completion.SetResult();
                break;
            case 22:
                if (!string.IsNullOrWhiteSpace(command.Text))
                    Android.Media.MediaScannerConnection.ScanFile(
                        this, [command.Text], null, null);
                completion.SetResult();
                break;
            default:
                throw new NotSupportedException(
                    $"La orden de control {command.Type} no está disponible en NOVORA-LINK.");
        }
    }

    private void ExecuteKey(NLControlInputCommand command)
    {
        if (command.KeyAction != 1) return;
        if (command.Keycode == 67) { EditFocusedText("\b"); return; }
        if (command.Keycode == 112) { EditFocusedText("\u007f"); return; }
        if (command.Keycode == 66)
        {
            using AccessibilityNodeInfo? node = RootInActiveWindow?.FindFocus(NodeFocus.Input);
            if (node is null || !node.PerformAction((Android.Views.Accessibility.Action)16908372))
                throw new InvalidOperationException("El campo activo no aceptó Enter.");
            return;
        }
        GlobalAction? action = command.Keycode switch
        {
            3 => GlobalAction.Home,
            4 => GlobalAction.Back,
            187 => GlobalAction.Recents,
            _ when command.Type == 4 => GlobalAction.Back,
            _ => null
        };
        if (action is null)
            throw new NotSupportedException($"Android no permite inyectar la tecla {command.Keycode}.");
        RequireGlobalAction(action.Value);
    }

    private void ExecuteTouch(NLControlInputCommand command, TaskCompletionSource completion)
    {
        PointF point = Scale(command);
        if (command.MotionAction is 0 or 5)
        {
            if (_pointers.Remove(command.PointerId, out PointerGesture? previous))
                previous.Stroke.Dispose();
            var stroke = CreateStroke(point, point, 1, true);
            _pointers[command.PointerId] = new(point, stroke);
            DispatchStroke(stroke, completion);
            return;
        }
        if (command.MotionAction == 2)
        {
            if (!_pointers.TryGetValue(command.PointerId, out PointerGesture? pointer))
            {
                var initial = CreateStroke(point, point, 1, true);
                _pointers[command.PointerId] = new(point, initial);
                DispatchStroke(initial, completion);
                return;
            }
            using Android.Graphics.Path path = CreatePath(pointer.Point, point);
            var continuation = pointer.Stroke.ContinueStroke(path, 0, 8, true)
                ?? throw new InvalidOperationException("Android no continuó el gesto.");
            pointer.Stroke.Dispose();
            _pointers[command.PointerId] = new(point, continuation);
            DispatchStroke(continuation, completion);
            return;
        }
        if (command.MotionAction is not (1 or 6))
        {
            if (_pointers.Remove(command.PointerId, out PointerGesture? cancelled))
                cancelled.Stroke.Dispose();
            completion.SetResult();
            return;
        }
        if (!_pointers.Remove(command.PointerId, out PointerGesture? active))
        {
            DispatchPath(point, point, 1, completion);
            return;
        }
        using Android.Graphics.Path finalPath = CreatePath(active.Point, point);
        var final = active.Stroke.ContinueStroke(finalPath, 0, 8, false)
            ?? throw new InvalidOperationException("Android no cerró el gesto.");
        active.Stroke.Dispose();
        DispatchStroke(final, completion, disposeAfterDispatch: true);
    }

    private void ExecuteScroll(NLControlInputCommand command, TaskCompletionSource completion)
    {
        PointF start = Scale(command);
        float distance = Math.Max(120, Resources?.DisplayMetrics?.HeightPixels / 4f ?? 300);
        PointF end = new(start.X, Math.Clamp(
            start.Y + Math.Sign(command.VerticalScroll) * distance,
            0, (Resources?.DisplayMetrics?.HeightPixels ?? 1) - 1));
        DispatchPath(start, end, 140, completion);
    }

    private PointF Scale(NLControlInputCommand command)
    {
        var metrics = Resources?.DisplayMetrics
            ?? throw new InvalidOperationException("Pantalla no disponible.");
        if (command.ScreenWidth == 0 || command.ScreenHeight == 0)
            throw new InvalidDataException("La orden no incluye el tamaño de pantalla.");
        (float x, float y) = NLControlPointerGeometry.MapToCurrentOrientation(
            command.X, command.Y, command.ScreenWidth, command.ScreenHeight,
            metrics.WidthPixels, metrics.HeightPixels);
        return new PointF(Math.Clamp(x, 0, metrics.WidthPixels - 1),
            Math.Clamp(y, 0, metrics.HeightPixels - 1));
    }

    private void EditFocusedText(string input)
    {
        using AccessibilityNodeInfo? root = RootInActiveWindow;
        using AccessibilityNodeInfo? node = root?.FindFocus(NodeFocus.Input);
        if (node is null || !node.Editable)
            throw new InvalidOperationException("No hay un campo editable enfocado.");
        string current = NLControlAndroidInput.GetEditableText(
            node.Text?.ToString(), node.HintText?.ToString(), node.ShowingHintText);
        string text = NLControlAndroidInput.EditText(
            current, node.TextSelectionStart, node.TextSelectionEnd, input);
        using var args = new Bundle();
        args.PutCharSequence(AccessibilityNodeInfo.ActionArgumentSetTextCharsequence,
            new Java.Lang.String(text));
        if (!node.PerformAction(Android.Views.Accessibility.Action.SetText, args))
            throw new InvalidOperationException("Android rechazó la edición del campo.");
    }

    private void SetClipboard(string text)
    {
        var clipboard = (ClipboardManager)GetSystemService(ClipboardService)!;
        clipboard.PrimaryClip = ClipData.NewPlainText("NOVORA", text);
    }

    private NLControlInputResponse ReadClipboard(bool copyFocusedSelection)
    {
        if (copyFocusedSelection)
        {
            using AccessibilityNodeInfo? node = RootInActiveWindow?.FindFocus(NodeFocus.Input);
            node?.PerformAction(Android.Views.Accessibility.Action.Copy);
        }
        var clipboard = (ClipboardManager)GetSystemService(ClipboardService)!;
        string text = clipboard.PrimaryClip?.GetItemAt(0)?.CoerceToText(this)?.ToString()
            ?? string.Empty;
        return new NLControlInputResponse(NLControlInputResponse.ClipboardType, Text: text);
    }

    private void StartApp(string? packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName))
            throw new InvalidDataException("Paquete Android vacío.");
        Intent intent = PackageManager?.GetLaunchIntentForPackage(packageName)
            ?? throw new InvalidOperationException("La aplicación no está instalada.");
        StartActivity(intent.AddFlags(ActivityFlags.NewTask));
    }

    private void RequireGlobalAction(GlobalAction action)
    {
        if (!PerformGlobalAction(action))
            throw new InvalidOperationException("Android rechazó la acción global.");
    }

    private void ResetPointers()
    {
        foreach (PointerGesture pointer in _pointers.Values) pointer.Stroke.Dispose();
        _pointers.Clear();
    }

    private void DispatchPath(
        PointF start, PointF end, long durationMs, TaskCompletionSource completion)
    {
        using Android.Graphics.Path path = CreatePath(start, end);
        using var stroke = new GestureDescription.StrokeDescription(path, 0, durationMs);
        DispatchStroke(stroke, completion);
    }

    private static GestureDescription.StrokeDescription CreateStroke(
        PointF start, PointF end, long durationMs, bool willContinue)
    {
        using Android.Graphics.Path path = CreatePath(start, end);
        return new GestureDescription.StrokeDescription(path, 0, durationMs, willContinue);
    }

    private static Android.Graphics.Path CreatePath(PointF start, PointF end)
    {
        var path = new Android.Graphics.Path();
        path.MoveTo(start.X, start.Y);
        if (start != end) path.LineTo(end.X, end.Y);
        return path;
    }

    private void DispatchStroke(
        GestureDescription.StrokeDescription stroke,
        TaskCompletionSource completion,
        bool disposeAfterDispatch = false)
    {
        using var builder = new GestureDescription.Builder();
        builder.AddStroke(stroke);
        using GestureDescription gesture = builder.Build()
            ?? throw new InvalidOperationException("Android no creó el gesto.");
        if (!DispatchGesture(gesture,
                new GestureCallback(completion, disposeAfterDispatch ? stroke : null),
                new Handler(Looper.MainLooper!)))
            throw new InvalidOperationException("Android rechazó el gesto.");
    }

    private sealed record PointerGesture(
        PointF Point, GestureDescription.StrokeDescription Stroke);

    private sealed class GestureCallback(
        TaskCompletionSource completion,
        GestureDescription.StrokeDescription? dispose) : GestureResultCallback
    {
        public override void OnCompleted(GestureDescription? gestureDescription)
        {
            dispose?.Dispose();
            completion.TrySetResult();
        }

        public override void OnCancelled(GestureDescription? gestureDescription)
        {
            dispose?.Dispose();
            completion.TrySetException(new InvalidOperationException("Android canceló el gesto."));
        }
    }
}

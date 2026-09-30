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

    public static async Task<NLControlInputResponse?> ExecuteWithResponseAsync(
        NLControlInputCommand command, CancellationToken cancellationToken)
    {
        if (command.Type is 0 or 1 or 4 &&
            NLAndroidInputMethodService.TryExecute(command))
            return null;

        NLAndroidControlAccessibilityService service;
        lock (Gate)
        {
            if (_current is null || !_current.TryGetTarget(out service!))
                throw new InvalidOperationException("Activa Control de NOVORA en Accesibilidad de Android.");
        }

        if (command.Type == 8)
            return await service.ReadClipboardOnMainAsync(command.CopyKey == 1, cancellationToken);

        await service.ExecuteOnMainAsync(command, cancellationToken);
        return command.Type == 9
            ? new NLControlInputResponse(NLControlInputResponse.ClipboardAckType, Sequence: command.Sequence)
            : null;
    }

    public static Task ResetPointersAsync(CancellationToken cancellationToken)
    {
        NLAndroidControlAccessibilityService service;
        lock (Gate)
        {
            if (_current is null || !_current.TryGetTarget(out service!))
                return Task.CompletedTask;
        }
        return service.ResetPointersOnMainAsync(cancellationToken);
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

    private Task ResetPointersOnMainAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        new Handler(Looper.MainLooper!).Post(() =>
        {
            ResetPointers();
            completion.TrySetResult();
        });
        return completion.Task;
    }

    private Task<NLControlInputResponse?> ReadClipboardOnMainAsync(
        bool copyFocusedSelection, CancellationToken cancellationToken)
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

    private void ResetPointers()
    {
        foreach (PointerGesture pointer in _pointers.Values)
            pointer.Stroke.Dispose();
        _pointers.Clear();
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
        if (command.Keycode == 67)
        {
            EditFocusedText("\b");
            return;
        }
        if (command.Keycode == 112)
        {
            EditFocusedText("\u007f");
            return;
        }
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
            throw new NotSupportedException($"Android no permite inyectar la tecla {command.Keycode} desde Accesibilidad.");
        RequireGlobalAction(action.Value, "tecla global");
    }

    private void ExecuteTouch(NLControlInputCommand command, TaskCompletionSource completion)
    {
        PointF point = Scale(command);
        if (command.MotionAction is 0 or 5)
        {
            if (_pointers.Remove(command.PointerId, out PointerGesture? previous))
                previous.Stroke.Dispose();
            GestureDescription.StrokeDescription stroke = CreateStroke(point, point, 1, true);
            _pointers[command.PointerId] = new PointerGesture(point, stroke);
            DispatchStroke(stroke, completion);
            return;
        }
        if (command.MotionAction == 2)
        {
            if (!_pointers.TryGetValue(command.PointerId, out PointerGesture? pointer))
            {
                GestureDescription.StrokeDescription initial = CreateStroke(point, point, 1, true);
                _pointers[command.PointerId] = new PointerGesture(point, initial);
                DispatchStroke(initial, completion);
                return;
            }

            using Android.Graphics.Path continuationPath = CreatePath(pointer.Point, point);
            GestureDescription.StrokeDescription continuation = pointer.Stroke.ContinueStroke(
                continuationPath, 0, 8, true)
                ?? throw new InvalidOperationException("Android no continuó el gesto de NOVORA.");
            pointer.Stroke.Dispose();
            _pointers[command.PointerId] = new PointerGesture(point, continuation);
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
        GestureDescription.StrokeDescription final = active.Stroke.ContinueStroke(
            finalPath, 0, 8, false)
            ?? throw new InvalidOperationException("Android no cerró el gesto de NOVORA.");
        active.Stroke.Dispose();
        DispatchStroke(final, completion, disposeAfterDispatch: true);
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
        if (start != end)
            path.LineTo(end.X, end.Y);
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
            ?? throw new InvalidOperationException("Android no creó el gesto de NOVORA.");
        if (!DispatchGesture(gesture,
                new GestureCallback(completion, disposeAfterDispatch ? stroke : null),
                new Handler(Looper.MainLooper!)))
            throw new InvalidOperationException("Android rechazó el gesto de NOVORA.");
    }

    private PointF Scale(NLControlInputCommand command)
    {
        var metrics = Resources?.DisplayMetrics ?? throw new InvalidOperationException("Pantalla no disponible.");
        if (command.ScreenWidth == 0 || command.ScreenHeight == 0)
            throw new InvalidDataException("La orden no incluye el tamaño de la pantalla.");
        if (!NLControlPointerGeometry.MatchesCurrentOrientation(
                command.ScreenWidth, command.ScreenHeight,
                metrics.WidthPixels, metrics.HeightPixels))
            throw new InvalidOperationException(
                "La geometría del puntero pertenece a una orientación anterior.");
        return new PointF(
            Math.Clamp(command.X * metrics.WidthPixels / (float)command.ScreenWidth, 0, metrics.WidthPixels - 1),
            Math.Clamp(command.Y * metrics.HeightPixels / (float)command.ScreenHeight, 0, metrics.HeightPixels - 1));
    }

    private void SetFocusedText(string text) => EditFocusedText(text);

    private void EditFocusedText(string input)
    {
        using AccessibilityNodeInfo? root = RootInActiveWindow;
        if (root is null)
            throw new InvalidOperationException("No hay una ventana Android activa.");

        using AccessibilityNodeInfo? inputFocus = root.FindFocus(NodeFocus.Input);
        if (TryEditNode(inputFocus, input))
            return;

        using AccessibilityNodeInfo? accessibilityFocus =
            root.FindFocus(NodeFocus.Accessibility);
        if (TryEditNode(accessibilityFocus, input))
            return;

        if (TryEditFocusedDescendant(root, input))
            return;

        throw new InvalidOperationException(
            "La aplicación activa no expuso un campo editable enfocado a Accesibilidad.");
    }

    private static bool TryEditFocusedDescendant(
        AccessibilityNodeInfo root,
        string input)
    {
        var pending = new Queue<AccessibilityNodeInfo>();
        for (int index = 0; index < root.ChildCount; index++)
        {
            AccessibilityNodeInfo? child = root.GetChild(index);
            if (child is not null)
                pending.Enqueue(child);
        }

        while (pending.Count > 0)
        {
            using AccessibilityNodeInfo node = pending.Dequeue();
            if ((node.Focused || node.AccessibilityFocused) &&
                TryEditNode(node, input))
            {
                while (pending.TryDequeue(out AccessibilityNodeInfo? remaining))
                    remaining.Dispose();
                return true;
            }

            for (int index = 0; index < node.ChildCount; index++)
            {
                AccessibilityNodeInfo? child = node.GetChild(index);
                if (child is not null)
                    pending.Enqueue(child);
            }
        }

        return false;
    }

    private static bool TryEditNode(
        AccessibilityNodeInfo? node,
        string input)
    {
        if (node is null ||
            (!node.Editable &&
             node.ActionList?.Any(action =>
                 action is not null &&
                 action.Id == AccessibilityNodeInfo.AccessibilityAction.ActionSetText?.Id) != true))
        {
            return false;
        }

        string current = NLControlAndroidInput.GetEditableText(
            node.Text?.ToString(),
            node.HintText?.ToString(),
            node.ShowingHintText);
        string text = NLControlAndroidInput.EditText(
            current,
            node.TextSelectionStart,
            node.TextSelectionEnd,
            input);

        using var args = new Bundle();
        args.PutCharSequence(
            AccessibilityNodeInfo.ActionArgumentSetTextCharsequence,
            new Java.Lang.String(text));
        return node.PerformAction(Android.Views.Accessibility.Action.SetText, args);
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
        string text = clipboard.PrimaryClip?.GetItemAt(0)?.CoerceToText(this)?.ToString() ?? string.Empty;
        return new NLControlInputResponse(NLControlInputResponse.ClipboardType, Text: text);
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

    private sealed record PointerGesture(
        PointF Point,
        GestureDescription.StrokeDescription Stroke);

    private sealed class GestureCallback(
        TaskCompletionSource completion,
        GestureDescription.StrokeDescription? dispose) : GestureResultCallback
    {
        public override void OnCompleted(GestureDescription? gestureDescription)
        {
            dispose?.Dispose();
            completion.TrySetResult();
        }

        public override void OnCancelled(GestureDescription? gestureDescription) =>
            CompleteCancelled();

        private void CompleteCancelled()
        {
            dispose?.Dispose();
            completion.TrySetException(new InvalidOperationException("Android canceló el gesto."));
        }
    }
}

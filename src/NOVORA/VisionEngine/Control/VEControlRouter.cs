using NOVORA.VisionEngine.Renderer;
using Forms = System.Windows.Forms;

namespace NOVORA.VisionEngine.Control;

/// <summary>
/// Enruta input de la superficie Win32 de VisionEngine hacia el control
/// protocol compatible con scrcpy 4.1.
///
/// El mouse se transforma desde el viewport con letterboxing a coordenadas
/// reales del frame Android. Los movimientos se limitan a 125 Hz para no
/// inundar el socket de control ni competir innecesariamente con video/red.
/// </summary>
public sealed class VEControlRouter : IDisposable
{
    public const long MouseMoveIntervalMsVE = 8;
    internal const int VerticalEdgeActivationPixelsVE = 12;

    private readonly VEControlManager _controlVE;
    private readonly VERendererManager _rendererVE;
    private readonly VEControlMouse _mouseVE;
    private readonly VEControlKeyboard _keyboardVE;
    private readonly object _queueGateVE = new();
    private readonly object _stateGateVE = new();
    private readonly CancellationTokenSource _disposeCtsVE = new();
    private readonly HashSet<Forms.Keys> _heldKeysVE = [];

    private VERendererHost? _hostVE;
    private Task _queueTailVE = Task.CompletedTask;
    private uint _buttonsVE;
    private long _lastMouseMoveAtVE;
    private VEControlPosition? _lastPositionVE;
    private bool _disposedVE;

    public VEControlRouter(
        VEControlManager control,
        VERendererManager renderer)
    {
        _controlVE =
            control
            ?? throw new ArgumentNullException(
                nameof(control));

        _rendererVE =
            renderer
            ?? throw new ArgumentNullException(
                nameof(renderer));

        _mouseVE =
            new VEControlMouse(
                _controlVE);

        _keyboardVE =
            new VEControlKeyboard(
                _controlVE);
    }

    public event EventHandler<string>? InputErrorVE;

    public void AttachVE(
        VERendererHost host)
    {
        ThrowIfDisposedVE();
        ArgumentNullException.ThrowIfNull(host);

        if (ReferenceEquals(
                _hostVE,
                host))
        {
            host.FocusInputVE();
            return;
        }

        DetachVE();

        _hostVE =
            host;

        host.MouseDownVE +=
            Host_MouseDownVE;

        host.MouseMoveVE +=
            Host_MouseMoveVE;

        host.MouseUpVE +=
            Host_MouseUpVE;

        host.MouseWheelVE +=
            Host_MouseWheelVE;

        host.KeyDownVE +=
            Host_KeyDownVE;

        host.KeyUpVE +=
            Host_KeyUpVE;

        host.InputFocusLostVE +=
            Host_InputFocusLostVE;

        host.FocusInputVE();
    }

    public void DetachVE()
    {
        VERendererHost? host =
            _hostVE;

        _hostVE =
            null;

        if (host is not null)
        {
            host.MouseDownVE -=
                Host_MouseDownVE;

            host.MouseMoveVE -=
                Host_MouseMoveVE;

            host.MouseUpVE -=
                Host_MouseUpVE;

            host.MouseWheelVE -=
                Host_MouseWheelVE;

            host.KeyDownVE -=
                Host_KeyDownVE;

            host.KeyUpVE -=
                Host_KeyUpVE;

            host.InputFocusLostVE -=
                Host_InputFocusLostVE;
        }

        lock (_stateGateVE)
        {
            _heldKeysVE.Clear();
            _buttonsVE = 0;
            _lastPositionVE = null;
        }
    }

    private void Host_MouseDownVE(
        object? sender,
        Forms.MouseEventArgs e)
    {
        if (!_controlVE.IsReadyVE ||
            !TryMapPositionVE(
                e.X,
                e.Y,
                out VEControlPosition position,
                activateVerticalEdges: true))
        {
            return;
        }

        uint actionButton =
            GetMouseButtonVE(
                e.Button);

        if (actionButton == 0)
        {
            return;
        }

        uint buttons;
        VEControlActionMotion action;

        lock (_stateGateVE)
        {
            bool hadButtons =
                _buttonsVE != 0;

            _buttonsVE |=
                actionButton;

            buttons =
                _buttonsVE;

            _lastPositionVE =
                position;

            action =
                hadButtons
                    ? VEControlActionMotion.ButtonPress
                    : VEControlActionMotion.Down;
        }

        EnqueueVE(
            cancellationToken =>
                _mouseVE.PointerAsync(
                    action,
                    position.X,
                    position.Y,
                    position.ScreenWidth,
                    position.ScreenHeight,
                    actionButton,
                    buttons,
                    cancellationToken));
    }

    private void Host_MouseMoveVE(
        object? sender,
        Forms.MouseEventArgs e)
    {
        if (!_controlVE.IsReadyVE)
        {
            return;
        }

        long now =
            Environment.TickCount64;

        long previous =
            Interlocked.Read(
                ref _lastMouseMoveAtVE);

        if (previous != 0 &&
            now - previous <
                MouseMoveIntervalMsVE)
        {
            return;
        }

        uint buttons;

        lock (_stateGateVE)
        {
            buttons =
                _buttonsVE;
        }

        if (!TryMapPositionVE(
                e.X,
                e.Y,
                out VEControlPosition position,
                clampToViewport: buttons != 0))
        {
            return;
        }

        Interlocked.Exchange(
            ref _lastMouseMoveAtVE,
            now);

        lock (_stateGateVE)
        {
            _lastPositionVE =
                position;
        }

        VEControlActionMotion action =
            buttons == 0
                ? VEControlActionMotion.HoverMove
                : VEControlActionMotion.Move;

        EnqueueVE(
            cancellationToken =>
                _mouseVE.PointerAsync(
                    action,
                    position.X,
                    position.Y,
                    position.ScreenWidth,
                    position.ScreenHeight,
                    actionButton: 0,
                    buttons: buttons,
                    cancellationToken: cancellationToken));
    }

    private void Host_MouseUpVE(
        object? sender,
        Forms.MouseEventArgs e)
    {
        uint actionButton =
            GetMouseButtonVE(
                e.Button);

        if (actionButton == 0)
        {
            return;
        }

        uint buttons;
        VEControlActionMotion action;
        VEControlPosition? lastPosition;

        lock (_stateGateVE)
        {
            _buttonsVE &=
                ~actionButton;

            buttons =
                _buttonsVE;

            lastPosition =
                _lastPositionVE;

            action =
                buttons == 0
                    ? VEControlActionMotion.Up
                    : VEControlActionMotion.ButtonRelease;
        }

        if (!_controlVE.IsReadyVE)
        {
            return;
        }

        if (!TryMapPositionVE(
                e.X,
                e.Y,
                out VEControlPosition position,
                clampToViewport: true))
        {
            if (lastPosition is not VEControlPosition fallback)
            {
                return;
            }

            position =
                fallback;
        }

        lock (_stateGateVE)
        {
            _lastPositionVE =
                position;
        }

        EnqueueVE(
            cancellationToken =>
                _mouseVE.PointerAsync(
                    action,
                    position.X,
                    position.Y,
                    position.ScreenWidth,
                    position.ScreenHeight,
                    actionButton,
                    buttons,
                    cancellationToken));
    }

    private void Host_MouseWheelVE(
        object? sender,
        Forms.MouseEventArgs e)
    {
        if (!_controlVE.IsReadyVE ||
            !TryMapPositionVE(
                e.X,
                e.Y,
                out VEControlPosition position))
        {
            return;
        }

        float vertical =
            Math.Clamp(
                e.Delta /
                (float)Forms.SystemInformation.MouseWheelScrollDelta,
                -16f,
                16f);

        uint buttons;

        lock (_stateGateVE)
        {
            buttons =
                _buttonsVE;

            _lastPositionVE =
                position;
        }

        EnqueueVE(
            cancellationToken =>
                _mouseVE.ScrollAsync(
                    position.X,
                    position.Y,
                    position.ScreenWidth,
                    position.ScreenHeight,
                    horizontal: 0,
                    vertical: vertical,
                    buttons: buttons,
                    cancellationToken: cancellationToken));
    }

    private void Host_KeyDownVE(
        object? sender,
        Forms.KeyEventArgs e)
    {
        if (e.Handled ||
            !_controlVE.IsReadyVE)
        {
            return;
        }

        Forms.Keys key =
            e.KeyCode;

        if (!VEControlKeycode.TryMapVE(
                key,
                out uint keycode))
        {
            return;
        }

        bool firstDown;

        lock (_stateGateVE)
        {
            firstDown =
                _heldKeysVE.Add(
                    key);
        }

        if (!firstDown)
        {
            return;
        }

        uint metaState =
            VEControlKeycode.GetMetaStateVE(
                e.KeyData);

        EnqueueVE(
            cancellationToken =>
                _keyboardVE.KeyAsync(
                    VEControlActionKey.Down,
                    keycode,
                    repeat: 0,
                    metaState: metaState,
                    cancellationToken: cancellationToken));

        e.Handled =
            true;
    }

    private void Host_KeyUpVE(
        object? sender,
        Forms.KeyEventArgs e)
    {
        if (e.Handled ||
            !_controlVE.IsReadyVE)
        {
            return;
        }

        Forms.Keys key =
            e.KeyCode;

        if (!VEControlKeycode.TryMapVE(
                key,
                out uint keycode))
        {
            return;
        }

        bool wasHeld;

        lock (_stateGateVE)
        {
            wasHeld =
                _heldKeysVE.Remove(
                    key);
        }

        if (!wasHeld)
        {
            return;
        }

        uint metaState =
            VEControlKeycode.GetMetaStateVE(
                e.KeyData);

        EnqueueVE(
            cancellationToken =>
                _keyboardVE.KeyAsync(
                    VEControlActionKey.Up,
                    keycode,
                    repeat: 0,
                    metaState: metaState,
                    cancellationToken: cancellationToken));

        e.Handled =
            true;
    }

    private void Host_InputFocusLostVE(
        object? sender,
        EventArgs e)
    {
        if (!_controlVE.IsReadyVE)
        {
            lock (_stateGateVE)
            {
                _heldKeysVE.Clear();
                _buttonsVE = 0;
            }

            return;
        }

        Forms.Keys[] held;
        uint buttons;
        VEControlPosition? position;

        lock (_stateGateVE)
        {
            held =
                _heldKeysVE.ToArray();

            _heldKeysVE.Clear();

            buttons =
                _buttonsVE;

            _buttonsVE =
                0;

            position =
                _lastPositionVE;
        }

        foreach (Forms.Keys key in held)
        {
            if (!VEControlKeycode.TryMapVE(
                    key,
                    out uint keycode))
            {
                continue;
            }

            EnqueueVE(
                cancellationToken =>
                    _keyboardVE.KeyAsync(
                        VEControlActionKey.Up,
                        keycode,
                        repeat: 0,
                        metaState: 0,
                        cancellationToken: cancellationToken));
        }

        if (buttons != 0 &&
            position is VEControlPosition last)
        {
            EnqueueVE(
                cancellationToken =>
                    _mouseVE.PointerAsync(
                        VEControlActionMotion.Up,
                        last.X,
                        last.Y,
                        last.ScreenWidth,
                        last.ScreenHeight,
                        actionButton: 0,
                        buttons: 0,
                        cancellationToken: cancellationToken));
        }
    }

    private bool TryMapPositionVE(
        int clientX,
        int clientY,
        out VEControlPosition position,
        bool clampToViewport = false,
        bool activateVerticalEdges = false)
    {
        position =
            default;

        VERendererHost? host =
            _hostVE;

        if (host is null)
        {
            return false;
        }

        VERendererStatus status =
            _rendererVE.StatusVE;

        int frameWidth =
            status.TextureWidth;

        int frameHeight =
            status.TextureHeight;

        if (frameWidth <= 0 ||
            frameHeight <= 0 ||
            frameWidth > ushort.MaxValue ||
            frameHeight > ushort.MaxValue)
        {
            return false;
        }

        int outputWidth =
            host.ClientWidthVE;

        int outputHeight =
            host.ClientHeightVE;

        int rotation =
            VERendererRotation.NormalizeVE(
                status.RotationDegrees);

        VERendererRect viewport =
            VERendererViewport.CalculateVE(
                frameWidth,
                frameHeight,
                outputWidth,
                outputHeight,
                rotation);

        if (viewport.Width <= 0 ||
            viewport.Height <= 0)
        {
            return false;
        }

        float viewportRight =
            viewport.X + viewport.Width - 1;

        float viewportBottom =
            viewport.Y + viewport.Height - 1;

        bool insideViewport =
            clientX >= viewport.X &&
            clientX <= viewportRight &&
            clientY >= viewport.Y &&
            clientY <= viewportBottom;

        bool insideVerticalEdgeActivation =
            activateVerticalEdges &&
            clientX >= viewport.X &&
            clientX <= viewportRight &&
            clientY >= viewport.Y - VerticalEdgeActivationPixelsVE &&
            clientY <= viewportBottom + VerticalEdgeActivationPixelsVE;

        if (!insideViewport &&
            !insideVerticalEdgeActivation &&
            !clampToViewport)
        {
            return false;
        }

        float mappedClientX =
            Math.Clamp(
                clientX,
                viewport.X,
                viewportRight);

        float mappedClientY =
            Math.Clamp(
                clientY,
                viewport.Y,
                viewportBottom);

        int rotatedWidth =
            VERendererRotation.SwapsDimensionsVE(
                rotation)
                ? frameHeight
                : frameWidth;

        int rotatedHeight =
            VERendererRotation.SwapsDimensionsVE(
                rotation)
                ? frameWidth
                : frameHeight;

        double normalizedX =
            Math.Clamp(
                (mappedClientX - viewport.X) /
                viewport.Width,
                0d,
                1d);

        double normalizedY =
            Math.Clamp(
                (mappedClientY - viewport.Y) /
                viewport.Height,
                0d,
                1d);

        int rotatedX =
            Math.Clamp(
                (int)Math.Round(
                    normalizedX *
                    Math.Max(
                        0,
                        rotatedWidth - 1),
                    MidpointRounding.AwayFromZero),
                0,
                Math.Max(
                    0,
                    rotatedWidth - 1));

        int rotatedY =
            Math.Clamp(
                (int)Math.Round(
                    normalizedY *
                    Math.Max(
                        0,
                        rotatedHeight - 1),
                    MidpointRounding.AwayFromZero),
                0,
                Math.Max(
                    0,
                    rotatedHeight - 1));

        (int x, int y) =
            rotation switch
            {
                90 =>
                    (
                        rotatedY,
                        frameHeight - 1 - rotatedX),

                180 =>
                    (
                        frameWidth - 1 - rotatedX,
                        frameHeight - 1 - rotatedY),

                270 =>
                    (
                        frameWidth - 1 - rotatedY,
                        rotatedX),

                _ =>
                    (
                        rotatedX,
                        rotatedY)
            };

        x =
            Math.Clamp(
                x,
                0,
                frameWidth - 1);

        y =
            Math.Clamp(
                y,
                0,
                frameHeight - 1);

        position =
            new VEControlPosition(
                x,
                y,
                checked((ushort)frameWidth),
                checked((ushort)frameHeight));

        return true;
    }

    private void EnqueueVE(
        Func<CancellationToken, Task> action)
    {
        if (_disposedVE ||
            _disposeCtsVE.IsCancellationRequested)
        {
            return;
        }

        lock (_queueGateVE)
        {
            _queueTailVE =
                _queueTailVE
                    .ContinueWith(
                        async _ =>
                        {
                            if (_disposeCtsVE.IsCancellationRequested)
                            {
                                return;
                            }

                            try
                            {
                                await action(
                                        _disposeCtsVE.Token)
                                    .ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                                when (_disposeCtsVE.IsCancellationRequested)
                            {
                            }
                            catch (Exception ex)
                            {
                                InputErrorVE?.Invoke(
                                    this,
                                    ex.Message);
                            }
                        },
                        CancellationToken.None,
                        TaskContinuationOptions.None,
                        TaskScheduler.Default)
                    .Unwrap();
        }
    }

    private static uint GetMouseButtonVE(
        Forms.MouseButtons button)
        => button switch
        {
            Forms.MouseButtons.Left => 1u,
            Forms.MouseButtons.Right => 2u,
            Forms.MouseButtons.Middle => 4u,
            Forms.MouseButtons.XButton1 => 8u,
            Forms.MouseButtons.XButton2 => 16u,
            _ => 0u
        };

    private void ThrowIfDisposedVE()
        => ObjectDisposedException.ThrowIf(
            _disposedVE,
            this);

    public void Dispose()
    {
        if (_disposedVE)
        {
            return;
        }

        DetachVE();
        _disposeCtsVE.Cancel();
        _disposeCtsVE.Dispose();
        _disposedVE = true;
    }
}

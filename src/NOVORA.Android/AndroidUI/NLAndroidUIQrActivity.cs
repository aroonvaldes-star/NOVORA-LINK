using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using Android.Graphics;
using NOVORA.Control;
using ZXing;
using ZXing.Common;
using Camera = Android.Hardware.Camera;

namespace NOVORA.AndroidUI;

// Legacy preview keeps this first scanner independent of Google Play Services.
// It is scoped to this screen and never records or saves camera frames.
#pragma warning disable CS0618
[Activity(Name = "com.novora.appcontrol.QrActivity", Label = "Escanear NOVORA",
    Exported = false, Theme = "@android:style/Theme.Material.NoActionBar")]
public sealed class NLAndroidUIQrActivity : Activity, ISurfaceHolderCallback, Camera.IPreviewCallback
{
    private const int CameraPermissionRequest = 215;
    private SurfaceView _preview = null!;
    private TextView _message = null!;
    private Camera? _camera;
    private bool _started, _surfaceReady, _finished, _invalidReported;
    private int _decoding, _generation, _width, _height;
    private readonly BarcodeReaderGeneric _reader = new()
    {
        AutoRotate = true,
        Options = new DecodingOptions { TryHarder = true, PossibleFormats = [BarcodeFormat.QR_CODE] }
    };

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.AddFlags(WindowManagerFlags.Secure);
        NLAndroidUIPalette palette = NLAndroidUITheme.Current(this);
        var body = new LinearLayout(this) { Orientation = Orientation.Vertical };
        body.SetBackgroundColor(Color.ParseColor(palette.Background));
        _message = new TextView(this)
        {
            Text = "Apunta al QR de enlace LAN que muestra HOME de NOVORA PC.", TextSize = 17
        };
        _message.SetTextColor(Color.ParseColor(palette.Text));
        int padding = NLAndroidUIVisual.Dp(this, palette.PagePadding);
        _message.SetPadding(padding, padding, padding, padding);
        body.AddView(_message);
        _preview = new SurfaceView(this);
        _preview.Holder!.AddCallback(this);
        body.AddView(_preview, new LinearLayout.LayoutParams(-1, 0, 1));
        var close = new Button(this) { Text = "Cancelar" };
        NLAndroidUIVisual.Button(close);
        close.Click += (_, _) => Finish();
        body.AddView(close, new LinearLayout.LayoutParams(-1, -2));
        SetContentView(body);
        if (CheckSelfPermission(Android.Manifest.Permission.Camera) != Permission.Granted)
            RequestPermissions([Android.Manifest.Permission.Camera], CameraPermissionRequest);
    }

    protected override void OnStart()
    {
        base.OnStart();
        _started = true;
        StartCamera();
    }
    protected override void OnStop()
    {
        _started = false;
        CloseCamera();
        base.OnStop();
    }
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode != CameraPermissionRequest) return;
        if (grantResults.Length > 0 && grantResults[0] == Permission.Granted) StartCamera();
        else _message.Text = "Sin permiso de cámara. Regresa y usa Pegar contenido del QR, o permite la cámara en Ajustes de Android.";
    }
    public void SurfaceCreated(ISurfaceHolder holder) { _surfaceReady = true; StartCamera(); }
    public void SurfaceChanged(ISurfaceHolder holder, Format format, int width, int height) { }
    public void SurfaceDestroyed(ISurfaceHolder holder) { _surfaceReady = false; CloseCamera(); }

    private void StartCamera()
    {
        if (!_started || !_surfaceReady || _finished || _camera is not null ||
            CheckSelfPermission(Android.Manifest.Permission.Camera) != Permission.Granted) return;
        try
        {
            int cameraId = -1;
            var info = new Camera.CameraInfo();
            for (int index = 0; index < Camera.NumberOfCameras; index++)
            {
                Camera.GetCameraInfo(index, info);
                if (info.Facing == Android.Hardware.CameraFacing.Back) { cameraId = index; break; }
            }
            if (cameraId < 0) throw new InvalidOperationException("No rear camera");
            Camera.GetCameraInfo(cameraId, info);
            var camera = Camera.Open(cameraId) ?? throw new InvalidOperationException("Camera unavailable");
            _camera = camera;
            using var parameters = camera.GetParameters()!;
            var sizes = parameters.SupportedPreviewSizes!;
            var size = sizes.Where(s => s.Width <= 1280 && s.Height <= 1280)
                .OrderByDescending(s => s.Width * s.Height).FirstOrDefault() ?? sizes.OrderBy(s => s.Width * s.Height).First();
            _width = size.Width;
            _height = size.Height;
            parameters.SetPreviewSize(_width, _height);
            parameters.PreviewFormat = ImageFormatType.Nv21;
            if (parameters.SupportedFocusModes?.Contains(Camera.Parameters.FocusModeContinuousPicture) == true)
                parameters.FocusMode = Camera.Parameters.FocusModeContinuousPicture;
            camera.SetParameters(parameters);
            int degrees = WindowManager!.DefaultDisplay!.Rotation switch
            {
                SurfaceOrientation.Rotation90 => 90,
                SurfaceOrientation.Rotation180 => 180,
                SurfaceOrientation.Rotation270 => 270,
                _ => 0
            };
            camera.SetDisplayOrientation((info.Orientation - degrees + 360) % 360);
            camera.SetPreviewDisplay(_preview.Holder);
            camera.SetPreviewCallback(this);
            camera.StartPreview();
        }
        catch (Exception)
        {
            CloseCamera();
            _message.Text = "No se pudo abrir la cámara. Regresa y usa Pegar contenido del QR.";
        }
    }

    public void OnPreviewFrame(byte[]? data, Camera? camera)
    {
        if (!_started || _finished || !ReferenceEquals(camera, _camera) || data is null ||
            Interlocked.CompareExchange(ref _decoding, 1, 0) != 0) return;
        int generation = _generation, width = _width, height = _height;
        // Android owns the callback buffer. Retain only a private copy for this one decode.
        byte[] frame = (byte[])data.Clone();
        _ = Task.Run(() =>
        {
            try
            {
                if (frame.Length < width * height) return;
                var source = new PlanarYUVLuminanceSource(frame, width, height, 0, 0, width, height, false);
                string? text = _reader.Decode(source)?.Text;
                if (string.IsNullOrWhiteSpace(text)) return;
                bool valid;
                try { valid = text.Length <= 4096 && NLControlLanInvitation.Parse(text) is not null; }
                catch (Exception) { valid = false; }
                RunOnUiThread(() =>
                {
                    if (!_started || _finished || generation != _generation) return;
                    if (!valid)
                    {
                        if (!_invalidReported) _message.Text = "Este QR no es una invitación NOVORA válida y vigente. Prepara uno nuevo en PC.";
                        _invalidReported = true;
                        return;
                    }
                    _finished = true;
                    CloseCamera();
                    SetResult(Android.App.Result.Ok, new Intent().PutExtra("invitation", text));
                    Finish();
                });
            }
            catch (Exception) { /* A malformed or blurred camera frame is simply discarded. */ }
            finally
            {
                Array.Clear(frame);
                Interlocked.Exchange(ref _decoding, 0);
            }
        });
    }

    private void CloseCamera()
    {
        _generation++;
        var camera = _camera;
        _camera = null;
        if (camera is null) return;
        try { camera.SetPreviewCallback(null); camera.StopPreview(); }
        catch (Exception) { }
        finally
        {
            try { camera.Release(); } catch (Exception) { }
            camera.Dispose();
        }
    }
}
#pragma warning restore CS0618

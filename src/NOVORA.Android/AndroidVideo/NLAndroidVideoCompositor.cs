using Android.Graphics;
using Android.Opengl;
using Android.OS;
using Android.Views;
using Java.Nio;

namespace NOVORA.AndroidVideo;

internal sealed class NLAndroidVideoCompositor : Java.Lang.Object,
    SurfaceTexture.IOnFrameAvailableListener, IAsyncDisposable
{
    private const int TextureExternalOes = 0x8D65;
    private readonly HandlerThread _thread = new("NOVORA-VE-Compositor");
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Surface _encoderSurface;
    private readonly int _outputWidth;
    private readonly int _outputHeight;
    private Handler? _handler;
    private EGLDisplay? _display;
    private EGLContext? _context;
    private EGLSurface? _eglSurface;
    private SurfaceTexture? _surfaceTexture;
    private Surface? _captureSurface;
    private FloatBuffer? _vertices;
    private FloatBuffer? _textureCoordinates;
    private int _textureId;
    private int _program;
    private int _positionLocation;
    private int _textureLocation;
    private int _matrixLocation;
    private int _rotationLocation;
    private int _rotationQuarterTurns;
    private bool _disposed;

    public NLAndroidVideoCompositor(Surface encoderSurface, int outputWidth, int outputHeight)
    {
        _encoderSurface = encoderSurface ?? throw new ArgumentNullException(nameof(encoderSurface));
        _outputWidth = outputWidth;
        _outputHeight = outputHeight;
        _thread.Start();
        _handler = new Handler(_thread.Looper!);
        _handler.Post(Initialize);
    }

    public async Task<Surface> GetCaptureSurfaceAsync(CancellationToken cancellationToken)
    {
        await _ready.Task.WaitAsync(cancellationToken);
        return _captureSurface
            ?? throw new InvalidOperationException("El compositor no creó la superficie de captura.");
    }

    public Task UpdateSourceAsync(int width, int height, int rotationDegrees,
        CancellationToken cancellationToken)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(NLAndroidVideoCompositor));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        _handler!.Post(() =>
        {
            try
            {
                _rotationQuarterTurns = ((rotationDegrees / 90) % 4 + 4) % 4;
                _surfaceTexture!.SetDefaultBufferSize(width, height);
                completion.TrySetResult();
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        });
        return completion.Task;
    }

    public void OnFrameAvailable(SurfaceTexture? surfaceTexture)
    {
        if (_disposed || surfaceTexture is null)
            return;
        try
        {
            surfaceTexture.UpdateTexImage();
            float[] transform = new float[16];
            surfaceTexture.GetTransformMatrix(transform);

            GLES20.GlViewport(0, 0, _outputWidth, _outputHeight);
            GLES20.GlClearColor(0f, 0f, 0f, 1f);
            GLES20.GlClear(GLES20.GlColorBufferBit);
            GLES20.GlUseProgram(_program);
            GLES20.GlActiveTexture(GLES20.GlTexture0);
            GLES20.GlBindTexture(TextureExternalOes, _textureId);
            GLES20.GlUniformMatrix4fv(_matrixLocation, 1, false, transform, 0);
            GLES20.GlUniform1i(_rotationLocation, _rotationQuarterTurns);

            _vertices!.Position(0);
            GLES20.GlVertexAttribPointer(_positionLocation, 2, GLES20.GlFloat, false, 0, _vertices);
            GLES20.GlEnableVertexAttribArray(_positionLocation);
            _textureCoordinates!.Position(0);
            GLES20.GlVertexAttribPointer(_textureLocation, 2, GLES20.GlFloat, false, 0, _textureCoordinates);
            GLES20.GlEnableVertexAttribArray(_textureLocation);
            GLES20.GlDrawArrays(GLES20.GlTriangleStrip, 0, 4);
            EGLExt.EglPresentationTimeANDROID(_display!, _eglSurface!, surfaceTexture.Timestamp);
            if (!EGL14.EglSwapBuffers(_display!, _eglSurface!))
                throw new InvalidOperationException($"EGL swap falló: 0x{EGL14.EglGetError():x}.");
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("NOVORA-VE", $"Compositor persistente: {ex}");
        }
    }

    private void Initialize()
    {
        try
        {
            _display = EGL14.EglGetDisplay(EGL14.EglDefaultDisplay);
            if (_display == EGL14.EglNoDisplay)
                throw new InvalidOperationException("EGL no entregó display.");
            int[] versions = new int[2];
            if (!EGL14.EglInitialize(_display, versions, 0, versions, 1))
                throw new InvalidOperationException("EGL no inicializó.");

            int[] attributes =
            [
                EGL14.EglRedSize, 8,
                EGL14.EglGreenSize, 8,
                EGL14.EglBlueSize, 8,
                EGL14.EglAlphaSize, 8,
                EGL14.EglRenderableType, EGL14.EglOpenglEs2Bit,
                EGL14.EglSurfaceType, EGL14.EglWindowBit,
                EGL14.EglNone
            ];
            EGLConfig[] configs = new EGLConfig[1];
            int[] count = new int[1];
            if (!EGL14.EglChooseConfig(_display, attributes, 0, configs, 0, 1, count, 0) || count[0] == 0)
                throw new InvalidOperationException("EGL no encontró una configuración compatible.");
            int[] contextAttributes = [EGL14.EglContextClientVersion, 2, EGL14.EglNone];
            _context = EGL14.EglCreateContext(
                _display, configs[0], EGL14.EglNoContext, contextAttributes, 0);
            int[] surfaceAttributes = [EGL14.EglNone];
            _eglSurface = EGL14.EglCreateWindowSurface(
                _display, configs[0], _encoderSurface, surfaceAttributes, 0);
            if (!EGL14.EglMakeCurrent(_display, _eglSurface, _eglSurface, _context))
                throw new InvalidOperationException("EGL no enlazó la superficie de MediaCodec.");

            int[] textures = new int[1];
            GLES20.GlGenTextures(1, textures, 0);
            _textureId = textures[0];
            GLES20.GlBindTexture(TextureExternalOes, _textureId);
            GLES20.GlTexParameteri(TextureExternalOes, GLES20.GlTextureMinFilter, GLES20.GlLinear);
            GLES20.GlTexParameteri(TextureExternalOes, GLES20.GlTextureMagFilter, GLES20.GlLinear);
            GLES20.GlTexParameteri(TextureExternalOes, GLES20.GlTextureWrapS, GLES20.GlClampToEdge);
            GLES20.GlTexParameteri(TextureExternalOes, GLES20.GlTextureWrapT, GLES20.GlClampToEdge);

            _surfaceTexture = new SurfaceTexture(_textureId);
            _surfaceTexture.SetOnFrameAvailableListener(this, _handler);
            _captureSurface = new Surface(_surfaceTexture);
            _program = CreateProgram();
            _positionLocation = GLES20.GlGetAttribLocation(_program, "aPosition");
            _textureLocation = GLES20.GlGetAttribLocation(_program, "aTexCoord");
            _matrixLocation = GLES20.GlGetUniformLocation(_program, "uTexMatrix");
            _rotationLocation = GLES20.GlGetUniformLocation(_program, "uRotation");
            _vertices = CreateBuffer([-1f, -1f, 1f, -1f, -1f, 1f, 1f, 1f]);
            _textureCoordinates = CreateBuffer([0f, 0f, 1f, 0f, 0f, 1f, 1f, 1f]);
            _ready.TrySetResult();
        }
        catch (Exception ex) { _ready.TrySetException(ex); }
    }

    private static int CreateProgram()
    {
        const string vertex = "attribute vec4 aPosition; attribute vec2 aTexCoord; " +
            "uniform mat4 uTexMatrix; uniform int uRotation; varying vec2 vTexCoord; " +
            "void main(){ gl_Position=aPosition; vec2 p=aTexCoord; " +
            "if(uRotation==1) p=vec2(aTexCoord.y,1.0-aTexCoord.x); " +
            "else if(uRotation==2) p=vec2(1.0-aTexCoord.x,1.0-aTexCoord.y); " +
            "else if(uRotation==3) p=vec2(1.0-aTexCoord.y,aTexCoord.x); " +
            "vTexCoord=(uTexMatrix*vec4(p,0.0,1.0)).xy; }";
        const string fragment = "#extension GL_OES_EGL_image_external : require\n" +
            "precision mediump float; varying vec2 vTexCoord; " +
            "uniform samplerExternalOES sTexture; " +
            "void main(){ gl_FragColor=texture2D(sTexture,vTexCoord); }";
        int vertexShader = CompileShader(GLES20.GlVertexShader, vertex);
        int fragmentShader = CompileShader(GLES20.GlFragmentShader, fragment);
        int program = GLES20.GlCreateProgram();
        GLES20.GlAttachShader(program, vertexShader);
        GLES20.GlAttachShader(program, fragmentShader);
        GLES20.GlLinkProgram(program);
        int[] linked = new int[1];
        GLES20.GlGetProgramiv(program, GLES20.GlLinkStatus, linked, 0);
        GLES20.GlDeleteShader(vertexShader);
        GLES20.GlDeleteShader(fragmentShader);
        if (linked[0] == 0)
            throw new InvalidOperationException($"OpenGL no enlazó el compositor: {GLES20.GlGetProgramInfoLog(program)}");
        return program;
    }

    private static int CompileShader(int type, string source)
    {
        int shader = GLES20.GlCreateShader(type);
        GLES20.GlShaderSource(shader, source);
        GLES20.GlCompileShader(shader);
        int[] compiled = new int[1];
        GLES20.GlGetShaderiv(shader, GLES20.GlCompileStatus, compiled, 0);
        if (compiled[0] == 0)
            throw new InvalidOperationException($"OpenGL no compiló el compositor: {GLES20.GlGetShaderInfoLog(shader)}");
        return shader;
    }

    private static FloatBuffer CreateBuffer(float[] values)
    {
        ByteOrder order = ByteOrder.NativeOrder()
            ?? throw new InvalidOperationException("Android no entregó el orden nativo de bytes.");
        FloatBuffer buffer = ByteBuffer.AllocateDirect(values.Length * sizeof(float))!
            .Order(order)!.AsFloatBuffer()!;
        buffer.Put(values);
        buffer.Position(0);
        return buffer;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler?.Post(() =>
        {
            try
            {
                _surfaceTexture?.SetOnFrameAvailableListener(null);
                _captureSurface?.Release();
                _captureSurface?.Dispose();
                _surfaceTexture?.Release();
                _surfaceTexture?.Dispose();
                if (_program != 0) GLES20.GlDeleteProgram(_program);
                if (_textureId != 0) GLES20.GlDeleteTextures(1, [_textureId], 0);
                if (_display is not null && _display != EGL14.EglNoDisplay)
                {
                    EGL14.EglMakeCurrent(_display, EGL14.EglNoSurface, EGL14.EglNoSurface, EGL14.EglNoContext);
                    if (_eglSurface is not null) EGL14.EglDestroySurface(_display, _eglSurface);
                    if (_context is not null) EGL14.EglDestroyContext(_display, _context);
                    EGL14.EglTerminate(_display);
                }
                completion.TrySetResult();
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        });
        await completion.Task;
        _thread.QuitSafely();
        _thread.Join();
        _handler?.Dispose();
        _thread.Dispose();
    }
}

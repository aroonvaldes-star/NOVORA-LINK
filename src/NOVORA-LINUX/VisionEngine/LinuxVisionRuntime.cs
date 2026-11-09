using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using NOVORA.Linux.Control;
using NOVORA.Linux.Files;

namespace NOVORA.Linux.VisionEngine;

/// <summary>One native session implementation for both authenticated USB and LAN transports.</summary>
internal sealed class LinuxVisionRuntime : IAsyncDisposable
{
    private readonly LinuxVisionSettings _settings;
    private readonly Channel<byte[]> _input = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(256) { SingleReader=true, FullMode=BoundedChannelFullMode.Wait });
    private CancellationTokenSource? _lifetime;
    private LinuxSdlVideoRenderer? _renderer;
    private long _packets;
    private long _videoBytes;
    private long _clipboardSequence;
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource> _clipboardAcks = new();
    private volatile bool _streaming;
    private int _disposed;
    public LinuxVisionFiles Files { get; }
    public LinuxVisionRecorder Recorder { get; }
    public long Generation { get; }
    public string DecoderBackend { get; private set; } = "Unavailable";
    public string InputStatus { get; private set; } = "Control remoto: requiere Accesibilidad de LINK Control en Android.";
    public bool AudioPlaying { get; private set; }
    public event Action? Changed;
    /// <summary>Android answered a clipboard read (Type 1). Never logged or persisted.</summary>
    public event Action<string>? ClipboardReceived;
    /// <summary>Ctrl+C (true) / Ctrl+V (false) pressed in the video window.</summary>
    public event Action<bool>? ClipboardShortcut;
    /// <summary>Paths dropped on the video window.</summary>
    public event Action<string[]>? FilesDropped;
    /// <summary>Files sent and received during this VE session (Windows Integración summary).</summary>
    public LinuxVisionTransferSummary Transfers { get; } = new();
    public bool IsStreaming => _streaming;
    public LinuxVisionRuntime(LinuxVisionSettings settings, long generation)
    {
        _settings=settings; Generation=generation;
        Files=new(settings.ReceiveDirectory);
        Recorder=new(settings.RecordingDirectory, settings.Fps);
        Files.Changed += Notify;
        Files.Sent += Transfers.RecordSent;
        Files.ReceivedFile += (destination,bytes)=>Transfers.RecordReceived(1,bytes,destination);
        Recorder.Changed += Notify;
    }
    private void Notify() => Changed?.Invoke();
    public LinuxVisionTelemetry Telemetry => new(_renderer?.Backend ?? "Unavailable",Interlocked.Read(ref _packets),_renderer?.FramesPresented ?? 0,
        Math.Max(0,Interlocked.Read(ref _packets)-(_renderer?.FramesPresented ?? 0)),_renderer?.OutputWidth ?? 0,_renderer?.OutputHeight ?? 0,
        DecoderBackend, AudioPlaying, InputStatus, Recorder.Status, Files.Status, Interlocked.Read(ref _videoBytes));

    /// <summary>Windows ExchangeVE Ctrl+C: Android copies the focused selection and returns its clipboard (Type 8 → Type 1).</summary>
    public void RequestClipboard()
    {
        if (!_streaming) throw new InvalidOperationException("ExchangeVE requiere VisionEngine activo.");
        QueueCommand(new { Type=8, CopyKey=1 });
    }

    /// <summary>Windows ExchangeVE Ctrl+V: sets Android's clipboard (Type 9) and optionally pastes into the focused field; waits for the ack.</summary>
    public async Task SetClipboardAsync(string text,bool paste,CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!_streaming) throw new InvalidOperationException("ExchangeVE requiere VisionEngine activo.");
        ulong sequence=(ulong)Interlocked.Increment(ref _clipboardSequence);
        byte[] payload=JsonSerializer.SerializeToUtf8Bytes(new { Type=9, Text=text, Sequence=sequence, Paste=paste });
        if (payload.Length>ControlProtocol.MaxFrame) throw new InvalidDataException("El texto del portapapeles es demasiado grande para enviarlo a Android.");
        var ack=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _clipboardAcks[sequence]=ack;
        try
        {
            byte[] frame=new byte[payload.Length+4]; BinaryPrimitives.WriteInt32BigEndian(frame,payload.Length); payload.CopyTo(frame,4);
            QueueInput(frame);
            await ack.Task.WaitAsync(TimeSpan.FromSeconds(3),token).ConfigureAwait(false);
        }
        catch (TimeoutException) { throw new TimeoutException("Android no confirmó el portapapeles. Revisa la Accesibilidad de LINK Control."); }
        finally { _clipboardAcks.TryRemove(sequence,out _); }
    }

    /// <summary>Windows VEMediaRecorder.CaptureAsync: PNG of the last presented frame in the recordings folder.</summary>
    public async Task<string> CaptureAsync(CancellationToken token=default)
    {
        if (!_streaming || _renderer is not { } renderer || !renderer.TryGetLastFrame(out byte[] rgba,out int width,out int height))
            throw new InvalidOperationException("Espera a que VE muestre imagen antes de capturar.");
        string directory=_settings.RecordingDirectory;
        string path=await Task.Run(()=>
        {
            Directory.CreateDirectory(directory);
            string stem="NOVORA-captura-"+DateTime.Now.ToString("yyyyMMdd-HHmmss",System.Globalization.CultureInfo.InvariantCulture);
            for (int i=0;i<1000;i++)
            {
                string candidate=Path.Combine(directory,i==0 ? stem+".png" : $"{stem} ({i}).png");
                try { LinuxPngWriter.Write(candidate,rgba,width,height); return candidate; }
                catch (IOException) when (File.Exists(candidate) && i<999) { }
            }
            throw new IOException("No se pudo reservar un nombre para la captura.");
        },token).ConfigureAwait(false);
        Notify();
        return path;
    }

    /// <summary>Applies "Presentación dinámica" to the open window without restarting VE.</summary>
    public void SetVSync(bool enabled) => _renderer?.SetVSync(enabled);

    public void StartRecording()
    {
        if (!_streaming) throw new InvalidOperationException("Espera a que VE esté transmitiendo antes de grabar.");
        Recorder.Start();
        QueueCommand(new { Type=17 }); // Ask Android for a fresh keyframe, through the single writer.
        Notify();
    }
    public Task StopRecordingAsync() => Recorder.StopAsync();
    private void QueueCommand(object command)
    {
        byte[] payload=JsonSerializer.SerializeToUtf8Bytes(command);
        byte[] frame=new byte[payload.Length+4]; BinaryPrimitives.WriteInt32BigEndian(frame,payload.Length); payload.CopyTo(frame,4);
        QueueInput(frame);
    }
    private void QueueInput(byte[] frame)
    {
        if (_input.Writer.TryWrite(frame)) return;
        InputStatus="Control remoto saturado; se detuvo la sesión para no perder eventos de liberación.";
        Notify(); _lifetime?.Cancel();
    }
    public async Task RunAsync(Stream video,Stream control,Stream? audio,Stream? files,CancellationToken token)
    {
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(token); _lifetime=lifetime;
        LinuxSdlVideoRenderer? renderer=null;
        LinuxFfmpegVideoDecoder? decoder=null;
        Task write=Task.CompletedTask, responses=Task.CompletedTask, audioTask=Task.CompletedTask;
        bool windowClosed=false;
        try
        {
            renderer=new LinuxSdlVideoRenderer(_settings); _renderer=renderer;
            renderer.Input += QueueInput;
            renderer.ClipboardShortcut += copy => ClipboardShortcut?.Invoke(copy);
            renderer.FilesDropped += paths => FilesDropped?.Invoke(paths);
            renderer.Closed += () => { windowClosed=true; lifetime.Cancel(); };
            if (files is not null) await Files.AttachAsync(files,Generation,lifetime.Token).ConfigureAwait(false);
            write=WriteInputAsync(control,lifetime.Token);
            responses=ReadResponsesAsync(control,lifetime.Token);
            audioTask=audio is null ? Task.CompletedTask : PlayAudioAsync(audio,lifetime.Token);
            _ = audioTask.ContinueWith(_=>lifetime.Cancel(), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            var reader=new LinuxVisionProtocolReader(video);
            (VideoCodec codec,VideoSession session)=await reader.OpenAsync(lifetime.Token).ConfigureAwait(false);
            renderer.UpdateSession(session);
            byte[]? configuration=null;
            _streaming=true; Notify();
            while (!lifetime.IsCancellationRequested)
            {
                if (write.IsFaulted) await write;
                if (responses.IsCompleted) { await responses; throw new EndOfStreamException("El canal de control de VE terminó."); }
                if (audioTask.IsFaulted) await audioTask;
                Task<VideoPacket?> read=reader.ReadPacketAsync(session,changed=>session=changed,lifetime.Token);
                VideoPacket? next=decoder is null ? await read.ConfigureAwait(false) : await LinuxVisionDecoderMonitor.AwaitAsync(read,decoder.FrameTask,lifetime).ConfigureAwait(false);
                if (next is not { } packet) throw new EndOfStreamException("El flujo de video terminó.");
                session=packet.Session;
                Interlocked.Add(ref _videoBytes,packet.Data.Length);
                if (packet.IsConfiguration) configuration=packet.Data; else Interlocked.Increment(ref _packets);
                bool recreate=decoder is null || decoder.SessionGeometryChanged(session);
                if (recreate)
                {
                    if (decoder is not null) await decoder.DisposeAsync().ConfigureAwait(false);
                    renderer.UpdateSession(session);
                    decoder=new LinuxFfmpegVideoDecoder(codec,session,renderer,_settings);
                    DecoderBackend=decoder.ActiveBackend; Notify();
                }
                Recorder.WriteVideo(codec,packet,configuration);
                foreach (var input in LinuxVisionDecoderInput.ForPacket(packet,configuration,recreate))
                    await LinuxVisionDecoderMonitor.AwaitAsync(decoder!.WritePacketAsync(input.Data,input.PresentationTimeUs,lifetime.Token),decoder.FrameTask,lifetime).ConfigureAwait(false);
            }
        }
        catch(OperationCanceledException) when(windowClosed && !token.IsCancellationRequested)
        {
            if(renderer?.InputError is { } error) throw new IOException("Falló la ventana de VE: "+error);
            // Closing the native video window is an ordinary user stop, not a failed stream.
        }
        catch(OperationCanceledException) when(audioTask.IsFaulted || write.IsFaulted || responses.IsFaulted)
        {
            await (audioTask.IsFaulted ? audioTask : write.IsFaulted ? write : responses).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _streaming=false;
            lifetime.Cancel(); _input.Writer.TryComplete();
            // Close native channels before awaiting workers: cancellation must not depend on another video packet.
            try
            {
                await LinuxVisionCleanup.RunAsync(
                    ()=>{control.Dispose(); return Task.CompletedTask;}, ()=>{audio?.Dispose(); return Task.CompletedTask;},
                    ()=>IgnoreCancellationAsync(write), ()=>IgnoreCancellationAsync(responses), ()=>IgnoreCancellationAsync(audioTask),
                    ()=>decoder is null ? Task.CompletedTask : decoder.DisposeAsync().AsTask(),
                    ()=>Recorder.StopAsync(), ()=>Files.DisposeAsync().AsTask(),
                    ()=>{ renderer?.Dispose(); return Task.CompletedTask; }).ConfigureAwait(false);
            }
            finally { _renderer=null; _lifetime=null; Notify(); }

        }
    }
    private async Task WriteInputAsync(Stream stream,CancellationToken token)
    {
        try
        {
            await foreach (byte[] bytes in _input.Reader.ReadAllAsync(token))
            { await stream.WriteAsync(bytes,token).ConfigureAwait(false); await stream.FlushAsync(token).ConfigureAwait(false); }
        }
        catch { _lifetime?.Cancel(); throw; }
    }
    private async Task ReadResponsesAsync(Stream stream,CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                JsonElement response=await ControlProtocol.ReadAsync<JsonElement>(stream,token).ConfigureAwait(false);
                int kind=response.TryGetProperty("Type",out var type) && type.ValueKind==JsonValueKind.Number ? type.GetInt32() : 0;
                switch (kind)
                {
                    case 1: // NLControlInputResponse.ClipboardType
                        ClipboardReceived?.Invoke(response.TryGetProperty("Text",out var clip) && clip.ValueKind==JsonValueKind.String ? clip.GetString() ?? "" : "");
                        break;
                    case 2: // ClipboardAckType
                        if (response.TryGetProperty("Sequence",out var sequence) && sequence.TryGetUInt64(out ulong acked) && _clipboardAcks.TryRemove(acked,out var ack))
                            ack.TrySetResult();
                        break;
                    case 6:
                        InputStatus=response.TryGetProperty("Text",out var text) ? text.GetString() ?? "Error de entrada Android." : "Error de entrada Android."; Notify();
                        break;
                }
            }
        }
        finally
        {
            _lifetime?.Cancel();
            foreach (var pending in _clipboardAcks.Values) pending.TrySetException(new IOException("El canal de control de VE terminó."));
        }
    }
    private async Task PlayAudioAsync(Stream stream,CancellationToken token)
    {
        var reader=new LinuxAudioProtocolReader(stream);
        AudioCodec? codec=await reader.OpenAsync(token).ConfigureAwait(false);
        if (codec is null) return;
        using var decoder=new LinuxFfmpegAudioDecoder(codec.Value);
        using LinuxSdlAudioPlayer? player=_settings.AudioEnabled ? new LinuxSdlAudioPlayer(_settings.AudioDeviceId,_settings.AudioDeviceName) : null;
        AudioPlaying=player is not null; Notify();
        try
        {
            while (!token.IsCancellationRequested)
            {
                AudioPacket packet=await reader.ReadPacketAsync(token).ConfigureAwait(false);
                foreach (byte[] pcm in decoder.Decode(packet))
                { player?.Play(pcm); Recorder.WriteAudio(pcm,packet.PresentationTimeUs); }
            }
        }
        finally { AudioPlaying=false; Notify(); }
    }
    private static async Task IgnoreCancellationAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed,1)!=0) return;
        try { _lifetime?.Cancel(); } catch(ObjectDisposedException) { }
        await LinuxVisionCleanup.RunAsync(()=>Recorder.DisposeAsync().AsTask(),()=>Files.DisposeAsync().AsTask()).ConfigureAwait(false);
    }
}

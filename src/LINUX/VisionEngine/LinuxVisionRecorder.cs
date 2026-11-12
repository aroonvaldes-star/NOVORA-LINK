using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace NOVORA.Linux.VisionEngine;

/// <summary>Nonblocking encoded H.264/PCM recorder. The bounded worker owns the file and
/// emits Matroska (RFC 9559); no encoder, shell, or hidden software re-encode is involved.</summary>
internal sealed class LinuxVisionRecorder : IAsyncDisposable
{
    private const long MaxQueuedBytes = 32L * 1024 * 1024;
    private readonly string _directory;
    private readonly int _fps;
    private readonly object _gate = new();
    private Channel<Message>? _queue;
    private Task _worker = Task.CompletedTask;
    private long _queuedBytes;
    private bool _recording;
    private Exception? _failure;
    private string _status = "Grabación detenida.";
    public bool IsRecording { get { lock (_gate) return _recording; } }
    public string Status { get { lock (_gate) return _status; } }
    public event Action? Changed;
    private abstract record Message(long ArrivedAt, int Bytes);
    private sealed record VideoMessage(VideoCodec Codec, VideoPacket Packet, byte[]? Configuration, long Arrived)
        : Message(Arrived, Packet.Data.Length + (Configuration?.Length ?? 0));
    private sealed record AudioMessage(byte[] Pcm, long? Pts, long Arrived) : Message(Arrived, Pcm.Length);

    public LinuxVisionRecorder(string directory, int fps)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("La carpeta de grabación debe ser absoluta.", nameof(directory));
        if (fps is < 1 or > 240) throw new ArgumentOutOfRangeException(nameof(fps));
        _directory = directory; _fps = fps;
    }
    public void Start()
    {
        lock (_gate)
        {
            if (_recording || !_worker.IsCompleted) throw new InvalidOperationException("La grabación anterior aún no terminó.");
            _failure = null; _queuedBytes = 0;
            _queue = Channel.CreateBounded<Message>(new BoundedChannelOptions(128)
                { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
            _recording = true; _status = "Grabación: esperando el siguiente keyframe H.264.";
            _worker = Task.Run(() => RunAsync(_queue.Reader));
        }
        Changed?.Invoke();
    }
    public void WriteVideo(VideoCodec codec, VideoPacket packet, byte[]? configuration) =>
        Enqueue(new VideoMessage(codec, packet, configuration, Stopwatch.GetTimestamp()));
    public void WriteAudio(byte[] pcm) => WriteAudio(pcm, null);
    public void WriteAudio(byte[] pcm, long? presentationTimeUs)
    {
        if (pcm.Length == 0 || !IsRecording) return;
        // Audio decoder buffers may be reused independently of this background worker.
        Enqueue(new AudioMessage(pcm.ToArray(), presentationTimeUs, Stopwatch.GetTimestamp()));
    }
    private void Enqueue(Message message)
    {
        bool failed = false;
        lock (_gate)
        {
            if (!_recording) return;
            if (_queuedBytes + message.Bytes > MaxQueuedBytes || !_queue!.Writer.TryWrite(message))
            {
                _failure = new IOException("El disco no sostiene la grabación; se alcanzó el límite de cola (32 MiB/128 paquetes).");
                _recording = false; _status = "Error de grabación: " + _failure.Message;
                _queue!.Writer.TryComplete(_failure); failed = true;
            }
            else _queuedBytes += message.Bytes;
        }
        if (failed) Changed?.Invoke();
    }
    private void Report(string status) { lock (_gate) _status = status; Changed?.Invoke(); }
    private async Task RunAsync(ChannelReader<Message> reader)
    {
        var capture = new Capture(_directory, _fps, Report);
        try
        {
            await foreach (Message message in reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    switch (message)
                    {
                        case VideoMessage video: capture.Video(video.Codec, video.Packet, video.Configuration, video.ArrivedAt); break;
                        case AudioMessage audio: capture.Audio(audio.Pcm, audio.Pts, audio.ArrivedAt); break;
                    }
                }
                finally { lock (_gate) _queuedBytes -= message.Bytes; }
            }
            capture.Complete();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            capture.Abort();
            lock (_gate) { _failure ??= ex; _recording = false; }
            Report("Error de grabación: " + ex.Message + (capture.PartialPath is null ? "" : $" · parcial recuperable: {capture.PartialPath}"));
        }
        finally { lock (_gate) _recording = false; }
    }
    public async Task StopAsync()
    {
        Task worker;
        lock (_gate) { _recording = false; _queue?.Writer.TryComplete(); worker = _worker; }
        await worker.ConfigureAwait(false);
        Exception? failure; lock (_gate) failure = _failure;
        if (failure is not null) throw new IOException(Status, failure);
    }
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private sealed class Capture(string directory, int fps, Action<string> report)
    {
        private Matroska? _file;
        private VideoSession? _session;
        private byte[]? _configuration;
        private long? _videoBase;
        private long _videoArrival;
        private long _segmentBaseUs;
        private long? _audioBase;
        private long _audioArrival;
        private long _audioSamples;
        private long _frames;
        private int _segments;
        private string? _lastPath;
        private readonly string _prefix = "VisionEngine-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        public string? PartialPath => _file?.PartialPath;
        public void Video(VideoCodec codec, VideoPacket packet, byte[]? configuration, long arrival)
        {
            if (codec != VideoCodec.H264) throw new NotSupportedException("Grabación disponible para H.264, el formato actual de LINK Control.");
            if (configuration is not null) _configuration = configuration;
            if (packet.IsConfiguration) { _configuration = packet.Data; return; }
            bool changed = _session is not null && (_session.Width != packet.Session.Width || _session.Height != packet.Session.Height || _session.RotationDegrees != packet.Session.RotationDegrees);
            if (changed) FinishSegment();
            if (_file is null)
            {
                if (!packet.IsKeyFrame) return;
                byte[] avcc = H264.Configuration(packet.Data, _configuration);
                Directory.CreateDirectory(directory);
                string final = Path.Combine(directory, _prefix + $"-{++_segments:D3}.mkv");
                _file = new Matroska(final, packet.Session, fps, avcc);
                _session = packet.Session;
                if (_videoArrival == 0) { _videoArrival = arrival; _videoBase = packet.PresentationTimeUs; }
                _segmentBaseUs = packet.PresentationTimeUs is { } segmentPts && _videoBase is { } videoOrigin
                    ? Math.Max(0, segmentPts-videoOrigin) : _frames*1_000_000/fps;
                report("Grabando: " + final + (_segments > 1 ? " · nuevo segmento por geometría/rotación." : ""));
            }
            long us = packet.PresentationTimeUs is { } source && _videoBase is { } origin
                ? Math.Max(0, source-origin) : _frames*1_000_000/fps;
            _file.Video(H264.Packet(packet.Data), Math.Max(0, us-_segmentBaseUs), packet.IsKeyFrame);
            _frames++;
        }
        public void Audio(byte[] pcm, long? pts, long arrival)
        {
            if (pcm.Length % 4 != 0) throw new InvalidDataException("Audio de grabación debe ser PCM S16LE estéreo a 48 kHz.");
            if (_audioArrival == 0) { _audioArrival = arrival; _audioBase = pts; _audioSamples = 0; }
            if (_file is not null)
            {
                // Android audio elapsed time and MediaCodec video PTS have different origins.
                // Preserve source deltas within each track and align their origins by first arrival.
                long audioClock = pts is { } p && _audioBase is { } b ? p-b : _audioSamples*1_000_000/48000;
                long arrivalOffset = (long)((_audioArrival-_videoArrival)*1_000_000.0/Stopwatch.Frequency);
                long us = audioClock+arrivalOffset-_segmentBaseUs;
                long durationUs = (long)pcm.Length/4*1_000_000/48000;
                if (us + durationUs > 0)
                {
                    if (us < 0)
                    {
                        int skip = (int)Math.Min(pcm.Length/4, (-us*48000+999999)/1_000_000)*4;
                        if (skip < pcm.Length) _file.Audio(pcm.AsSpan(skip).ToArray(), 0);
                    }
                    else _file.Audio(pcm, us);
                }
            }
            _audioSamples += pcm.Length/4;
        }
        private void FinishSegment()
        {
            if (_file is null) return;
            _lastPath = _file.Complete(); _file = null;
            _session = null;
        }
        public void Complete()
        {
            FinishSegment();
            report(_lastPath is null ? "Grabación detenida: no llegó un keyframe; no se creó archivo." : $"Grabación guardada: {_lastPath} · {_segments} segmento(s).");
        }
        public void Abort() => _file?.Abort();
    }

    private static class H264
    {
        public static byte[] Configuration(byte[] packet, byte[]? configuration)
        {
            var units = Units(packet).Concat(configuration is null ? [] : Units(configuration)).ToArray();
            byte[]? sps = units.FirstOrDefault(n => n.Length >= 4 && (n[0]&31)==7);
            byte[]? pps = units.FirstOrDefault(n => n.Length > 0 && (n[0]&31)==8);
            if (sps is null || pps is null)
            {
                if (configuration is { Length: > 6 } && configuration[0]==1) return configuration.ToArray();
                throw new InvalidDataException("El keyframe H.264 no contiene configuración SPS/PPS válida.");
            }
            using var output = new MemoryStream(); output.Write([1,sps[1],sps[2],sps[3],255,225]);
            WriteLength(output,sps.Length); output.Write(sps); output.WriteByte(1); WriteLength(output,pps.Length); output.Write(pps); return output.ToArray();
        }
        private static void WriteLength(Stream stream,int length)
        {
            if (length>ushort.MaxValue) throw new InvalidDataException("SPS/PPS H.264 demasiado grande.");
            Span<byte> bytes=stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(bytes,(ushort)length); stream.Write(bytes);
        }
        public static byte[] Packet(byte[] data)
        {
            using var output=new MemoryStream();
            Span<byte> size=stackalloc byte[4];
            foreach(byte[] nal in Units(data))
            {
                if(nal.Length==0)continue;
                BinaryPrimitives.WriteInt32BigEndian(size,nal.Length); output.Write(size);output.Write(nal);
            }
            if(output.Length==0)throw new InvalidDataException("Paquete H.264 vacío.");
            return output.ToArray();
        }
        private static IEnumerable<byte[]> Units(byte[] data)
        {
            static int Prefix(byte[] bytes,int at)
            {
                if(at+2<bytes.Length && bytes[at]==0 && bytes[at+1]==0 && bytes[at+2]==1)return 3;
                if(at+3<bytes.Length && bytes[at]==0 && bytes[at+1]==0 && bytes[at+2]==0 && bytes[at+3]==1)return 4;
                return 0;
            }
            int first=-1;
            for(int i=0;i<data.Length-2;i++)if(Prefix(data,i)!=0){first=i;break;}
            if(first>=0)
            {
                int start=first+Prefix(data,first);
                for(int i=start;i<data.Length-2;i++)
                {
                    int size=Prefix(data,i);if(size==0)continue;
                    if(i>start)yield return data[start..i]; start=i+size;i=start-1;
                }
                if(start<data.Length)yield return data[start..];
                yield break;
            }
            // avcC is configuration, not a length-prefixed video packet.
            if(data.Length>6 && data[0]==1 && data[4]==255)yield break;
            int offset=0;
            while(offset+4<=data.Length)
            {
                int length=BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset,4));offset+=4;
                if(length<=0||length>data.Length-offset)throw new InvalidDataException("Paquete H.264 sin delimitadores válidos.");
                yield return data[offset..(offset+length)];offset+=length;
            }
            if(offset!=data.Length)throw new InvalidDataException("Paquete H.264 truncado.");
        }
    }

    private sealed class Matroska
    {
        private readonly FileStream _stream;
        private readonly string _finalPath;
        public string PartialPath { get; }
        private readonly long _segmentOffset, _segmentSizeOffset, _durationOffset, _audioTrackOffset;
        private readonly int _audioTrackLength;
        private long _durationUs;
        private readonly long _frameDurationUs;
        private bool _hasAudio;
        private bool _closed;
        private readonly List<(long Time,long Position)> _cues=[];
        public Matroska(string path,VideoSession session,int fps,byte[] avcc)
        {
            _finalPath=path;PartialPath=path+".partial";_frameDurationUs=1_000_000/fps;
            _stream=new FileStream(PartialPath,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read,65536,FileOptions.SequentialScan);
            try
            {
                _stream.Write(Element(0x1A45DFA3,Join(UInt(0x4286,1),UInt(0x42F7,1),UInt(0x42F2,4),UInt(0x42F3,8),Text(0x4282,"matroska"),UInt(0x4287,4),UInt(0x4285,2))));
                _stream.Write(Id(0x18538067));_segmentSizeOffset=_stream.Position;_stream.Write([0x01,255,255,255,255,255,255,255]);_segmentOffset=_stream.Position;
                byte[] infoPrefix=Join(UInt(0x2AD7B1,1_000_000),Text(0x4D80,"NOVORA-LINK"),Text(0x5741,"NOVORA-LINK VE"));
                byte[] duration=Float(0x4489,0);byte[] info=Join(infoPrefix,duration);
                _stream.Write(Id(0x1549A966));_stream.Write(Size((ulong)info.Length));
                _durationOffset=_stream.Position+infoPrefix.Length+Id(0x4489).Length+Size(8).Length;_stream.Write(info);
                int rotation=session.RotationDegrees==270?90:-session.RotationDegrees;
                byte[] video=Join(UInt(0xB0,(ulong)session.Width),UInt(0xBA,(ulong)session.Height),Element(0x7670,Join(UInt(0x7671,0),Float(0x7673,0),Float(0x7674,0),Float(0x7675,rotation))));
                byte[] videoTrack=Element(0xAE,Join(UInt(0xD7,1),UInt(0x73C5,1),UInt(0x83,1),UInt(0x9C,0),UInt(0x23E383,(ulong)(1_000_000_000L/fps)),Text(0x86,"V_MPEG4/ISO/AVC"),Element(0x63A2,avcc),Element(0xE0,video)));
                byte[] audioTrack=Element(0xAE,Join(UInt(0xD7,2),UInt(0x73C5,2),UInt(0x83,2),UInt(0x9C,0),Text(0x86,"A_PCM/INT/LIT"),Element(0xE1,Join(Float(0xB5,48000),UInt(0x9F,2),UInt(0x6264,16)))));
                _stream.Write(Id(0x1654AE6B));_stream.Write(Size((ulong)(videoTrack.Length+audioTrack.Length)));_stream.Write(videoTrack);
                _audioTrackOffset=_stream.Position;_audioTrackLength=audioTrack.Length;_stream.Write(audioTrack);
            }
            catch{_stream.Dispose();throw;}
        }
        public void Video(byte[] data,long timeUs,bool key)
        {
            Block(1,data,timeUs,key);if(key)_cues.Add((timeUs/1000,_lastClusterPosition));_durationUs=Math.Max(_durationUs,timeUs+_frameDurationUs);
        }
        public void Audio(byte[] pcm,long timeUs)
        {
            Block(2,pcm,timeUs,true);_hasAudio=true;_durationUs=Math.Max(_durationUs,timeUs+(long)pcm.Length/4*1_000_000/48000);
        }
        private long _lastClusterPosition;
        private void Block(int track,byte[] data,long timeUs,bool key)
        {
            if(timeUs<0)throw new InvalidDataException("Timestamp negativo de grabación.");
            byte[] block=new byte[data.Length+4];block[0]=(byte)(0x80|track);block[3]=key?(byte)0x80:(byte)0;data.CopyTo(block,4);
            _lastClusterPosition=_stream.Position-_segmentOffset;
            _stream.Write(Element(0x1F43B675,Join(UInt(0xE7,(ulong)(timeUs/1000)),Element(0xA3,block))));
        }
        public string Complete()
        {
            if(_closed)return _finalPath;
            try
            {
                byte[] cues=Join(_cues.Select(c=>Element(0xBB,Join(UInt(0xB3,(ulong)c.Time),Element(0xB7,Join(UInt(0xF7,1),UInt(0xF1,(ulong)c.Position)))))).ToArray());
                _stream.Write(Element(0x1C53BB6B,cues));long length=_stream.Position;
                _stream.Position=_durationOffset;_stream.Write(FloatPayload(_durationUs/1000.0));
                if(!_hasAudio){_stream.Position=_audioTrackOffset;_stream.Write(Void(_audioTrackLength));}
                _stream.Position=_segmentSizeOffset;_stream.Write(Size((ulong)(length-_segmentOffset),8));
                _stream.Flush(true);_stream.Dispose();_closed=true;
                File.Move(PartialPath,_finalPath,overwrite:false);return _finalPath;
            }
            catch{Abort();throw;}
        }
        public void Abort(){if(_closed)return;_closed=true;_stream.Dispose();}
        private static byte[] Id(uint id)
        {
            byte[] bytes=new byte[4];BinaryPrimitives.WriteUInt32BigEndian(bytes,id);return bytes.SkipWhile(b=>b==0).ToArray();
        }
        private static byte[] Size(ulong value,int width=0)
        {
            if(width==0){width=1;while(width<8&&value>=(1UL<<(width*7))-1)width++;}
            if(width>8||value>=(1UL<<(width*7))-1)throw new InvalidDataException("Elemento Matroska demasiado grande.");
            byte[] bytes=new byte[width];for(int i=width-1;i>=0;i--){bytes[i]=(byte)value;value>>=8;}bytes[0]|=(byte)(1<<(8-width));return bytes;
        }
        private static byte[] Element(uint id,byte[] payload)=>Join(Id(id),Size((ulong)payload.Length),payload);
        private static byte[] UInt(uint id,ulong value)
        {
            byte[] bytes=new byte[8];BinaryPrimitives.WriteUInt64BigEndian(bytes,value);int start=0;while(start<7&&bytes[start]==0)start++;return Element(id,bytes[start..]);
        }
        private static byte[] Text(uint id,string text)=>Element(id,Encoding.UTF8.GetBytes(text));
        private static byte[] FloatPayload(double value){byte[] bytes=new byte[8];BinaryPrimitives.WriteInt64BigEndian(bytes,BitConverter.DoubleToInt64Bits(value));return bytes;}
        private static byte[] Float(uint id,double value)=>Element(id,FloatPayload(value));
        private static byte[] Join(params byte[][] arrays)
        {
            byte[] result=new byte[arrays.Sum(a=>a.Length)];int offset=0;foreach(byte[] array in arrays){array.CopyTo(result,offset);offset+=array.Length;}return result;
        }
        private static byte[] Void(int length)
        {
            for(int width=1;width<=8;width++)
            {
                int payload=length-1-width;if(payload<0)continue;
                if((ulong)payload<(1UL<<(width*7))-1)return Join([0xEC],Size((ulong)payload,width),new byte[payload]);
            }
            throw new InvalidDataException("No se pudo finalizar la pista de audio opcional.");
        }
    }
}

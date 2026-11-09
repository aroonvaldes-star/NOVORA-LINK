using System.Diagnostics;
using System.Text.Json;
using NOVORA.Linux.VisionEngine;

internal static class VisionRecordingChecks
{
    public static async Task RunAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "novora-recording-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            byte[] stream = await GenerateAsync();
            byte[] resizedStream = await GenerateAsync(128,64);
            var session = new VideoSession(64, 32, 0, 2*1024*1024);
            byte[] pcm = new byte[4800*4];
            await using (var recorder = new LinuxVisionRecorder(directory, 10))
            {
                recorder.Start();
                recorder.WriteAudio(pcm, 1_000_000);
                recorder.WriteVideo(VideoCodec.H264,new(session,stream, 10_000_000, false, true), stream);
                recorder.WriteAudio(pcm, 1_100_000);
                recorder.WriteVideo(VideoCodec.H264,new(session,stream, 10_350_000, false, true), stream);
                recorder.WriteVideo(VideoCodec.H264,new(session with { RotationDegrees=90 },stream, 10_700_000, false, true), stream);
                recorder.WriteAudio(pcm, 1_700_000);
                recorder.WriteVideo(VideoCodec.H264,new(session with { Width=128,Height=64 },resizedStream, 11_000_000, false, true), resizedStream);
                recorder.WriteAudio(pcm, 2_000_000);
                await recorder.StopAsync();
                if (recorder.IsRecording) throw new Exception("Recorder remained active after StopAsync.");
                if (recorder.Status.Contains("Error",StringComparison.OrdinalIgnoreCase)) throw new Exception(recorder.Status);
            }
            string[] files = Directory.GetFiles(directory,"*.mkv");
            if (files.Length != 3) throw new Exception($"Expected 3 geometry segments, got {files.Length}.");
            Array.Sort(files,StringComparer.Ordinal);
            foreach (string file in files)
            {
                string json = await RunAsync("ffprobe",["-v","error","-show_streams","-show_packets","-of","json",file]);
                using var document = JsonDocument.Parse(json);
                var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToArray();
                if (!streams.Any(s=>s.GetProperty("codec_type").GetString()=="video" && s.GetProperty("codec_name").GetString()=="h264")) throw new Exception("Missing H264 track.");
                if (!streams.Any(s=>s.GetProperty("codec_type").GetString()=="audio" && s.GetProperty("codec_name").GetString()=="pcm_s16le")) throw new Exception("Missing PCM track.");
                var videoPackets = document.RootElement.GetProperty("packets").EnumerateArray()
                    .Where(p => p.GetProperty("codec_type").GetString()=="video").ToArray();
                if (file==files[0])
                {
                    double first=double.Parse(videoPackets[0].GetProperty("pts_time").GetString()!,System.Globalization.CultureInfo.InvariantCulture);
                    double second=double.Parse(videoPackets[1].GetProperty("pts_time").GetString()!,System.Globalization.CultureInfo.InvariantCulture);
                    if(Math.Abs(second-first-.35)>.001)throw new Exception("Original video PTS delta was not preserved.");
                }
                else if (file==files[1])
                {
                    var audioPackets=document.RootElement.GetProperty("packets").EnumerateArray().Where(p=>p.GetProperty("codec_type").GetString()=="audio").ToArray();
                    double audioStart=double.Parse(audioPackets[0].GetProperty("pts_time").GetString()!,System.Globalization.CultureInfo.InvariantCulture);
                    if(audioStart>.025)throw new Exception("Audio timestamps were not rebased for the rotated segment.");
                    var videoStream=streams.First(s=>s.GetProperty("codec_type").GetString()=="video");
                    if(!videoStream.TryGetProperty("side_data_list",out var sideData) || !sideData.EnumerateArray().Any(d=>d.TryGetProperty("rotation",out var r)&&Math.Abs(r.GetDouble())==90))
                        throw new Exception("Rotated segment lacks display rotation metadata.");
                }
                else
                {
                    var resized=streams.First(s=>s.GetProperty("codec_type").GetString()=="video");
                    if(resized.GetProperty("width").GetInt32()!=128 || resized.GetProperty("height").GetInt32()!=64)
                        throw new Exception("New geometry retained stale H264 SPS/PPS.");
                }
                await RunAsync("ffmpeg",["-hide_banner","-loglevel","error","-i",file,"-f","null","-"]);
            }
            if (Directory.GetFiles(directory,"*.partial").Length != 0) throw new Exception("Successful recording left partial output.");
            // A capture without audio must not advertise an empty audio track.
            await using (var recorder = new LinuxVisionRecorder(directory,10))
            {
                recorder.Start(); recorder.WriteVideo(VideoCodec.H264,new(session,stream,1,false,true),stream); await recorder.StopAsync();
            }
            string onlyVideo = Directory.GetFiles(directory,"*.mkv").Except(files).Single();
            string videoProbe=await RunAsync("ffprobe",["-v","error","-show_streams","-of","json",onlyVideo]);
            using(var doc=JsonDocument.Parse(videoProbe))
                if(doc.RootElement.GetProperty("streams").GetArrayLength()!=1) throw new Exception("Video-only recording advertises an empty audio track.");
            // Decoder failures or disk backpressure must be visible; never publish a corrupt final file.
            var failed = new LinuxVisionRecorder(directory,10);
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            failed.Changed += () => { if(failed.Status.StartsWith("Grabando:"))opened.TrySetResult(); };
            failed.Start(); failed.WriteVideo(VideoCodec.H264,new(session,stream,1,false,true),stream);
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
            failed.WriteVideo(VideoCodec.H265,new(session,stream,2,false,true),stream);
            try{await failed.StopAsync();throw new Exception("Unsupported recording codec was silently accepted.");}catch(IOException){}
            if(Directory.GetFiles(directory,"*.partial").Length!=1)throw new Exception("Failed recording did not preserve its partial file.");
            if(failed.IsRecording || !failed.Status.Contains("Error"))throw new Exception("Recording failure not reported.");
            try{await failed.DisposeAsync();}catch(IOException){}
            // Oversized queued video fails immediately rather than blocking the decoder or growing memory without bounds.
            var saturated=new LinuxVisionRecorder(directory,10); saturated.Start();
            saturated.WriteVideo(VideoCodec.H264,new(session,new byte[33*1024*1024],1,false,true),null);
            if(saturated.IsRecording)throw new Exception("Recording queue bound not enforced.");
            try{await saturated.StopAsync();throw new Exception("Queue overflow was hidden.");}catch(IOException){}
            try{await saturated.DisposeAsync();}catch(IOException){}
            Console.WriteLine("VisionEngine recording MKV/H264/PCM, PTS/rotation, segmentation, stop, video-only, partial recovery and queue-bound checks passed.");
        }
        finally { Directory.Delete(directory,true); }
    }
    private static async Task<byte[]> GenerateAsync(int width=64,int height=32)
    {
        var start=new ProcessStartInfo("ffmpeg"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        string encoder=await SoftwareH264EncoderAsync();
        foreach(string arg in new[]{"-hide_banner","-loglevel","error","-f","lavfi","-i",$"color=c=red:s={width}x{height}:r=10","-frames:v","1","-c:v",encoder,"-f","h264","pipe:1"}) start.ArgumentList.Add(arg);
        using var process=Process.Start(start) ?? throw new Exception("FFmpeg missing.");
        using var output=new MemoryStream(); Task copy=process.StandardOutput.BaseStream.CopyToAsync(output); Task<string> errors=process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(); await copy; if(process.ExitCode!=0) throw new Exception(await errors); return output.ToArray();
    }
    private static string? _encoder;
    /// <summary>Fedora ships libopenh264; Ubuntu (CI) ships libx264. Use whichever FFmpeg provides.</summary>
    private static async Task<string> SoftwareH264EncoderAsync()
    {
        if(_encoder is not null) return _encoder;
        var start=new ProcessStartInfo("ffmpeg"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add("-hide_banner"); start.ArgumentList.Add("-encoders");
        using var process=Process.Start(start) ?? throw new Exception("FFmpeg missing.");
        Task<string> errors=process.StandardError.ReadToEndAsync();
        string encoders=await process.StandardOutput.ReadToEndAsync(); await process.WaitForExitAsync(); await errors;
        return _encoder=new[]{"libx264","libopenh264"}.FirstOrDefault(name=>encoders.Contains(" "+name+" ",StringComparison.Ordinal))
            ?? throw new Exception("FFmpeg must provide libx264 or libopenh264 for the recording checks.");
    }
    private static async Task<string> RunAsync(string name,string[] args)
    {
        var start=new ProcessStartInfo(name){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true}; foreach(string arg in args)start.ArgumentList.Add(arg);
        using var process=Process.Start(start) ?? throw new Exception(name+" missing."); Task<string> output=process.StandardOutput.ReadToEndAsync(); Task<string> error=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try{await process.WaitForExitAsync(timeout.Token);}catch{process.Kill(true);throw;}
        if(process.ExitCode!=0)throw new Exception(await error); return await output;
    }
}

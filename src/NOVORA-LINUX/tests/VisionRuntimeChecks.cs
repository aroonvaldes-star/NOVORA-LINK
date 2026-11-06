using Avalonia;
using Avalonia.Threading;
using NOVORA.Linux;
using NOVORA.Linux.Control;
using NOVORA.Linux.VisionEngine;
using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;

internal static class VisionRuntimeChecks
{
    public static void RunNative()
    {
        AppBuilder.Configure<App>().UsePlatformDetect().SetupWithoutStarting();
        using var mainLoop=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task work=Task.Run(async()=>
        {
            string directory=Path.Combine(Path.GetTempPath(),"novora-ve-runtime-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                byte[] sample=await SampleAsync(stop.Token);
                using var video=await Pair.CreateAsync(stop.Token); using var control=await Pair.CreateAsync(stop.Token); using var audio=await Pair.CreateAsync(stop.Token);
                await using var runtime=new LinuxVisionRuntime(LinuxVisionSettings.Default with { Acceleration="Software",VSync=false,RecordingDirectory=directory },19);
                Task session=runtime.RunAsync(video.Server,control.Server,audio.Server,null,stop.Token);
                Task sender=Task.Run(async()=>
                {
                    try
                    {
                        byte[] header=new byte[16]; BinaryPrimitives.WriteUInt32BigEndian(header,(uint)VideoCodec.H264); header[4]=0x80;
                        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8),64); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12),32);
                        await video.Client.WriteAsync(header,stop.Token);
                        byte[] codec=new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(codec,(uint)AudioCodec.Raw); await audio.Client.WriteAsync(codec,stop.Token);
                        for(int i=0;i<100;i++)
                        {
                            byte[] packet=new byte[12+sample.Length]; BinaryPrimitives.WriteUInt64BigEndian(packet,(1UL<<61)|((ulong)i*40_000UL));
                            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8),(uint)sample.Length); sample.CopyTo(packet,12); await video.Client.WriteAsync(packet,stop.Token);
                            byte[] pcm=new byte[12+7680]; BinaryPrimitives.WriteUInt64BigEndian(pcm,(ulong)(i*40_000)); BinaryPrimitives.WriteUInt32BigEndian(pcm.AsSpan(8),7680);
                            await audio.Client.WriteAsync(pcm,stop.Token); await Task.Delay(40,stop.Token);
                        }
                    }
                    catch(Exception ex) when(ex is OperationCanceledException or IOException or ObjectDisposedException) { }
                });
                while(runtime.Telemetry.FramesRendered==0 || !runtime.AudioPlaying)
                { if(session.IsCompleted) await session; await Task.Delay(20,stop.Token); }
                runtime.StartRecording();
                JsonElement keyframe=await ControlProtocol.ReadAsync<JsonElement>(control.Client,stop.Token);
                if(keyframe.GetProperty("Type").GetInt32()!=17) throw new Exception("Recorder did not request Android keyframe");
                await Task.Delay(300,stop.Token);
                await runtime.StopRecordingAsync();
                if(!Directory.EnumerateFiles(directory,"*.mkv").Any()) throw new Exception("Live runtime recording was not finalized");
                await Dispatcher.UIThread.InvokeAsync(PushWindowClose);
                await session.WaitAsync(TimeSpan.FromSeconds(5));
                stop.Cancel();
                await sender;
                Console.WriteLine("VisionEngine live socket→FFmpeg→SDL video/audio, keyframe request, recording and shutdown checks passed.");
            }
            finally { if(Directory.Exists(directory)) Directory.Delete(directory,true); mainLoop.Cancel(); }
        });
        Dispatcher.UIThread.MainLoop(mainLoop.Token);
        work.GetAwaiter().GetResult();
    }
    private static void PushWindowClose()
    {
        IntPtr library=NativeLibrary.Load("libSDL3.so.0");
        IntPtr buffer=Marshal.AllocHGlobal(128);
        try
        {
            var push=Marshal.GetDelegateForFunctionPointer<PushEvent>(NativeLibrary.GetExport(library,"SDL_PushEvent"));
            Marshal.Copy(new byte[128],0,buffer,128); Marshal.WriteInt32(buffer,0x100);
            if(!push(buffer)) throw new Exception("SDL quit injection failed");
        }
        finally { Marshal.FreeHGlobal(buffer);NativeLibrary.Free(library); }
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)]
    private delegate bool PushEvent(IntPtr data);
    private static async Task<byte[]> SampleAsync(CancellationToken token)
    {
        var start=new ProcessStartInfo("ffmpeg") { RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false };
        foreach(var arg in new[]{"-hide_banner","-loglevel","error","-f","lavfi","-i","color=c=red:s=64x32:r=25","-frames:v","3","-c:v","libopenh264","-g","1","-f","h264","pipe:1"}) start.ArgumentList.Add(arg);
        using var process=Process.Start(start)!; using var bytes=new MemoryStream();
        Task copy=process.StandardOutput.BaseStream.CopyToAsync(bytes,token); Task<string> error=process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(token); await copy;
        if(process.ExitCode!=0) throw new Exception(await error);
        return bytes.ToArray();
    }
    private sealed class Pair(TcpClient server,TcpClient client) : IDisposable
    {
        public Stream Server=>server.GetStream(); public Stream Client=>client.GetStream();
        public static async Task<Pair> CreateAsync(CancellationToken token)
        {
            var listener=new TcpListener(IPAddress.Loopback,0); listener.Start();
            try
            {
                var client=new TcpClient(); Task connect=client.ConnectAsync(IPAddress.Loopback,((IPEndPoint)listener.LocalEndpoint).Port,token).AsTask();
                TcpClient server=await listener.AcceptTcpClientAsync(token); await connect; return new(server,client);
            }
            finally { listener.Stop(); }
        }
        public void Dispose() { server.Dispose();client.Dispose(); }
    }
}

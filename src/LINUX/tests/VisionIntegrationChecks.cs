using NOVORA.Linux.VisionEngine;
using NOVORA.Linux.Files;
internal static class VisionIntegrationChecks
{
    public static async Task RunAsync()
    {
        bool files=false,transport=false,gate=false;
        try
        {
            await LinuxVisionCleanup.RunAsync(()=>Task.FromException(new IOException("Recording disk failed")),
                ()=>{files=true;return Task.CompletedTask;},()=>{transport=true;return Task.CompletedTask;},()=>{gate=true;return Task.CompletedTask;});
            throw new Exception("Cleanup lost the original failure");
        }
        catch (AggregateException ex) { if(ex.InnerExceptions[0].Message!="Recording disk failed") throw; }
        if(!files || !transport || !gate) throw new Exception("Recording failure stranded session owners");
        var allocation=LinuxFilesNetwork.Allocate("{\"Generation\":17,\"Direction\":\"FromAndroid\"}",17,5,"LAN");
        if(allocation.Generation!=17 || allocation.Direction!=0 || allocation.Mode!=1 || allocation.BurstBytes>1048576 || allocation.ValidForMilliseconds>2000)
            throw new Exception("Android FILES allocation contract invalid");
        try { LinuxFilesNetwork.Allocate("{\"Generation\":16,\"Direction\":\"FromAndroid\"}",17,5,"LAN"); throw new Exception("Stale allocation accepted"); }
        catch(InvalidDataException) { }
        Console.WriteLine("VisionEngine failure cleanup and Android FILES budget contract checks passed.");
    }
}

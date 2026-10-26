namespace NOVORA.Service;
public sealed class NLServiceNovoraPaths
{
    public string Adb => "unused-adb.exe";
    public string ToolsDirectory => AppContext.BaseDirectory;
}
public sealed class NLServiceADB
{
    public Task StartServerAsync(CancellationToken token) {token.ThrowIfCancellationRequested();return Task.CompletedTask;}
    public Task ExecuteRawAsync(string[] arguments,CancellationToken token) {token.ThrowIfCancellationRequested();return Task.CompletedTask;}
}

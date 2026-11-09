// Only WPF scheduling and protocol data are substituted; the production dispatch guard is linked.
namespace NOVORA.Control
{
    public sealed record NLControlRequest(long Id);
    public sealed record NLControlReply(int Version, long Id, bool Success, string Message);
    public static class NLControlProtocol { public const int Version = 1; }
    public sealed class NLControlTrustServer { public bool IsAuthorized { get; set; } = true; }
}

namespace NOVORA
{
    using NOVORA.Control;
    public partial class NLUIWindowMain
    {
        internal long _androidControlGeneration;
        internal NLControlTrustServer? _androidControl, _androidLanControl;
        private bool AndroidControlSessionOpen => _androidControl is not null || _androidLanControl is not null;
        internal readonly QueuedDispatcher Dispatcher = new();
        internal int Applied;
        private Task<NLControlReply> ApplyAndroidControlAsync(NLControlRequest request, string transport)
        { Applied++; return Task.FromResult(new NLControlReply(1, request.Id, true, transport)); }
        internal Task<NLControlReply> Send(string route, NLControlTrustServer server) =>
            HandleAndroidControlAsync(new(1), _androidControlGeneration, route, server);
    }

    internal sealed class QueuedDispatcher
    {
        private Action? _queued;
        internal Operation<T> InvokeAsync<T>(Func<T> callback)
        {
            var source = new TaskCompletionSource<T>();
            _queued = () => source.SetResult(callback());
            return new(source.Task);
        }
        internal void Drain() { var action = _queued; _queued = null; action!(); }
    }
    internal sealed record Operation<T>(Task<T> Task);
}

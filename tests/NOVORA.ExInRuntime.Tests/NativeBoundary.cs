using System.Threading.Channels;
using NOVORA.ExInEngine;

namespace NOVORA.Service
{
    // Substitute only filesystem placement; production profile persistence remains real.
    public sealed class NLServiceNovoraPaths
    {
        public string ExInProfilesDirectory { get; } = Path.Combine(Path.GetTempPath(), "novora-exin-runtime", Guid.NewGuid().ToString("N"));
    }
}

namespace NOVORA.ExInEngine
{
    // SDL is the external native boundary. Manager, Core, router, generations,
    // report encoding, pointer translation and calibration are source-linked unchanged.
    public sealed class ExInSdl : IDisposable
    {
        public static ExInSdl Last = null!;
        public static uint[] InitialIds = [1];
        public readonly Dictionary<uint, ExInState> States = [];
        public readonly Channel<ExInEvent> Events = Channel.CreateUnbounded<ExInEvent>();
        public int InitializeCount;
        public int FailInitializations;
        public ExInSdl(NOVORA.Service.NLServiceNovoraPaths paths)
        {
            Last = this;
            foreach (uint id in InitialIds) States[id] = default;
        }
        public void InitializeVE()
        {
            InitializeCount++;
            if (FailInitializations-- > 0) throw new IOException("transient native initialization");
        }
        public IReadOnlyList<uint> GetDetectedIdsVE() => States.Keys.ToArray();
        public IntPtr OpenVE(uint id) => (IntPtr)id;
        public void CloseVE(IntPtr handle) { }
        public ExInControllerIdentity GetIdentityVE(IntPtr handle) => ExInControllerIdentity.CreateVE((uint)handle, 0x045E, 0x028E, Guid.Empty, "fixture gamepad", handle.ToString(), null);
        public ExInBatteryStatus GetBatteryStatusVE(IntPtr handle, ExInControllerIdentity identity) => ExInBatteryStatus.CreateVE(identity.ProfileKey, identity.Name, -1, ExInBatteryState.Unknown);
        public ExInState ReadStateVE(IntPtr handle) => States[(uint)handle];
        public IAsyncEnumerable<ExInEvent> ReadEventsVE(CancellationToken token) => Events.Reader.ReadAllAsync(token);
        public void Dispose() { }
    }
}

using System.Collections.Generic;

namespace NOVORA.Control;

public enum NLNetworkDomain { Internet, DeviceLink }
public enum NLNetworkDirection { FromAndroid, ToAndroid }
public enum NLNetworkTrafficClass { Interactive, Audio, Video, BestEffort, Bulk }
public enum NLNetworkPolicyMode { Disabled, Observe, Manual, Adaptive }
public enum NLNetworkSampleSource { UsbControl, LanControl, TunnelHttp, InternetPcHttp, InternetAndroidHttp, Pipeline }
public sealed record NLNetworkPolicy(long Revision, long Generation, string LinkKey,
    NLNetworkDomain Domain, NLNetworkPolicyMode Mode,
    long? FromAndroidBytesPerSecond, long? ToAndroidBytesPerSecond);
public sealed record NLNetworkSample(long Generation, string LinkKey, NLNetworkDomain Domain,
    NLNetworkDirection Direction, NLNetworkSampleSource Source,
    long ObservedAtTicks, double WindowSeconds,
    double? RttMs, double? WriteWaitP95Ms, double? QueueAgeP95Ms,
    long QueueBytes, long UsefulBytes, long ProtocolBytes, long Dropped, bool Valid);
public sealed record NLNetworkGrant(long Revision, long Generation, string LinkKey,
    NLNetworkDomain Domain, NLNetworkDirection Direction, NLNetworkTrafficClass TrafficClass,
    long BytesPerSecond, int BurstBytes, long ValidUntilTicks);
public sealed record NLNetworkBaseline(long Generation, string LinkKey,
    NLNetworkDomain Domain, NLNetworkSampleSource Source,
    double RttP95Ms, long ObservedAtTicks, bool QuietCalibrationConfirmed);
public sealed record NLControlNetworkCapabilities(int Version = 1,
    bool StabilityTelemetry = false, bool LiveVideoBitrate = false,
    bool CaptureFramePacing = false, bool LiveGeometry = false, int[]? FilesWireVersions = null);
public sealed record NLNetworkDecision(NLNetworkPolicy AppliedPolicy,
    IReadOnlyList<NLNetworkGrant> Grants, bool Congested, double VideoRateFactor, string Reason);

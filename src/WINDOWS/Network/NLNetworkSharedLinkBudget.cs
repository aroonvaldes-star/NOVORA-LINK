using System;

namespace NOVORA.Network;

public sealed record NLNetworkSharedLinkAllocation(long VeFromAndroid,long VeToAndroid,long RelayFromAndroid,long RelayToAndroid);
/// <summary>Used only after the user confirms that both current engines use one physical route.</summary>
public static class NLNetworkSharedLinkBudget
{
    public static NLNetworkSharedLinkAllocation Split(long? fromAndroid,long? toAndroid,long videoBytesPerSecond,long audioBytesPerSecond)
    {
        if(fromAndroid is null || toAndroid is null || fromAndroid>NLNetworkBudget.MaximumBytesPerSecond || toAndroid>NLNetworkBudget.MaximumBytesPerSecond ||
            videoBytesPerSecond<0 || audioBytesPerSecond<0 || fromAndroid<audioBytesPerSecond+64_000+31_250+64_000 || toAndroid<128_000)
            throw new ArgumentException("El enlace compartido necesita ambas capacidades medidas y margen para audio, control, video mínimo y LE.");
        long remaining=Math.Max(0,fromAndroid.Value-videoBytesPerSecond-audioBytesPerSecond-64_000);
        long relayUp=Math.Clamp(remaining/2,64_000,fromAndroid.Value-audioBytesPerSecond-64_000-31_250);
        long relayDown=Math.Max(64_000,(toAndroid.Value-64_000)/2);
        return new(fromAndroid.Value-relayUp,toAndroid.Value-relayDown,relayUp,relayDown);
    }
}

using NOVORA.ViewModel;
using NOVORA.VisionEngine.Performance;

namespace NOVORA.Service;

public static class NLServiceVideoProfile
{
    public static VEPerformanceOptions ApplyVE(NLViewModelMain view, VEPerformanceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(view);
        var options = VEPerformanceOptions.CreateVE(profile);
        view.Bitrate = (options.RecommendedBitrate / 1_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture) + "M";
        view.MaxSize = options.MaxSize;
        view.TargetFps = (int)options.MaxFps;
        return options;
    }
}

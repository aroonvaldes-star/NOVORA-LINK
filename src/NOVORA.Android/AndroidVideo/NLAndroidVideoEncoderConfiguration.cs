namespace NOVORA.AndroidVideo;

/// <summary>Portable selection and retry policy; Android owns capability queries and JNI calls.</summary>
internal static class NLAndroidVideoEncoderConfiguration
{
    internal const int KeyFrameIntervalSeconds = 2;

    internal static IReadOnlyDictionary<string, int> Options(int bitrate, int fps, int apiLevel,
        bool supportsCbr, bool supportsNoBFrames, bool advanced = true)
    {
        var result = new Dictionary<string, int>
        {
            ["color-format"] = unchecked((int)0x7F000789), ["bitrate"] = bitrate,
            ["frame-rate"] = fps,
            // A short period bounds a frozen picture when a request-sync is ignored or a
            // packet is lost; recovery still asks for sync frames through the one-second gate.
            ["i-frame-interval"] = KeyFrameIntervalSeconds
        };
        if (!advanced) return result;
        if (supportsCbr) result["bitrate-mode"] = 2;
        // KEY_MAX_B_FRAMES is official from API29; its default is already zero.
        if (apiLevel >= 29 && supportsNoBFrames) result["max-bframes"] = 0;
        if (apiLevel >= 26) result["latency"] = 1;
        if (apiLevel >= 23)
        {
            result["priority"] = 0;
            // Without an operating rate the vendor encoder may clock down and take longer per frame.
            result["operating-rate"] = fps;
        }
        return result;
    }

    internal static void Configure(Action<bool> configure, Action reset, Func<Exception, bool> mayRetry,
        Action<Exception> reportFallback)
    {
        try { configure(true); }
        catch (Exception ex) when (mayRetry(ex))
        {
            reportFallback(ex);
            reset();
            // The second failure propagates: options are all removed, without hiding
            // invalid geometry/bitrate, unavailable resources or a broken codec.
            configure(false);
        }
    }
}

using Android.Content;
using Android.Widget;

namespace NOVORA.AndroidUI;

internal static class NLAndroidUIVisionSettingsPage
{
    internal static TextView VideoSection(Context context) =>
        NLAndroidUIComponents.SectionTitle(context, "VisionEngine");

    internal static TextView ApplyHint(Context context) =>
        NLAndroidUIComponents.StatusChip(context,
            "Monitor, perfil, bitrate, resolución y FPS se aplican juntos.",
            NLAndroidUIStatusTone.Neutral);
}

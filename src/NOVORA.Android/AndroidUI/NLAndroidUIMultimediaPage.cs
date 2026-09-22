using Android.Content;
using Android.Widget;

namespace NOVORA.AndroidUI;

internal static class NLAndroidUIMultimediaPage
{
    internal static TextView Heading(Context context) =>
        NLAndroidUIComponents.SectionTitle(context, "Audio, grabación y captura");

    internal static TextView FloatingControlHeading(Context context) =>
        NLAndroidUIComponents.SectionTitle(context, "Control flotante opcional");
}

using Android.Content;
using Android.Widget;

namespace NOVORA.AndroidUI;

internal static class NLAndroidUIMultimediaPage
{
    internal static TextView Heading(Context context) =>
        NLAndroidUIComponents.SectionTitle(context, "Audio, grabación y captura");
}

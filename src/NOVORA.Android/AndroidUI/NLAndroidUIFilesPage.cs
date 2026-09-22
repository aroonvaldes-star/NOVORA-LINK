using Android.Content;
using Android.Widget;

namespace NOVORA.AndroidUI;

internal static class NLAndroidUIFilesPage
{
    internal static TextView Heading(Context context) =>
        NLAndroidUIComponents.SectionTitle(context, "Archivos reales de la sesión");
}

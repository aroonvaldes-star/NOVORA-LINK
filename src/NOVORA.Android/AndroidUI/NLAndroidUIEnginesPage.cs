using Android.Content;
using Android.Widget;

namespace NOVORA.AndroidUI;

internal static class NLAndroidUIEnginesPage
{
    internal static TextView IndependentLabel(Context context) =>
        NLAndroidUIComponents.SectionTitle(context, "Motores independientes");
}

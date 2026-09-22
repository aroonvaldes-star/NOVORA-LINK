using Android.Content;
using Android.Widget;

namespace NOVORA.AndroidUI;

internal static class NLAndroidUIConnectPage
{
    internal static TextView UsbPriorityLabel(Context context) =>
        NLAndroidUIComponents.SectionTitle(context, "USB físico prioritario");

    internal static TextView AuxiliaryLabel(Context context) =>
        NLAndroidUIComponents.SectionTitle(context, "Conexiones auxiliares");
}

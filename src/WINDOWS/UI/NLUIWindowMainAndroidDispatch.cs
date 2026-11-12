using NOVORA.Control;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private Task<NLControlReply> HandleAndroidControlAsync(NLControlRequest request, long generation, string transport,
        NLControlTrustServer? expectedServer = null) =>
        Dispatcher.InvokeAsync(() => generation == _androidControlGeneration && AndroidControlSessionOpen &&
            (expectedServer is null || ReferenceEquals(expectedServer, transport == "USB" ? _androidControl : _androidLanControl)) &&
            (transport == "USB" ? _androidControl?.IsAuthorized == true : _androidLanControl?.IsAuthorized == true)
            ? ApplyAndroidControlAsync(request, transport)
            : Task.FromResult(new NLControlReply(NLControlProtocol.Version, request.Id, false,
                "Sesión revocada."))).Task.Unwrap();

}

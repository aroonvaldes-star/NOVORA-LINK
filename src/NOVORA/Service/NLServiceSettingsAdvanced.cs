using NOVORA.ViewModel;

namespace NOVORA.Service;

internal static class NLServiceSettingsAdvanced
{
    internal static void CaptureNV(NLViewModelMain view, NLServiceNovoraSettings settings)
    {
        settings.PrivacyShieldEnabled = view.PrivacyShieldEnabled;
        settings.IntegrationClipboardEnabled = view.IntegrationClipboardEnabled;
        settings.IntegrationFileTransferEnabled = view.IntegrationFileTransferEnabled;
        settings.IntegrationDragDropEnabled = view.IntegrationDragDropEnabled;
        settings.IntegrationApplicationsEnabled = view.IntegrationApplicationsEnabled;
        settings.IntegrationNotificationsEnabled = view.IntegrationNotificationsEnabled;
        settings.IntegrationDynamicResizeEnabled = view.IntegrationDynamicResizeEnabled;
        settings.ExInEnabled = view.ExInEnabled;
        settings.NvidiaProfile = view.NvidiaProfile;
    }

    internal static void ApplyNV(NLServiceNovoraSettings settings, NLViewModelMain view)
    {
        view.PrivacyShieldEnabled = settings.PrivacyShieldEnabled;
        view.IntegrationClipboardEnabled = settings.IntegrationClipboardEnabled;
        view.IntegrationFileTransferEnabled = settings.IntegrationFileTransferEnabled;
        view.IntegrationDragDropEnabled = settings.IntegrationDragDropEnabled;
        view.IntegrationApplicationsEnabled = settings.IntegrationApplicationsEnabled;
        view.IntegrationNotificationsEnabled = settings.IntegrationNotificationsEnabled;
        view.IntegrationDynamicResizeEnabled = settings.IntegrationDynamicResizeEnabled;
        view.ExInEnabled = settings.ExInEnabled;
        view.NvidiaProfile = settings.NvidiaProfile;
    }
}

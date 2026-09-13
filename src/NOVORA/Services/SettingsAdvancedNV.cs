using NOVORA.ViewModels;

namespace NOVORA.Services;

internal static class SettingsAdvancedNV
{
    internal static void CaptureNV(MainViewModel view, NovoraSettings settings)
    {
        settings.PrivacyShieldEnabled = view.PrivacyShieldEnabled;
        settings.IntegrationClipboardEnabled = view.IntegrationClipboardEnabled;
        settings.IntegrationFileTransferEnabled = view.IntegrationFileTransferEnabled;
        settings.IntegrationDragDropEnabled = view.IntegrationDragDropEnabled;
        settings.IntegrationApplicationsEnabled = view.IntegrationApplicationsEnabled;
        settings.IntegrationNotificationsEnabled = view.IntegrationNotificationsEnabled;
        settings.IntegrationDynamicResizeEnabled = view.IntegrationDynamicResizeEnabled;
        settings.GamepadEnabled = view.GamepadEnabled;
        settings.RemoteAndroidEnabled = view.RemoteAndroidEnabled;
        settings.NvidiaProfile = view.NvidiaProfile;
    }

    internal static void ApplyNV(NovoraSettings settings, MainViewModel view)
    {
        view.PrivacyShieldEnabled = settings.PrivacyShieldEnabled;
        view.IntegrationClipboardEnabled = settings.IntegrationClipboardEnabled;
        view.IntegrationFileTransferEnabled = settings.IntegrationFileTransferEnabled;
        view.IntegrationDragDropEnabled = settings.IntegrationDragDropEnabled;
        view.IntegrationApplicationsEnabled = settings.IntegrationApplicationsEnabled;
        view.IntegrationNotificationsEnabled = settings.IntegrationNotificationsEnabled;
        view.IntegrationDynamicResizeEnabled = settings.IntegrationDynamicResizeEnabled;
        view.GamepadEnabled = settings.GamepadEnabled;
        view.RemoteAndroidEnabled = settings.RemoteAndroidEnabled;
        view.NvidiaProfile = settings.NvidiaProfile;
    }
}

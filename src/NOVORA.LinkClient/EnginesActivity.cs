using Android.App;
using Android.OS;
using Android.Widget;
using Android.Content;
using Android.Net;
using NOVORA.Control;

namespace NOVORA.LinkClient;

[Activity(Label = "Engines")]
public sealed class EnginesActivity : BaseActivity
{
    private const int VpnPermissionRequest = 401;
    private bool _rendering;
    private NovoraViewState _view = NovoraViewState.From(new(0, NLControlSessionPhase.Disconnected, "", "", null));

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_engines);
        ConfigureNavigation();

        var resolutionBadge = FindViewById<TextView>(Resource.Id.resolution_badge)!;
        FindViewById<RadioGroup>(Resource.Id.resolution_group)!.CheckedChange += async (_, e) =>
        {
            if (_rendering) return;
            string label = e.CheckedId == Resource.Id.resolution_1080 ? "1080" : "1440";
            resolutionBadge.Text = label == "1080" ? "Resolución: 1080p" : "Resolución: 1440p QHD";
            await SendVideoOptionAsync("resolution", label);
        };

        var fpsBadge = FindViewById<TextView>(Resource.Id.fps_badge)!;
        FindViewById<RadioGroup>(Resource.Id.fps_group)!.CheckedChange += async (_, e) =>
        {
            if (_rendering) return;
            string value = e.CheckedId == Resource.Id.fps_60 ? "60" : "120";
            fpsBadge.Text = $"Tasa de refresco: {value} FPS";
            await SendVideoOptionAsync("fps", value);
        };

        FindViewById<Button>(Resource.Id.button_detect)!.Click += async (_, _) => await RunCommandAsync("get");
        FindViewById<Button>(Resource.Id.button_restart)!.Click += async (_, _) => await RunCommandAsync("restartVideo");
        FindViewById<Button>(Resource.Id.button_toggle_video)!.Click += async (_, _) => await RunCommandAsync(_view.VideoButton.Action);
        FindViewById<Button>(Resource.Id.button_toggle_link)!.Click += async (_, _) => await ToggleLinkAsync();
        FindViewById<RadioGroup>(Resource.Id.controller_group)!.Enabled = false;
        FindViewById<Button>(Resource.Id.button_calibration_next)!.Enabled = false;
        FindViewById<Button>(Resource.Id.button_calibration_reset)!.Enabled = false;
    }

    private async Task SendVideoOptionAsync(string action, string text)
    {
        if (Connection.Phase != NLControlSessionPhase.Connected || Connection.Busy) return;
        NLControlOption[] options = action == "resolution"
            ? Connection.Snapshot?.VideoSettings?.Resolutions ?? []
            : Connection.Snapshot?.VideoSettings?.FrameRates ?? [];
        NLControlOption? option = options.FirstOrDefault(item =>
            item.Label.Contains(text, StringComparison.OrdinalIgnoreCase) || item.Value.Contains(text, StringComparison.OrdinalIgnoreCase));
        if (option is null) { ShowMessage($"NOVORA PC no ofrece la opción {text}."); return; }
        await RunCommandAsync(action, option.Value);
    }

    private async Task ToggleLinkAsync()
    {
        if (_view.LinkButton.Action == "stopLink")
        {
            NLAndroidVpnService.Stop(this);
            await RunCommandAsync("stopLink");
            return;
        }
        Intent? permission = VpnService.Prepare(this);
        if (permission is not null)
        {
#pragma warning disable CA1422
            StartActivityForResult(permission, VpnPermissionRequest);
#pragma warning restore CA1422
            return;
        }
        await StartLinkAsync();
    }

    private async Task StartLinkAsync()
    {
        try
        {
            NLControlReply reply = await NovoraConnection.SendAsync("startLink");
            if (!reply.Success || string.IsNullOrWhiteSpace(reply.Value)) throw new InvalidOperationException(reply.Message);
            NLControlLinkOffer offer = System.Text.Json.JsonSerializer.Deserialize<NLControlLinkOffer>(reply.Value)
                ?? throw new InvalidDataException("NOVORA PC no entregó el transporte DATA de LinkEngine.");
            NLAndroidVpnService.Start(this, offer);
            ShowMessage(reply.Message);
        }
        catch (Exception ex)
        {
            NLAndroidVpnService.Stop(this);
            ShowMessage(ex.Message);
        }
    }

#pragma warning disable CS0672, CA1422
    protected override async void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != VpnPermissionRequest) return;
        if (resultCode == Result.Ok) await StartLinkAsync();
        else ShowMessage("Android no autorizó la VPN local de LinkEngine.");
    }
#pragma warning restore CS0672, CA1422

    protected override void RenderConnection(NLControlSessionState state)
    {
        base.RenderConnection(state);
        _view = NovoraViewState.From(state);
        bool connected = state.Phase == NLControlSessionPhase.Connected;
        FindViewById<Button>(Resource.Id.button_detect)!.Enabled = connected && !state.Busy;
        FindViewById<Button>(Resource.Id.button_restart)!.Enabled = connected && !state.Busy && state.Snapshot?.VideoRunning == true;
        FindViewById<TextView>(Resource.Id.engines_bus_status)!.Text = _view.Connection;
        FindViewById<TextView>(Resource.Id.link_state)!.Text = state.Snapshot?.Engines?.LinkState ?? "No disponible";
        FindViewById<TextView>(Resource.Id.link_details)!.Text = _view.LinkStatus;
        FindViewById<TextView>(Resource.Id.video_details)!.Text = _view.VideoDetails;
        FindViewById<TextView>(Resource.Id.exin_state)!.Text = _view.ExInStatus;
        var videoButton = FindViewById<Button>(Resource.Id.button_toggle_video)!;
        videoButton.Text = _view.VideoButton.Label;
        videoButton.Enabled = _view.VideoButton.Enabled;
        var linkButton = FindViewById<Button>(Resource.Id.button_toggle_link)!;
        linkButton.Text = _view.LinkButton.Label;
        linkButton.Enabled = _view.LinkButton.Enabled;
        FindViewById<TextView>(Resource.Id.controller_details)!.Text = _view.ControllerDetails;
        FindViewById<TextView>(Resource.Id.stick_left)!.Text = _view.LeftStick;
        FindViewById<TextView>(Resource.Id.stick_right)!.Text = _view.RightStick;
        FindViewById<TextView>(Resource.Id.left_trigger)!.Text = _view.LeftTrigger;
        FindViewById<TextView>(Resource.Id.right_trigger)!.Text = _view.RightTrigger;
        FindViewById<TextView>(Resource.Id.calibration_status)!.Text = state.Snapshot?.ExIn is { } exIn
            ? $"{exIn.Message}\n{exIn.CalibrationDetails}\nCalibración: {(exIn.Calibrating ? "en curso" : exIn.Calibrated ? "completa" : "pendiente")}" : "Sin control físico detectado.";
        _rendering = true;
        FindViewById<RadioGroup>(Resource.Id.controller_group)!.ClearCheck();
        _rendering = false;
        if (state.Snapshot?.VideoSettings is { } video)
        {
            FindViewById<TextView>(Resource.Id.resolution_badge)!.Text = $"Resolución: {video.Resolution}";
            FindViewById<TextView>(Resource.Id.fps_badge)!.Text = $"Tasa de refresco: {video.Fps} FPS";
            _rendering = true;
            FindViewById<RadioButton>(Resource.Id.resolution_1080)!.Checked = video.Resolution.Contains("1080", StringComparison.OrdinalIgnoreCase);
            FindViewById<RadioButton>(Resource.Id.resolution_1440)!.Checked = video.Resolution.Contains("1440", StringComparison.OrdinalIgnoreCase);
            FindViewById<RadioButton>(Resource.Id.fps_60)!.Checked = video.Fps.Contains("60", StringComparison.OrdinalIgnoreCase);
            FindViewById<RadioButton>(Resource.Id.fps_120)!.Checked = video.Fps.Contains("120", StringComparison.OrdinalIgnoreCase);
            _rendering = false;
        }
    }
}

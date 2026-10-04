using Android.App;
using Android.OS;
using Android.Widget;
using NOVORA.Control;

namespace NOVORA.LinkClient;

[Activity(Label = "Archivos")]
public sealed class FilesActivity : BaseActivity
{
    private NovoraViewState _view = NovoraViewState.From(new(0, NLControlSessionPhase.Disconnected, "", "", null));

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_files);
        ConfigureNavigation();

        FindViewById<Button>(Resource.Id.button_pause)!.Enabled = false;
        FindViewById<Button>(Resource.Id.button_cancel)!.Enabled = false;
        FindViewById<Button>(Resource.Id.button_received)!.Enabled = false;
        FindViewById<Button>(Resource.Id.button_sent)!.Enabled = false;
        FindViewById<Button>(Resource.Id.button_send_pc)!.Click += (_, _) =>
            ShowMessage("La transferencia P2P aún no está habilitada en este cliente independiente.");
        FindViewById<Button>(Resource.Id.button_capture)!.Click += async (_, _) => await RunCommandAsync("capture");
        FindViewById<Button>(Resource.Id.button_record)!.Click += async (_, _) => await RunCommandAsync(_view.RecordButton.Action);
        FindViewById<RadioGroup>(Resource.Id.audio_route_group)!.Enabled = false;
    }

    protected override void RenderConnection(NLControlSessionState state)
    {
        base.RenderConnection(state);
        _view = NovoraViewState.From(state);
        FindViewById<TextView>(Resource.Id.files_status)!.Text = _view.FileSharing ? "Archivos disponibles" : "Archivos no disponibles";
        FindViewById<TextView>(Resource.Id.transfer_title)!.Text = _view.FileSharing ? "Canal P2P disponible" : "Sin transferencia activa";
        FindViewById<TextView>(Resource.Id.transfer_details)!.Text = _view.FileSharing
            ? "NOVORA PC permite transferencias; el selector de archivos se habilitará al integrar el canal binario."
            : "NOVORA PC no anunció transferencia de archivos.";
        FindViewById<ProgressBar>(Resource.Id.progress_transfer)!.Progress = 0;
        FindViewById<TextView>(Resource.Id.audio_status)!.Text = state.Snapshot is null
            ? "Sin datos de audio" : $"Configurado: {state.Snapshot.AudioOutput}\nActivo: {state.Snapshot.ActiveAudioOutput}\n{_view.MediaStatus}";
        FindViewById<Button>(Resource.Id.button_capture)!.Enabled = _view.CaptureEnabled;
        var record = FindViewById<Button>(Resource.Id.button_record)!;
        record.Text = _view.RecordButton.Label;
        record.Enabled = _view.RecordButton.Enabled;
        FindViewById<Button>(Resource.Id.button_send_pc)!.Enabled = false;
    }
}

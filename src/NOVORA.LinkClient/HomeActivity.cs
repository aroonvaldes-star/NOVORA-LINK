using Android.App;
using Android.OS;
using Android.Widget;
using NOVORA.Control;

namespace NOVORA.LinkClient;

[Activity(Label = "NOVORA-LINK", MainLauncher = true, Exported = true)]
public sealed class HomeActivity : BaseActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_home);
        ConfigureNavigation();

        FindViewById<Button>(Resource.Id.button_check_usb)!.Click += (_, _) =>
            ShowMessage(Connection.Transport == "USB"
                ? "USB conectado a NOVORA."
                : "Conecta el cable USB y abre NOVORA en la PC para detección automática.");
        FindViewById<Button>(Resource.Id.button_link_lan)!.Click += (_, _) => ShowPairDialog();
    }


    private void ShowPairDialog()
    {
        var entry = new EditText(this) { Hint = "000000", InputType = Android.Text.InputTypes.ClassNumber };
        entry.SetFilters([new Android.Text.InputFilterLengthFilter(6)]);
        new AlertDialog.Builder(this)!.SetTitle("Código LAN de NOVORA PC")!
            .SetMessage("Escribe el código temporal mostrado por NOVORA.")!.SetView(entry)!
            .SetNegativeButton("Cancelar", (_, _) => { })!
            .SetPositiveButton("Conectar", async (_, _) =>
            {
                string code = entry.Text?.Trim() ?? "";
                if (code.Length != 6 || !code.All(char.IsAsciiDigit)) { ShowMessage("El código debe tener 6 dígitos."); return; }
                try { await NovoraConnection.PairAsync(code); ShowMessage("Conectado a NOVORA PC."); }
                catch (Exception ex) { ShowMessage(ex.Message); }
            })!.Show();
    }

    protected override void RenderConnection(NLControlSessionState state)
    {
        base.RenderConnection(state);
        NovoraViewState view = NovoraViewState.From(state);
        var button = FindViewById<Button>(Resource.Id.button_link_lan);
        if (button is null) return;
        button.Text = state.Phase == NLControlSessionPhase.Connected ? $"Conectado · {view.PcName}" : "Vincular";
        button.Enabled = !state.Busy && state.Phase != NLControlSessionPhase.Connecting;
        FindViewById<TextView>(Resource.Id.home_pc_name)!.Text = view.PcName;
        FindViewById<TextView>(Resource.Id.home_pc_details)!.Text = state.Snapshot is null
            ? state.Message : $"NOVORA {state.Snapshot.PcVersion} · {view.Transport}";
        FindViewById<TextView>(Resource.Id.home_pc_status)!.Text = $"●  {view.Connection}";
        FindViewById<TextView>(Resource.Id.home_phone_name)!.Text = $"{Build.Manufacturer} {Build.Model}";
        FindViewById<TextView>(Resource.Id.home_phone_details)!.Text = $"Android {Build.VERSION.Release} · Cliente independiente";
        FindViewById<TextView>(Resource.Id.home_lan_status)!.Text = view.Transport;
        FindViewById<TextView>(Resource.Id.home_link_status)!.Text = $"↔  LinkEngine\n     {view.LinkStatus}";
        FindViewById<TextView>(Resource.Id.home_video_status)!.Text = $"▣  VisionEngine\n     {view.VideoStatus}";
        FindViewById<TextView>(Resource.Id.home_exin_status)!.Text = $"✚  ExInEngine\n     {view.ExInStatus}";
    }
}

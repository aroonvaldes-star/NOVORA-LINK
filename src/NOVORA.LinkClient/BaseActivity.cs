using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;
using NOVORA.Control;

namespace NOVORA.LinkClient;

public abstract class BaseActivity : Activity
{
    private NLControlSessionPhase _lastConnectionPhase = NLControlSessionPhase.Disconnected;
    protected NLControlSessionState Connection => NovoraConnection.Current;

    protected override void OnResume()
    {
        base.OnResume();
        NovoraConnection.Changed += ConnectionChanged;
        RenderConnection(Connection);
    }

    protected override void OnPause()
    {
        NovoraConnection.Changed -= ConnectionChanged;
        base.OnPause();
    }

    private void ConnectionChanged(object? sender, NLControlSessionState state) =>
        RunOnUiThread(() => RenderConnection(state));

    protected virtual void RenderConnection(NLControlSessionState state)
    {
        if (_lastConnectionPhase == NLControlSessionPhase.Connected &&
            state.Phase != NLControlSessionPhase.Connected)
            NovoraVeLanCaptureService.Stop(this);
        _lastConnectionPhase = state.Phase;
        var status = FindViewById<TextView>(Resource.Id.connection_status);
        if (status is not null) status.Text = NovoraViewState.From(state).Connection;
    }

    protected async Task RunCommandAsync(string action, string? value = null)
    {
        try
        {
            var reply = await NovoraConnection.SendAsync(action, value);
            ShowMessage(reply.Message);
        }
        catch (Exception ex) { ShowMessage(ex.Message); }
    }

    protected void ShowMessage(string message) => Toast.MakeText(this, message, ToastLength.Short)!.Show();

    protected void ConfigureNavigation()
    {
        FindViewById<Button>(Resource.Id.nav_home)!.Click += (_, _) => Navigate<HomeActivity>();
        FindViewById<Button>(Resource.Id.nav_engines)!.Click += (_, _) => Navigate<EnginesActivity>();
        FindViewById<Button>(Resource.Id.nav_files)!.Click += (_, _) => Navigate<FilesActivity>();
    }

    private void Navigate<T>() where T : Activity
    {
        if (this is T) return;
        StartActivity(new Intent(this, typeof(T)));
        Finish();
    }
}

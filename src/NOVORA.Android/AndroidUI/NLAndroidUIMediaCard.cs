using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.Media.Session;
using Android.OS;
using Android.Views;
using Android.Widget;
using Controller = Android.Media.Session.MediaController;
using Orientation = Android.Widget.Orientation;

namespace NOVORA.AndroidUI;

/// <summary>One selected app's media session. Callbacks supply state; the visible clock is extrapolated locally.</summary>
internal sealed class NLAndroidUIMediaCard : IDisposable
{
    private readonly Controller _controller;
    private readonly Handler _handler = new(Looper.MainLooper!);
    private readonly MediaCallback _callback;
    private readonly TextView _title, _time;
    private readonly ProgressBar _progress;
    private readonly Button _play, _previous, _next;
    private readonly Action _tick;
    private PlaybackState? _state;
    private long _duration;
    private bool _disposed;
    internal LinearLayout View { get; }
    internal NLAndroidUIMediaCard(Context context, Controller controller, string appName)
    {
        _controller = controller;
        View = new LinearLayout(context) { Orientation = Orientation.Vertical };
        int padding = NLAndroidUIVisual.Dp(context, 10);
        View.SetPadding(padding, padding, padding, padding);
        View.Background = NLAndroidUIVisual.Surface(context);
        TextView Text(string value, int size) { var t = new TextView(context) { Text = value, TextSize = size }; t.SetTextColor(Color.White); View.AddView(t); return t; }
        Text(appName, 12);
        _title = Text("Sin información de canción", 15);
        _progress = new ProgressBar(context, null, Android.Resource.Attribute.ProgressBarStyleHorizontal) { Max = 1000 };
        View.AddView(_progress, new LinearLayout.LayoutParams(-1, NLAndroidUIVisual.Dp(context, 16)));
        _time = Text("Duración no disponible", 12);
        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal }; View.AddView(row);
        Button Control(string label, Action action) {
            var b = new Button(context) { Text = label, ContentDescription = label }; NLAndroidUIVisual.Button(b); b.TextSize = 11;
            row.AddView(b, new LinearLayout.LayoutParams(0, -2, 1));
            b.Click += (_, _) => {
                if (_disposed || !NLAndroidUIMediaAccess.Enabled(context)) return;
                try { action(); } catch (Java.Lang.RuntimeException) { Toast.MakeText(context, "La app ya no permite este control.", ToastLength.Short)?.Show(); }
            }; return b;
        }
        _previous = Control("Anterior", () => _controller.GetTransportControls()?.SkipToPrevious());
        _play = Control("Reproducir", () => {
            if (_state?.State == PlaybackStateCode.Playing) _controller.GetTransportControls()?.Pause();
            else _controller.GetTransportControls()?.Play();
        });
        _next = Control("Siguiente", () => _controller.GetTransportControls()?.SkipToNext());
        _tick = UpdateClock;
        _callback = new MediaCallback(this);
        _controller.RegisterCallback(_callback, _handler);
        UpdateMetadata(_controller.Metadata); UpdateState(_controller.PlaybackState);
    }
    private void UpdateMetadata(MediaMetadata? metadata)
    {
        if (_disposed) return;
        string title = metadata?.GetString(MediaMetadata.MetadataKeyTitle) ?? metadata?.Description?.Title?.ToString() ?? "Sin información de canción";
        string? artist = metadata?.GetString(MediaMetadata.MetadataKeyArtist);
        _title.Text = string.IsNullOrWhiteSpace(artist) ? title : title + "\n" + artist;
        _duration = Math.Max(0, metadata?.GetLong(MediaMetadata.MetadataKeyDuration) ?? 0);
        UpdateClock();
    }
    private void UpdateState(PlaybackState? state)
    {
        if (_disposed) return;
        _state = state;
        long actions = state?.Actions ?? 0;
        bool playing = state?.State == PlaybackStateCode.Playing;
        _play.Text = playing ? "Pausar" : "Reproducir";
        _play.ContentDescription = _play.Text;
        _play.Enabled = (actions & (playing ? PlaybackState.ActionPause : PlaybackState.ActionPlay)) != 0;
        _previous.Enabled = (actions & PlaybackState.ActionSkipToPrevious) != 0;
        _next.Enabled = (actions & PlaybackState.ActionSkipToNext) != 0;
        UpdateClock();
    }
    private static string Time(long milliseconds) {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }
    private void UpdateClock()
    {
        if (_disposed) return;
        _handler.RemoveCallbacks(_tick);
        long position = _state?.Position ?? -1;
        bool playing = _state?.State == PlaybackStateCode.Playing;
        if (position >= 0 && playing && _state!.LastPositionUpdateTime > 0)
            position += (long)(Math.Max(0, SystemClock.ElapsedRealtime() - _state.LastPositionUpdateTime) * _state.PlaybackSpeed);
        if (_duration > 0 && position >= 0) {
            position = Math.Clamp(position, 0, _duration);
            _progress.Progress = (int)(position * 1000d / _duration);
            _time.Text = $"{Time(position)} / {Time(_duration)} · Restan {Time(_duration - position)}";
        } else { _progress.Progress = 0; _time.Text = _duration > 0 ? $"Duración {Time(_duration)} · Posición no disponible" : "Duración no disponible"; }
        // No media queries: only animate the clock while this card is attached and playing.
        if (playing && position >= 0 && _duration > 0) _handler.PostDelayed(_tick, 1000);
    }
    public void Dispose() {
        if (_disposed) return; _disposed = true;
        _handler.RemoveCallbacksAndMessages(null);
        try { _controller.UnregisterCallback(_callback); } catch (Java.Lang.RuntimeException) { }
        _callback.Dispose(); _handler.Dispose();
    }
    private sealed class MediaCallback(NLAndroidUIMediaCard owner) : Controller.Callback
    {
        public override void OnMetadataChanged(MediaMetadata? metadata) => owner.UpdateMetadata(metadata);
        public override void OnPlaybackStateChanged(PlaybackState? state) => owner.UpdateState(state);
        public override void OnSessionDestroyed() { owner.UpdateState(null); owner._title.Text = "Reproducción finalizada"; }
    }
}

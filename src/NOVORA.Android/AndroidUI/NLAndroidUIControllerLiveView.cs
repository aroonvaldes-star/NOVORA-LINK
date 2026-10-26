using Android.Content;
using Android.Graphics;
using Android.Views;
using NOVORA.Control;
using NOVORA.AndroidApp;

namespace NOVORA.AndroidUI;

/// <summary>Renders the real ExIn snapshot over the existing controller artwork.</summary>
internal sealed class NLAndroidUIControllerLiveView : View
{
    private readonly Bitmap? _controller;
    private readonly Paint _paint = new(PaintFlags.AntiAlias);
    private NLControlExIn? _snapshot;
    private long _sequence = -1;

    internal NLAndroidUIControllerLiveView(Context context) : base(context)
    {
        _controller = BitmapFactory.DecodeResource(Resources, Resource.Drawable.novora_controller_mock);
        ContentDescription = "Control físico con sticks, gatillos y botones en vivo";
        SetMinimumHeight(NLAndroidUIVisual.Dp(context, 220));
    }

    internal void UpdateVE(NLControlExIn? snapshot)
    {
        long sequence = snapshot?.Telemetry?.Sequence ?? -1;
        if (sequence >= 0 && sequence == _sequence) return;
        _sequence = sequence;
        _snapshot = snapshot;
        PostInvalidateOnAnimation();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        float w = Width, h = Height;
        if (_controller is not null)
        {
            float scale = Math.Min(w / _controller.Width, h / _controller.Height);
            float dw = _controller.Width * scale, dh = _controller.Height * scale;
            canvas.DrawBitmap(_controller, null, new RectF((w - dw) / 2, (h - dh) / 2, (w + dw) / 2, (h + dh) / 2), _paint);
        }
        if (_snapshot?.Detected != true) return;
        _paint.Color = Color.Rgb(13, 142, 157);
        _paint.SetStyle(Paint.Style.Fill);
        DrawStick(canvas, w * .355f, h * .46f, _snapshot.CorrectedLeftX, _snapshot.CorrectedLeftY);
        DrawStick(canvas, w * .61f, h * .59f, _snapshot.CorrectedRightX, _snapshot.CorrectedRightY);
        DrawTrigger(canvas, w * .16f, h * .08f, _snapshot.CorrectedLeftTrigger);
        DrawTrigger(canvas, w * .69f, h * .08f, _snapshot.CorrectedRightTrigger);
    }

    private void DrawStick(Canvas canvas, float cx, float cy, int x, int y)
    {
        float dx = Math.Clamp(x / 32767f, -1f, 1f) * Width * .035f;
        float dy = Math.Clamp(y / 32767f, -1f, 1f) * Height * .05f;
        canvas.DrawCircle(cx + dx, cy + dy, Math.Max(6, Width * .012f), _paint);
    }

    private void DrawTrigger(Canvas canvas, float x, float y, int value)
    {
        float width = Width * .15f;
        _paint.Color = Color.Argb(70, 13, 142, 157);
        canvas.DrawRoundRect(new RectF(x, y, x + width, y + Height * .025f), 8, 8, _paint);
        _paint.Color = Color.Rgb(13, 142, 157);
        canvas.DrawRoundRect(new RectF(x, y, x + width * Math.Clamp(value / 100f, 0, 1), y + Height * .025f), 8, 8, _paint);
    }
}

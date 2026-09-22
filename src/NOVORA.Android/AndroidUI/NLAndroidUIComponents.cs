using Android.Content;
using Android.Graphics;
using Android.Text;
using Android.Views;
using Android.Widget;

namespace NOVORA.AndroidUI;

internal enum NLAndroidUIStatusTone { Neutral, Accent, Success, Warning, Error }

internal static class NLAndroidUIComponents
{
    internal static View AppBar(Context context, string version, string connection, string engines,
        Action toggleTheme, Action openSettings)
    {
        NLAndroidUIPalette palette = NLAndroidUITheme.Current(context);
        var bar = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        bar.SetGravity(GravityFlags.CenterVertical);
        bar.SetPadding(Dp(context, 16), Dp(context, 8), Dp(context, 8), Dp(context, 8));
        bar.SetBackgroundColor(Color.ParseColor(palette.Surface));
        bar.SetMinimumHeight(Dp(context, 64));
        bar.AddView(NLAndroidUIVisual.Logo(context, 40));

        var identity = new LinearLayout(context) { Orientation = Orientation.Vertical };
        identity.SetPadding(Dp(context, 10), 0, Dp(context, 8), 0);
        identity.AddView(Text(context, "NOVORA-LINK", 17, palette.Text, true, 1));
        identity.AddView(Text(context, $"{version}  |  {connection}  |  {engines}", 11, palette.Muted, false, 2));
        bar.AddView(identity, new LinearLayout.LayoutParams(0, -2, 1));
        bar.AddView(IconButton(context, Android.Resource.Drawable.IcMenuDay, "Cambiar tema", toggleTheme));
        bar.AddView(IconButton(context, Android.Resource.Drawable.IcMenuPreferences, "Abrir ajustes", openSettings));
        return bar;
    }

    internal static TextView StatusChip(Context context, string text, NLAndroidUIStatusTone tone)
    {
        NLAndroidUIPalette palette = NLAndroidUITheme.Current(context);
        string color = Tone(palette, tone);
        TextView chip = Text(context, text, 12, color, true, 2);
        chip.Gravity = GravityFlags.Center;
        chip.SetPadding(Dp(context, 10), Dp(context, 5), Dp(context, 10), Dp(context, 5));
        chip.SetMinHeight(Dp(context, 32));
        chip.Background = NLAndroidUIVisual.Surface(context, palette.SurfaceRaised, color, 8);
        return chip;
    }

    internal static TextView SectionTitle(Context context, string text)
    {
        TextView title = Text(context, text, 15, NLAndroidUITheme.Current(context).Text, true, 2);
        title.SetPadding(0, Dp(context, 12), 0, Dp(context, 6));
        return title;
    }

    internal static View EngineRow(Context context, string name, string summary, string state,
        NLAndroidUIStatusTone tone, Action? action)
    {
        NLAndroidUIPalette palette = NLAndroidUITheme.Current(context);
        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(Dp(context, 12), Dp(context, 10), Dp(context, 10), Dp(context, 10));
        row.SetMinimumHeight(Dp(context, 72));
        row.Background = NLAndroidUIVisual.Surface(context, palette.Surface, palette.Border);
        var copy = new LinearLayout(context) { Orientation = Orientation.Vertical };
        copy.AddView(Text(context, name, 14, palette.Text, true, 1));
        copy.AddView(Text(context, summary, 12, palette.Muted, false, 3));
        row.AddView(copy, new LinearLayout.LayoutParams(0, -2, 1));
        row.AddView(StatusChip(context, state, tone));
        if (action is not null) {
            row.Clickable = true; row.Focusable = true; row.ContentDescription = $"{name}: {state}";
            row.Click += (_, _) => action();
        }
        return row;
    }

    internal static Button ActionTile(Context context, string title, string caption, int iconResource, Action action)
    {
        var button = new Button(context) { Text = $"{title}\n{caption}", ContentDescription = $"{title}. {caption}" };
        button.SetCompoundDrawablesWithIntrinsicBounds(0, iconResource, 0, 0);
        button.CompoundDrawablePadding = Dp(context, 6);
        button.SetMaxLines(3); button.Ellipsize = TextUtils.TruncateAt.End;
        button.Gravity = GravityFlags.Center; button.SetMinHeight(Dp(context, 112));
        NLAndroidUIVisual.Button(button);
        button.Click += (_, _) => action();
        return button;
    }

    internal static Button CommandButton(Context context, string text, bool primary, Func<Task> action)
    {
        var button = new Button(context) { Text = text, ContentDescription = text };
        button.SetMaxLines(2);
        button.Ellipsize = TextUtils.TruncateAt.End;
        NLAndroidUIVisual.Button(button, primary);
        button.Click += async (_, _) => {
            if (!button.Enabled) return;
            button.Enabled = false;
            try { await action(); }
            finally { button.Enabled = true; }
        };
        return button;
    }

    internal static LinearLayout SegmentedControl(Context context,
        IReadOnlyList<(string Label, bool Selected, bool Enabled, Func<Task> Action)> items)
    {
        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal, WeightSum = Math.Max(1, items.Count) };
        foreach ((string label, bool selected, bool enabled, Func<Task> action) in items) {
            Button button = CommandButton(context, label, selected, action);
            button.Enabled = enabled;
            row.AddView(button, new LinearLayout.LayoutParams(0, Dp(context, 48), 1));
        }
        return row;
    }

    internal static Button BottomTab(Context context, string label, int iconResource, bool selected, Action action)
    {
        NLAndroidUIPalette palette = NLAndroidUITheme.Current(context);
        var button = new Button(context) { Text = label, ContentDescription = label, Selected = selected };
        button.SetMaxLines(1);
        button.SetCompoundDrawablesWithIntrinsicBounds(0, iconResource, 0, 0);
        button.CompoundDrawablePadding = Dp(context, 2);
        button.TextSize = 11; button.Gravity = GravityFlags.Center;
        button.SetTextColor(Color.ParseColor(selected ? palette.Accent : palette.Muted));
        button.SetBackgroundColor(Color.Transparent);
        button.SetMinHeight(Dp(context, 56));
        button.Click += (_, _) => action();
        return button;
    }

    private static Button IconButton(Context context, int icon, string description, Action action)
    {
        var button = new Button(context) { ContentDescription = description };
        button.SetCompoundDrawablesWithIntrinsicBounds(0, icon, 0, 0);
        button.SetBackgroundColor(Color.Transparent);
        button.SetMinWidth(Dp(context, 48)); button.SetMinimumWidth(Dp(context, 48));
        button.SetMinHeight(Dp(context, 48)); button.SetMinimumHeight(Dp(context, 48));
        button.Click += (_, _) => action();
        return button;
    }

    private static TextView Text(Context context, string value, float size, string color, bool medium, int maxLines)
    {
        var text = new TextView(context) { Text = value, TextSize = size };
        text.SetMaxLines(maxLines);
        text.SetTextColor(Color.ParseColor(color));
        text.SetTypeface(Typeface.Create(medium ? "sans-serif-medium" : "sans-serif", TypefaceStyle.Normal), TypefaceStyle.Normal);
        text.Ellipsize = TextUtils.TruncateAt.End;
        return text;
    }

    private static string Tone(NLAndroidUIPalette palette, NLAndroidUIStatusTone tone) => tone switch {
        NLAndroidUIStatusTone.Accent => palette.Accent,
        NLAndroidUIStatusTone.Success => palette.Success,
        NLAndroidUIStatusTone.Warning => palette.Warning,
        NLAndroidUIStatusTone.Error => palette.Error,
        _ => palette.Muted
    };

    private static int Dp(Context context, int value) => NLAndroidUIVisual.Dp(context, value);
}

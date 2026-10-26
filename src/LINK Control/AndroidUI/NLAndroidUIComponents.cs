using Android.Content;
using Android.Graphics;
using Android.Text;
using Android.Views;
using Android.Widget;

namespace NOVORA.AndroidUI;

internal enum NLAndroidUIStatusTone { Neutral, Accent, Success, Warning, Error }

internal static class NLAndroidUIComponents
{
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
        var drawable = context.GetDrawable(iconResource)!.Mutate();
        drawable.SetBounds(0, 0, Dp(context, 20), Dp(context, 20));
        drawable.SetTint(Color.ParseColor(selected ? palette.Accent : palette.Muted));
        button.SetCompoundDrawables(null, drawable, null, null);
        button.CompoundDrawablePadding = Dp(context, 1);
        button.TextSize = 12; button.Gravity = GravityFlags.Center;
        button.SetPadding(Dp(context, 2), Dp(context, 4), Dp(context, 2), Dp(context, 2));
        button.SetTextColor(Color.ParseColor(selected ? palette.Accent : palette.Muted));
        button.SetBackgroundColor(Color.Transparent);
        button.SetMinHeight(Dp(context, 56)); button.SetMinimumHeight(Dp(context, 56));
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

internal sealed class NLAndroidUIOptionAdapter : ArrayAdapter<string>
{
    private readonly Context _context;

    internal NLAndroidUIOptionAdapter(Context context, string[] labels)
        : base(context, Android.Resource.Layout.SimpleSpinnerItem, labels)
    {
        _context = context;
    }

    public override View GetView(int position, View? convertView, ViewGroup parent) =>
        OptionView(position, convertView, parent, false);

    public override View GetDropDownView(int position, View? convertView, ViewGroup? parent) =>
        OptionView(position, convertView, parent, true);

    private View OptionView(int position, View? convertView, ViewGroup? parent, bool dropDown)
    {
        NLAndroidUIPalette palette = NLAndroidUITheme.Current(_context);
        TextView view = convertView as TextView ?? new TextView(_context);
        view.Text = GetItem(position) ?? string.Empty;
        view.TextSize = 14;
        view.Gravity = GravityFlags.CenterVertical;
        view.SetSingleLine(false);
        view.SetTextColor(Color.ParseColor(palette.Text));
        view.SetBackgroundColor(Color.ParseColor(dropDown ? palette.SurfaceRaised : palette.Surface));
        view.SetPadding(Dp(16), Dp(10), Dp(16), Dp(10));
        view.SetMinHeight(Dp(48));
        return view;
    }

    private int Dp(int value) => NLAndroidUIVisual.Dp(_context, value);
}

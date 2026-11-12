using NOVORA.Service;
namespace NOVORA.ViewModel;
public sealed partial class NLViewModelMain
{
    private string _theme = NLServiceTheme.Light;
    public string Theme
    {
        get => _theme;
        set => Set(
            ref _theme,
            string.Equals(
                value,
                NLServiceTheme.Light,
                StringComparison.OrdinalIgnoreCase)
                ? NLServiceTheme.Light
                : NLServiceTheme.Dark);
    }
    public IReadOnlyList<NLViewModelSettingOption<string>> ThemeOptions { get; } =
        new[]
        {
            new NLViewModelSettingOption<string>(NLServiceTheme.Dark, "Dark"),
            new NLViewModelSettingOption<string>(NLServiceTheme.Light, "Light")
        };
}

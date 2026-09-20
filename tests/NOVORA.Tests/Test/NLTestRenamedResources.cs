using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestRenamedResources
{
    [Fact]
    public void Renamed_application_resources_and_main_window_can_be_constructed()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = new NLApplicationApp();
                application.InitializeComponent();
                var window = new NLUIWindowMain();
                Assert.NotNull(window.FindName("MainActionButton"));
                var output = new NOVORA.Model.NLModelOutputProfile(1080, 1920, 120, 120, "25M", 1920);
                var buildOptions = typeof(NLUIWindowMain).GetMethod("BuildVisionOptionsVE",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                var options = Assert.IsType<NOVORA.VisionEngine.Server.VEServerOptions>(
                    buildOptions.Invoke(window, [output, false]));
                Assert.Equal(120d, options.MaxFps);
                Assert.Equal(25_000_000, options.VideoBitRate);
                Assert.Equal(1920, options.MaxSize);
                var combo = Assert.IsType<System.Windows.Controls.ComboBox>(window.FindName("NvidiaProfileCombo14"));
                Assert.Equal("NvidiaProfile", System.Windows.Data.BindingOperations.GetBinding(combo,
                    System.Windows.Controls.Primitives.Selector.SelectedValueProperty)?.Path.Path);
                var view = Assert.IsType<NOVORA.ViewModel.NLViewModelMain>(window.DataContext);
                foreach (var profile in Enum.GetValues<NOVORA.VisionEngine.Performance.VEPerformanceProfile>())
                {
                    var expected = NOVORA.Service.NLServiceVideoProfile.ApplyVE(view, profile);
                    var selectedOutput = new NOVORA.Model.NLModelOutputProfile(720, expected.MaxSize, 120,
                        view.TargetFps, view.Bitrate, view.MaxSize);
                    var effective = Assert.IsType<NOVORA.VisionEngine.Server.VEServerOptions>(
                        buildOptions.Invoke(window, [selectedOutput, false]));
                    Assert.Equal(expected.MaxFps, effective.MaxFps);
                    Assert.Equal(expected.RecommendedBitrate, effective.VideoBitRate);
                    Assert.Equal(expected.MaxSize, effective.MaxSize);
                    var argumentsMethod = effective.GetType().GetMethod("BuildArgumentsVE",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                    var arguments = Assert.IsAssignableFrom<System.Collections.Generic.IReadOnlyList<string>>(
                        argumentsMethod.Invoke(effective, [1, true]));
                    Assert.Contains($"video_bit_rate={expected.RecommendedBitrate}", arguments);
                    Assert.Contains($"max_size={expected.MaxSize}", arguments);
                    Assert.Contains("max_fps=" + expected.MaxFps.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), arguments);
                }
                var applyAdvanced = typeof(NLUIWindowMain).GetMethod("ApplyAdvancedVisionSettingsVE",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                Assert.Same(view, combo.DataContext);
                combo.GetBindingExpression(System.Windows.Controls.Primitives.Selector.SelectedValueProperty)!.UpdateTarget();
                foreach (var profile in Enum.GetValues<NOVORA.NVIDIA.NLNVIDIAProfile>())
                {
                    combo.SelectedValue = profile.ToString();
                    Assert.Equal(profile.ToString(), combo.SelectedValue);
                    System.Windows.Data.BindingOperations.GetBindingExpression(combo,
                        System.Windows.Controls.Primitives.Selector.SelectedValueProperty)!.UpdateSource();
                    Assert.Equal(profile.ToString(), view.NvidiaProfile);
                    applyAdvanced.Invoke(window, null);
                    var title = Assert.IsType<System.Windows.Controls.TextBlock>(window.FindName("NvidiaStatusTitle14"));
                    Assert.Contains(profile.ToString(), title.Text);
                }
                var networkButton = Assert.IsType<System.Windows.Controls.Button>(window.FindName("LinkEngineTestButton"));
                Assert.False(networkButton.IsEnabled);
                Assert.NotNull(application.Resources["NovoraBrandImageSource"]);
                Assert.NotNull(window.Resources["NvPrimaryButtonStyle"]);
                Assert.True(System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "Asset", "NLAssetPDI2T.mp4")));
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "La ventana no termino de construirse.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace NOVORA;

/// <summary>
/// Reproduce la introducciÃ³n oficial de NOVORA-LINK antes de abrir NLUIWindowMain.
/// La ventana usa las mismas dimensiones nominales que NLUIWindowMain: 920 x 560.
/// </summary>
public partial class NLUIWindowStartup : Window
{
    private static readonly TimeSpan PlaybackSafetyTimeout =
        TimeSpan.FromSeconds(10);

    private readonly TaskCompletionSource<bool> _mediaCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _played;

    public NLUIWindowStartup()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Reproduce Asset\NLAssetPDI2T.mp4 una sola vez y espera hasta que termine.
    /// </summary>
    public async Task PlayIntroAsync()
    {
        if (_played)
        {
            return;
        }

        _played =
            true;

        await WaitUntilLoadedAsync();

        string videoPath =
            Path.Combine(
                AppContext.BaseDirectory,
                "Asset",
                "NLAssetPDI2T.mp4");

        if (!File.Exists(videoPath))
        {
            throw new FileNotFoundException(
                "No se encontrÃ³ el video de inicio PDI2T.mp4 de NOVORA.",
                videoPath);
        }

        IntroMedia.Source =
            new Uri(
                videoPath,
                UriKind.Absolute);

        IntroMedia.Position =
            TimeSpan.Zero;

        IntroMedia.Play();

        Task completedTask =
            await Task.WhenAny(
                _mediaCompletion.Task,
                Task.Delay(PlaybackSafetyTimeout));

        if (!ReferenceEquals(
                completedTask,
                _mediaCompletion.Task))
        {
            IntroMedia.Stop();

            return;
        }

        await _mediaCompletion.Task;
    }

    private void IntroMedia_MediaEnded(
        object sender,
        RoutedEventArgs e)
    {
        _mediaCompletion.TrySetResult(
            true);
    }

    private void IntroMedia_MediaFailed(
        object sender,
        ExceptionRoutedEventArgs e)
    {
        _mediaCompletion.TrySetException(
            e.ErrorException
            ?? new InvalidOperationException(
                "No se pudo reproducir el video de inicio PDI2T.mp4 de NOVORA."));
    }

    private Task WaitUntilLoadedAsync()
    {
        if (IsLoaded)
        {
            return Task.CompletedTask;
        }

        var completion =
            new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        RoutedEventHandler? loadedHandler =
            null;

        loadedHandler =
            (_, _) =>
            {
                Loaded -=
                    loadedHandler;

                completion.TrySetResult(
                    true);
            };

        Loaded +=
            loadedHandler;

        return completion.Task;
    }

    protected override void OnClosed(
        EventArgs e)
    {
        try
        {
            IntroMedia.Stop();
        }
        catch
        {
        }

        base.OnClosed(e);
    }
}


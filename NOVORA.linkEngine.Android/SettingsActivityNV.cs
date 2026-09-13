using System.Text.Json;
using Android.App;
using Android.OS;
using Android.Views;
using Android.Widget;
using NOVORA.LinkEngine.Android.Remote;

using AColor = global::Android.Graphics.Color;
using AGradientDrawable = global::Android.Graphics.Drawables.GradientDrawable;
using ATypeface = global::Android.Graphics.Typeface;
using ATypefaceStyle = global::Android.Graphics.TypefaceStyle;

namespace NOVORA.LinkEngine.Android;

[Activity(
    Label = "NOVORA-LINK · Ajustes",
    Exported = false)]
public sealed class SettingsActivityNV :
    Activity
{
    private readonly ClientRemoteNV _remoteNV =
        new();

    private SettingsSnapshotRemoteNV? _snapshotNV;

    private TextView? _connectionNV;
    private TextView? _capabilitiesNV;
    private TextView? _messageNV;

    private Spinner? _themeNV;
    private Spinner? _modeNV;
    private Spinner? _monitorNV;
    private Spinner? _bitrateNV;
    private Spinner? _fpsNV;
    private Spinner? _resolutionNV;
    private Spinner? _audioNV;

    private Button? _saveNV;

    private bool _operationNV;

    protected override void OnCreate(
        Bundle? savedInstanceState)
    {
        base.OnCreate(
            savedInstanceState);

        BuildInterfaceNV();
    }

    protected override async void OnStart()
    {
        base.OnStart();

        await LoadSettingsNVAsync();
    }

    protected override async void OnStop()
    {
        try
        {
            await _remoteNV
                .DisconnectAsync();
        }
        catch
        {
        }

        base.OnStop();
    }

    private void BuildInterfaceNV()
    {
        var scroll =
            new ScrollView(this)
            {
                FillViewport =
                    true
            };

        var root =
            new LinearLayout(this)
            {
                Orientation =
                    Orientation.Vertical
            };

        root.SetPadding(
            DpNV(20),
            DpNV(26),
            DpNV(20),
            DpNV(30));

        root.SetBackgroundColor(
            AColor.ParseColor(
                "#0B1017"));

        TextView title =
            CreateTextNV(
                "AJUSTES",
                28f,
                true,
                "#F4F7FB");

        TextView subtitle =
            CreateTextNV(
                "Sincronizados con SettingsWindow de NOVORA PC",
                14f,
                false,
                "#8EA2B8");

        subtitle.SetPadding(
            0,
            DpNV(3),
            0,
            DpNV(18));

        root.AddView(
            title);

        root.AddView(
            subtitle);

        LinearLayout statusCard =
            CreateCardNV();

        _connectionNV =
            CreateTextNV(
                "●  CONECTANDO CON NOVORA...",
                15f,
                true,
                "#8EA2B8");

        _capabilitiesNV =
            CreateTextNV(
                "Resolución: -\nFPS: -\nHz: -",
                14f,
                false,
                "#D7E1EC");

        _capabilitiesNV.SetPadding(
            0,
            DpNV(12),
            0,
            0);

        statusCard.AddView(
            _connectionNV);

        statusCard.AddView(
            _capabilitiesNV);

        root.AddView(
            statusCard);

        LinearLayout settingsCard =
            CreateCardNV();

        settingsCard.LayoutParameters =
            new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
            {
                TopMargin =
                    DpNV(14)
            };

        _themeNV =
            AddSpinnerRowNV(
                settingsCard,
                "Tema de NOVORA PC",
                "Dark o Light");

        _modeNV =
            AddSpinnerRowNV(
                settingsCard,
                "Modo",
                "Ventana o pantalla completa");

        _monitorNV =
            AddSpinnerRowNV(
                settingsCard,
                "Monitor",
                "Pantalla donde abrir VisionEngine");

        _bitrateNV =
            AddSpinnerRowNV(
                settingsCard,
                "Calidad de transmisión",
                "Tasa de bits de video");

        _fpsNV =
            AddSpinnerRowNV(
                settingsCard,
                "FPS",
                "Hasta el máximo nativo");

        _resolutionNV =
            AddSpinnerRowNV(
                settingsCard,
                "Resolución",
                "Hasta la resolución nativa");

        _audioNV =
            AddSpinnerRowNV(
                settingsCard,
                "Audio",
                "Salida de audio de la computadora");

        root.AddView(
            settingsCard);

        _saveNV =
            CreateButtonNV(
                "GUARDAR EN NOVORA",
                true);

        _saveNV.LayoutParameters =
            new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                DpNV(54))
            {
                TopMargin =
                    DpNV(16)
            };

        _saveNV.Click +=
            async (_, _) =>
                await SaveSettingsNVAsync();

        root.AddView(
            _saveNV);

        Button cancel =
            CreateButtonNV(
                "VOLVER",
                false);

        cancel.LayoutParameters =
            new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                DpNV(48))
            {
                TopMargin =
                    DpNV(10)
            };

        cancel.Click +=
            (_, _) =>
                Finish();

        root.AddView(
            cancel);

        _messageNV =
            CreateTextNV(
                string.Empty,
                13f,
                false,
                "#8EA2B8");

        _messageNV.SetPadding(
            DpNV(4),
            DpNV(16),
            DpNV(4),
            0);

        root.AddView(
            _messageNV);

        scroll.AddView(
            root);

        SetContentView(
            scroll);
    }

    private Spinner AddSpinnerRowNV(
        LinearLayout parent,
        string title,
        string caption)
    {
        TextView titleView =
            CreateTextNV(
                title,
                15f,
                true,
                "#F4F7FB");

        TextView captionView =
            CreateTextNV(
                caption,
                12f,
                false,
                "#8EA2B8");

        captionView.SetPadding(
            0,
            DpNV(2),
            0,
            DpNV(7));

        var spinner =
            new Spinner(this);

        spinner.SetPadding(
            DpNV(10),
            0,
            DpNV(10),
            0);

        spinner.Background =
            CreateRoundedBackgroundNV(
                "#0E151E",
                "#2A3A4D",
                10);

        spinner.LayoutParameters =
            new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                DpNV(50))
            {
                BottomMargin =
                    DpNV(14)
            };

        parent.AddView(
            titleView);

        parent.AddView(
            captionView);

        parent.AddView(
            spinner);

        return spinner;
    }

    private async Task LoadSettingsNVAsync()
    {
        if (_operationNV)
        {
            return;
        }

        _operationNV =
            true;

        UpdateEnabledNV();

        try
        {
            bool connected =
                await _remoteNV
                    .ConnectAsync();

            if (!connected)
            {
                SetConnectedNV(
                    false);

                SetMessageNV(
                    "NOVORA PC no está disponible.");

                return;
            }

            SetConnectedNV(
                true);

            ResultRemoteNV result =
                await _remoteNV
                    .SendCommandAsync(
                        CommandRemoteNV.GetSettings);

            if (!result.Success)
            {
                SetMessageNV(
                    result.Message);

                return;
            }

            SettingsSnapshotRemoteNV? snapshot =
                JsonSerializer.Deserialize(
                    result.Message,
                    RemoteJsonContextNV
                        .Default
                        .SettingsSnapshotRemoteNV);

            if (snapshot is null)
            {
                SetMessageNV(
                    "NOVORA devolvió ajustes vacíos.");

                return;
            }

            _snapshotNV =
                snapshot;

            ApplySnapshotNV(
                snapshot);
        }
        catch (Exception ex)
        {
            SetMessageNV(
                ex.Message);
        }
        finally
        {
            _operationNV =
                false;

            UpdateEnabledNV();
        }
    }

    private void ApplySnapshotNV(
        SettingsSnapshotRemoteNV snapshot)
    {
        if (_capabilitiesNV is not null)
        {
            _capabilitiesNV.Text =
                $"Resolución nativa: {snapshot.DeviceNativeResolution}\n" +
                $"FPS máximos: {snapshot.DeviceMaxFps}\n" +
                $"Hz máximos: {snapshot.DeviceMaxRefreshRate}";
        }

        BindStringOptionsNV(
            _themeNV,
            snapshot.ThemeOptions,
            snapshot.Theme);

        BindStringOptionsNV(
            _modeNV,
            snapshot.PresentationModeOptions,
            snapshot.VideoPresentationMode);

        BindMonitorOptionsNV(
            _monitorNV,
            snapshot.Monitors,
            snapshot.SelectedMonitorDeviceName);

        BindStringOptionsNV(
            _bitrateNV,
            snapshot.BitrateOptions,
            snapshot.Bitrate);

        BindIntOptionsNV(
            _fpsNV,
            snapshot.FpsOptions,
            snapshot.TargetFps);

        BindIntOptionsNV(
            _resolutionNV,
            snapshot.ResolutionOptions,
            snapshot.MaxSize);

        BindStringOptionsNV(
            _audioNV,
            snapshot.AudioOutputOptions,
            snapshot.SelectedAudioOutput);

        SetMessageNV(
            string.IsNullOrWhiteSpace(
                snapshot.Notice)
                ? "Ajustes sincronizados con NOVORA PC."
                : snapshot.Notice);
    }

    private async Task SaveSettingsNVAsync()
    {
        SettingsSnapshotRemoteNV? snapshot =
            _snapshotNV;

        if (snapshot is null ||
            _operationNV)
        {
            return;
        }

        _operationNV =
            true;

        UpdateEnabledNV();

        try
        {
            var update =
                new SettingsUpdateRemoteNV
                {
                    Theme =
                        SelectedStringValueNV(
                            _themeNV,
                            snapshot.ThemeOptions,
                            snapshot.Theme),

                    VideoPresentationMode =
                        SelectedStringValueNV(
                            _modeNV,
                            snapshot.PresentationModeOptions,
                            snapshot.VideoPresentationMode),

                    SelectedMonitorDeviceName =
                        SelectedMonitorValueNV(
                            _monitorNV,
                            snapshot.Monitors,
                            snapshot.SelectedMonitorDeviceName),

                    Bitrate =
                        SelectedStringValueNV(
                            _bitrateNV,
                            snapshot.BitrateOptions,
                            snapshot.Bitrate),

                    TargetFps =
                        SelectedIntValueNV(
                            _fpsNV,
                            snapshot.FpsOptions,
                            snapshot.TargetFps),

                    MaxSize =
                        SelectedIntValueNV(
                            _resolutionNV,
                            snapshot.ResolutionOptions,
                            snapshot.MaxSize),

                    SelectedAudioOutput =
                        SelectedStringValueNV(
                            _audioNV,
                            snapshot.AudioOutputOptions,
                            snapshot.SelectedAudioOutput)
                };

            string payload =
                JsonSerializer.Serialize(
                    update,
                    RemoteJsonContextNV
                        .Default
                        .SettingsUpdateRemoteNV);

            ResultRemoteNV result =
                await _remoteNV
                    .SendCommandAsync(
                        CommandRemoteNV.SaveSettings,
                        payload);

            SetMessageNV(
                result.Message);

            if (result.Success)
            {
                Toast.MakeText(
                        this,
                        result.Message,
                        ToastLength.Long)
                    ?.Show();

                Finish();
            }
        }
        catch (Exception ex)
        {
            SetMessageNV(
                ex.Message);
        }
        finally
        {
            _operationNV =
                false;

            UpdateEnabledNV();
        }
    }

    private void SetConnectedNV(
        bool connected)
    {
        if (_connectionNV is null)
        {
            return;
        }

        _connectionNV.Text =
            connected
                ? "●  NOVORA CONECTADO"
                : "●  NOVORA NO DISPONIBLE";

        _connectionNV.SetTextColor(
            AColor.ParseColor(
                connected
                    ? "#59D38C"
                    : "#E86A75"));
    }

    private void UpdateEnabledNV()
    {
        bool enabled =
            !_operationNV &&
            _snapshotNV is not null;

        if (_saveNV is not null)
        {
            _saveNV.Enabled =
                enabled;
        }

        Spinner?[] spinners =
        {
            _themeNV,
            _modeNV,
            _monitorNV,
            _bitrateNV,
            _fpsNV,
            _resolutionNV,
            _audioNV
        };

        foreach (Spinner? spinner in
                 spinners)
        {
            if (spinner is not null)
            {
                spinner.Enabled =
                    enabled;
            }
        }
    }

    private void SetMessageNV(
        string message)
    {
        if (_messageNV is not null)
        {
            _messageNV.Text =
                message ??
                string.Empty;
        }
    }

    private void BindStringOptionsNV(
        Spinner? spinner,
        OptionStringRemoteNV[] options,
        string selectedValue)
    {
        if (spinner is null)
        {
            return;
        }

        string[] labels =
            options
                .Select(
                    option =>
                        option.Label)
                .ToArray();

        BindLabelsNV(
            spinner,
            labels);

        int index =
            Array.FindIndex(
                options,
                option =>
                    string.Equals(
                        option.Value,
                        selectedValue,
                        StringComparison.OrdinalIgnoreCase));

        spinner.SetSelection(
            Math.Max(
                0,
                index));
    }

    private void BindIntOptionsNV(
        Spinner? spinner,
        OptionIntRemoteNV[] options,
        int selectedValue)
    {
        if (spinner is null)
        {
            return;
        }

        string[] labels =
            options
                .Select(
                    option =>
                        option.Label)
                .ToArray();

        BindLabelsNV(
            spinner,
            labels);

        int index =
            Array.FindIndex(
                options,
                option =>
                    option.Value ==
                    selectedValue);

        spinner.SetSelection(
            Math.Max(
                0,
                index));
    }

    private void BindMonitorOptionsNV(
        Spinner? spinner,
        MonitorRemoteNV[] options,
        string selectedDeviceName)
    {
        if (spinner is null)
        {
            return;
        }

        string[] labels =
            options
                .Select(
                    option =>
                        option.Label)
                .ToArray();

        BindLabelsNV(
            spinner,
            labels);

        int index =
            Array.FindIndex(
                options,
                option =>
                    string.Equals(
                        option.DeviceName,
                        selectedDeviceName,
                        StringComparison.OrdinalIgnoreCase));

        spinner.SetSelection(
            Math.Max(
                0,
                index));
    }

    private void BindLabelsNV(
        Spinner spinner,
        string[] labels)
    {
        string[] values =
            labels.Length == 0
                ? new[]
                {
                    "No disponible"
                }
                : labels;

        var adapter =
            new ArrayAdapter<string>(
                this,
                global::Android.Resource.Layout.SimpleSpinnerItem,
                values);

        adapter.SetDropDownViewResource(
            global::Android.Resource.Layout.SimpleSpinnerDropDownItem);

        spinner.Adapter =
            adapter;
    }

    private static string SelectedStringValueNV(
        Spinner? spinner,
        OptionStringRemoteNV[] options,
        string fallback)
    {
        int index =
            spinner?.SelectedItemPosition ??
            -1;

        return
            index >= 0 &&
            index < options.Length
                ? options[index].Value
                : fallback;
    }

    private static int SelectedIntValueNV(
        Spinner? spinner,
        OptionIntRemoteNV[] options,
        int fallback)
    {
        int index =
            spinner?.SelectedItemPosition ??
            -1;

        return
            index >= 0 &&
            index < options.Length
                ? options[index].Value
                : fallback;
    }

    private static string SelectedMonitorValueNV(
        Spinner? spinner,
        MonitorRemoteNV[] options,
        string fallback)
    {
        int index =
            spinner?.SelectedItemPosition ??
            -1;

        return
            index >= 0 &&
            index < options.Length
                ? options[index].DeviceName
                : fallback;
    }

    private LinearLayout CreateCardNV()
    {
        var card =
            new LinearLayout(this)
            {
                Orientation =
                    Orientation.Vertical
            };

        card.SetPadding(
            DpNV(17),
            DpNV(16),
            DpNV(17),
            DpNV(16));

        card.Background =
            CreateRoundedBackgroundNV(
                "#121A24",
                "#233142",
                16);

        return card;
    }

    private Button CreateButtonNV(
        string text,
        bool primary)
    {
        var button =
            new Button(this)
            {
                Text =
                    text,

                TextSize =
                    15f,

                Gravity =
                    GravityFlags.Center
            };

        button.SetAllCaps(
            false);

        button.SetTypeface(
            ATypeface.DefaultBold,
            ATypefaceStyle.Bold);

        button.SetTextColor(
            AColor.ParseColor(
                "#F4F7FB"));

        button.Background =
            CreateRoundedBackgroundNV(
                primary
                    ? "#2F7EF7"
                    : "#121A24",

                primary
                    ? "#4394FF"
                    : "#233142",

                14);

        return button;
    }

    private TextView CreateTextNV(
        string text,
        float size,
        bool bold,
        string color)
    {
        var view =
            new TextView(this)
            {
                Text =
                    text,

                TextSize =
                    size
            };

        view.SetTextColor(
            AColor.ParseColor(
                color));

        if (bold)
        {
            view.SetTypeface(
                ATypeface.DefaultBold,
                ATypefaceStyle.Bold);
        }

        return view;
    }

    private AGradientDrawable CreateRoundedBackgroundNV(
        string fill,
        string stroke,
        int radiusDp)
    {
        var drawable =
            new AGradientDrawable();

        drawable.SetColor(
            AColor.ParseColor(
                fill));

        drawable.SetStroke(
            DpNV(1),
            AColor.ParseColor(
                stroke));

        drawable.SetCornerRadius(
            DpNV(radiusDp));

        return drawable;
    }

    private int DpNV(
        int value)
    {
        float density =
            Resources
                ?.DisplayMetrics
                ?.Density ??
            1f;

        return
            (int)(
                value *
                density);
    }

    protected override void OnDestroy()
    {
        try
        {
            _remoteNV
                .DisposeAsync()
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
        }

        base.OnDestroy();
    }
}

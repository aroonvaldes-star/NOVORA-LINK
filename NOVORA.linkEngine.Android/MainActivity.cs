using Android.App;
using Android.Content;
using Android.Database;
using Android.Net;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NOVORA.LinkEngine.Android.Discovery;
using NOVORA.LinkEngine.Android.LinkEngine;
using NOVORA.LinkEngine.Android.Remote;

using AColor = global::Android.Graphics.Color;
using AGradientDrawable = global::Android.Graphics.Drawables.GradientDrawable;
using ATypeface = global::Android.Graphics.Typeface;
using ATypefaceStyle = global::Android.Graphics.TypefaceStyle;

namespace NOVORA.LinkEngine.Android;

[Activity(
    Label = "NOVORA-LINK",
    MainLauncher = true,
    Exported = true)]
[IntentFilter(
    new[]
    {
        Intent.ActionSend
    },
    Categories = new[]
    {
        Intent.CategoryDefault
    },
    DataMimeType = "*/*")]
[IntentFilter(
    new[]
    {
        Intent.ActionSendMultiple
    },
    Categories = new[]
    {
        Intent.CategoryDefault
    },
    DataMimeType = "*/*")]
public sealed class MainActivity :
    Activity
{
    private const int VpnRequestCodeLE =
        27185;

    private readonly ClientRemoteNV _remoteNV =
        new();

    private TextView? _pcStateNV;
    private TextView? _visionStateNV;
    private TextView? _linkStateNV;
    private TextView? _messageNV;

    private Button? _visionButtonNV;
    private Button? _linkButtonNV;
    private Button? _discoverButtonNV;
    private TextView? _discoveredPcNV;
    private TextView? _settingsButtonNV;

    private bool _visionRunningNV;
    private bool _linkRunningNV;
    private bool _operationRunningNV;
    private bool _linkFailureCleanupRunningNV;

    protected override void OnCreate(
        Bundle? savedInstanceState)
    {
        base.OnCreate(
            savedInstanceState);

        EnsureNotificationPermissionNV();

        DiscoverySignalReceiverNV.StartDiscoveryServiceNV(
            this,
            DiscoveryServiceNV.ActionStartNV);

        BuildInterfaceNV();

        _remoteNV.ConnectionChangedNV +=
            Remote_ConnectionChangedNV;

        VpnNetworkLE.StatusChangedLE +=
            Vpn_StatusChangedLE;

        UpdateLinkFromLocalNV(
            VpnNetworkLE.StatusLE);

        _ =
            HandleShareIntentNVAsync(
                Intent);
    }

    protected override void OnNewIntent(
        Intent? intent)
    {
        base.OnNewIntent(
            intent);

        _ =
            HandleShareIntentNVAsync(
                intent);
    }

    protected override async void OnStart()
    {
        base.OnStart();

        await RefreshPcStatusNVAsync();
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

    private void EnsureNotificationPermissionNV()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return;
        }

        if (CheckSelfPermission(
                global::Android.Manifest.Permission.PostNotifications) ==
            global::Android.Content.PM.Permission.Granted)
        {
            return;
        }

        RequestPermissions(
            new[]
            {
                global::Android.Manifest.Permission.PostNotifications
            },
            27186);
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
            DpNV(22),
            DpNV(28),
            DpNV(22),
            DpNV(28));

        root.SetBackgroundColor(
            AColor.ParseColor(
                "#0B1017"));

        var header =
            new LinearLayout(this)
            {
                Orientation =
                    Orientation.Horizontal
            };

        header.SetGravity(
            GravityFlags.CenterVertical);

        TextView brand =
            CreateTextNV(
                "NOVORA-LINK",
                30f,
                true,
                "#F4F7FB");

        brand.LayoutParameters =
            new LinearLayout.LayoutParams(
                0,
                ViewGroup.LayoutParams.WrapContent,
                1f);

        _settingsButtonNV =
            CreateTextNV(
                "⚙",
                25f,
                true,
                "#DDE7F2");

        _settingsButtonNV.Gravity =
            GravityFlags.Center;

        _settingsButtonNV.ContentDescription =
            "AJUSTES";

        _settingsButtonNV.Background =
            CreateRoundedBackgroundNV(
                "#121A24",
                "#233142",
                14);

        _settingsButtonNV.LayoutParameters =
            new LinearLayout.LayoutParams(
                DpNV(48),
                DpNV(48));

        _settingsButtonNV.Click +=
            (_, _) =>
                OpenSettingsNV();

        header.AddView(
            brand);

        header.AddView(
            _settingsButtonNV);

        root.AddView(
            header);

        TextView subtitle =
            CreateTextNV(
                "Android ↔ PC",
                14f,
                false,
                "#8EA2B8");

        subtitle.SetPadding(
            0,
            DpNV(2),
            0,
            DpNV(20));

        root.AddView(
            subtitle);

        LinearLayout pcCard =
            CreateCardNV();

        TextView pcTitle =
            CreateTextNV(
                "NOVORA PC",
                13f,
                true,
                "#8EA2B8");

        _pcStateNV =
            CreateTextNV(
                "●  Buscando NOVORA...",
                18f,
                true,
                "#F4F7FB");

        _pcStateNV.SetPadding(
            0,
            DpNV(8),
            0,
            0);

        pcCard.AddView(
            pcTitle);

        pcCard.AddView(
            _pcStateNV);

        root.AddView(
            pcCard);

        _discoverButtonNV =
            CreateButtonNV(
                "BUSCAR NOVORA CERCA",
                false);

        _discoverButtonNV.LayoutParameters =
            new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                DpNV(48))
            {
                TopMargin = DpNV(10)
            };

        _discoverButtonNV.Click +=
            async (_, _) =>
                await FindNovoraOnLanNVAsync();

        root.AddView(
            _discoverButtonNV);

        _discoveredPcNV =
            CreateTextNV(
                "Busca NOVORA en tu misma red Wi-Fi o Ethernet.",
                12f,
                false,
                "#8EA2B8");

        _discoveredPcNV.SetPadding(
            DpNV(4),
            DpNV(8),
            DpNV(4),
            0);

        root.AddView(
            _discoveredPcNV);

        LinearLayout enginesCard =
            CreateCardNV();

        enginesCard.LayoutParameters =
            new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
            {
                TopMargin =
                    DpNV(14)
            };

        TextView enginesTitle =
            CreateTextNV(
                "MOTORES",
                13f,
                true,
                "#8EA2B8");

        _visionStateNV =
            CreateTextNV(
                "VisionEngine     DETENIDO",
                17f,
                true,
                "#F4F7FB");

        _linkStateNV =
            CreateTextNV(
                "LinkEngine       DETENIDO",
                17f,
                true,
                "#F4F7FB");

        _visionStateNV.SetPadding(
            0,
            DpNV(12),
            0,
            DpNV(8));

        enginesCard.AddView(
            enginesTitle);

        enginesCard.AddView(
            _visionStateNV);

        enginesCard.AddView(
            _linkStateNV);

        root.AddView(
            enginesCard);

        _visionButtonNV =
            CreateButtonNV(
                "▶  INICIAR TRANSMISIÓN",
                true);

        _visionButtonNV.LayoutParameters =
            new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                DpNV(56))
            {
                TopMargin =
                    DpNV(20)
            };

        _visionButtonNV.Click +=
            async (_, _) =>
                await ToggleVisionNVAsync();

        root.AddView(
            _visionButtonNV);

        _linkButtonNV =
            CreateButtonNV(
                "INTERNET DESDE PC",
                false);

        _linkButtonNV.LayoutParameters =
            new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                DpNV(52))
            {
                TopMargin =
                    DpNV(10)
            };

        _linkButtonNV.Click +=
            (_, _) =>
                ToggleLinkNV();

        root.AddView(
            _linkButtonNV);

        Button folderButton =
            CreateButtonNV(
                "ABRIR CARPETA NOVORA",
                false);

        folderButton.LayoutParameters =
            new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                DpNV(48))
            {
                TopMargin =
                    DpNV(10)
            };

        folderButton.Click +=
            (_, _) =>
                OpenNovoraFolderNV();

        root.AddView(
            folderButton);

        Button stopAll =
            CreateButtonNV(
                "DETENER TODO",
                false);

        stopAll.LayoutParameters =
            new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                DpNV(48))
            {
                TopMargin =
                    DpNV(10)
            };

        stopAll.Click +=
            async (_, _) =>
                await StopAllNVAsync();

        root.AddView(
            stopAll);

        _messageNV =
            CreateTextNV(
                "NOVORA listo. Los ajustes de transmisión están en ⚙.",
                13f,
                false,
                "#8EA2B8");

        _messageNV.SetPadding(
            DpNV(4),
            DpNV(18),
            DpNV(4),
            0);

        root.AddView(
            _messageNV);

        scroll.AddView(
            root);

        SetContentView(
            scroll);
    }

    private void OpenSettingsNV()
    {
        if (_operationRunningNV)
        {
            return;
        }

        var intent =
            new Intent(
                this,
                typeof(SettingsActivityNV));

        StartActivity(
            intent);
    }

    private async Task FindNovoraOnLanNVAsync()
    {
        if (_operationRunningNV)
        {
            return;
        }

        _operationRunningNV = true;
        UpdateButtonsNV();
        SetMessageNV("Buscando NOVORA en la red local...");

        try
        {
            IReadOnlyList<DiscoveredPcNV> pcsNV =
                await LanDiscoveryClientNV.FindAsync();

            if (pcsNV.Count == 0)
            {
                if (_discoveredPcNV is not null)
                {
                    _discoveredPcNV.Text =
                        "No se encontró NOVORA. Verifica que ambos estén en la misma red y que NOVORA PC esté abierto.";
                }

                return;
            }

            string listNV = string.Join(
                "\n",
                pcsNV.Select(pc => $"{pc.NameNV} · {pc.AddressNV} · requiere vinculación"));

            if (_discoveredPcNV is not null)
            {
                _discoveredPcNV.Text = listNV;
            }

            SetMessageNV("NOVORA detectado. La vinculación segura se habilitará antes de permitir ajustes o control por LAN.");
        }
        catch (Exception ex)
        {
            SetMessageNV($"No se pudo buscar NOVORA: {ex.Message}");
        }
        finally
        {
            _operationRunningNV = false;
            UpdateButtonsNV();
        }
    }

    private async Task HandleShareIntentNVAsync(
        Intent? intent)
    {
        if (intent is null ||
            (intent.Action != Intent.ActionSend &&
             intent.Action != Intent.ActionSendMultiple))
        {
            return;
        }

        IReadOnlyList<global::Android.Net.Uri> uris =
            ExtractSharedUrisNV(
                intent);

        if (uris.Count == 0)
        {
            SetMessageNV(
                "Android no entregó archivos para compartir.");

            return;
        }

        ShareFileItemRemoteNV[] files =
            uris
                .Select(
                    ReadSharedFileItemNV)
                .ToArray();

        var offer =
            new ShareFilesOfferRemoteNV
            {
                OfferId =
                    Guid.NewGuid()
                        .ToString("N"),

                CreatedAtUtc =
                    DateTimeOffset.UtcNow,

                Files =
                    files
            };

        string payload =
            JsonSerializer.Serialize(
                offer,
                RemoteJsonContextNV
                    .Default
                    .ShareFilesOfferRemoteNV);

        SetMessageNV(
            files.Length == 1
                ? $"Enviando {files[0].Name} a NOVORA PC..."
                : $"Enviando {files.Length} archivos a NOVORA PC...");

        ResultRemoteNV result =
            await _remoteNV
                .SendCommandAsync(
                    CommandRemoteNV.ShareFiles,
                    payload);

        SetMessageNV(
            result.Message);

        if (result.Success)
        {
            foreach (ShareFileItemRemoteNV file in files)
            {
                try
                {
                    await SendSharedFileBytesNVAsync(
                        file);

                    await SendSharedFileStatusNVAsync(
                        file.TransferId,
                        success: true,
                        "Archivo enviado.");
                }
                catch (Exception ex)
                {
                    string message =
                        $"No se pudo enviar {file.Name}: {ex.Message}";

                    SetMessageNV(
                        message);

                    await SendSharedFileStatusNVAsync(
                        file.TransferId,
                        success: false,
                        ex.Message);
                }
            }

            await RefreshPcStatusNVAsync();
        }
    }

    private async Task SendSharedFileStatusNVAsync(
        string transferId,
        bool success,
        string message)
    {
        if (string.IsNullOrWhiteSpace(
                transferId))
        {
            return;
        }

        var status =
            new ShareFileStatusRemoteNV
            {
                TransferId =
                    transferId,

                Success =
                    success,

                Message =
                    message
            };

        string payload =
            JsonSerializer.Serialize(
                status,
                RemoteJsonContextNV
                    .Default
                    .ShareFileStatusRemoteNV);

        try
        {
            _ =
                await _remoteNV
                    .SendCommandAsync(
                        CommandRemoteNV.ShareFileStatus,
                        payload);
        }
        catch
        {
        }
    }

    private async Task SendSharedFileBytesNVAsync(
        ShareFileItemRemoteNV file)
    {
        if (string.IsNullOrWhiteSpace(
                file.AndroidUri))
        {
            SetMessageNV(
                $"No se pudo leer {file.Name}: Uri vacío.");

            return;
        }

        global::Android.Net.Uri? uri =
            global::Android.Net.Uri.Parse(
                file.AndroidUri);

        if (uri is null)
        {
            SetMessageNV(
                $"No se pudo leer {file.Name}: Uri inválido.");

            return;
        }

        await using Stream? input =
            ContentResolver?.OpenInputStream(
                uri);

        if (input is null)
        {
            SetMessageNV(
                $"Android no pudo abrir {file.Name}.");

            return;
        }

        SetMessageNV(
            $"Transfiriendo {file.Name} a NOVORA PC...");

        using var client =
            new TcpClient
            {
                NoDelay =
                    true
            };

        await client
            .ConnectAsync(
                ClientRemoteNV.DefaultHostNV,
                ProtocolRemoteNV.DefaultFileTransferPortNV);

        await using NetworkStream stream =
            client.GetStream();

        var header =
            new ShareFileTransferHeaderRemoteNV
            {
                TransferId =
                    file.TransferId,

                Name =
                    file.Name,

                MimeType =
                    file.MimeType,

                SizeBytes =
                    file.SizeBytes
            };

        string headerJson =
            JsonSerializer.Serialize(
                header,
                RemoteJsonContextNV
                    .Default
                    .ShareFileTransferHeaderRemoteNV);

        byte[] headerBytes =
            Encoding.UTF8.GetBytes(
                headerJson);

        byte[] lengthBytes =
            new byte[sizeof(int)];

        BinaryPrimitives.WriteInt32BigEndian(
            lengthBytes,
            headerBytes.Length);

        await stream
            .WriteAsync(
                lengthBytes);

        await stream
            .WriteAsync(
                headerBytes);

        byte[] buffer =
            new byte[128 * 1024];

        int read;

        while ((read = await input
                   .ReadAsync(
                       buffer)) > 0)
        {
            await stream
                .WriteAsync(
                    buffer.AsMemory(
                        0,
                        read));
        }

        await stream
            .FlushAsync();

        SetMessageNV(
            $"Archivo enviado: {file.Name}");
    }

    private IReadOnlyList<global::Android.Net.Uri> ExtractSharedUrisNV(
        Intent intent)
    {
        var uris =
            new List<global::Android.Net.Uri>();

        ClipData? clipData =
            intent.ClipData;

        if (clipData is not null)
        {
            for (int index = 0; index < clipData.ItemCount; index++)
            {
                global::Android.Net.Uri? uri =
                    clipData
                        .GetItemAt(index)
                        ?.Uri;

                if (uri is not null)
                {
                    uris.Add(
                        uri);
                }
            }
        }

        if (uris.Count == 0)
        {
#pragma warning disable CS0618
            if (intent.GetParcelableExtra(Intent.ExtraStream) is global::Android.Net.Uri streamUri)
#pragma warning restore CS0618
            {
                uris.Add(
                    streamUri);
            }
        }

        if (uris.Count == 0 &&
            intent.Data is not null)
        {
            uris.Add(
                intent.Data);
        }

        return uris;
    }

    private ShareFileItemRemoteNV ReadSharedFileItemNV(
        global::Android.Net.Uri uri)
    {
        string name =
            uri.LastPathSegment ??
            "archivo-android";

        long size =
            -1;

        try
        {
            using ICursor? cursor =
                ContentResolver?.Query(
                    uri,
                    null,
                    null,
                    null,
                    null);

            if (cursor is not null &&
                cursor.MoveToFirst())
            {
                int nameIndex =
                    cursor.GetColumnIndex(
                        IOpenableColumns.DisplayName);

                if (nameIndex >= 0)
                {
                    string? displayName =
                        cursor.GetString(
                            nameIndex);

                    if (!string.IsNullOrWhiteSpace(
                            displayName))
                    {
                        name =
                            displayName;
                    }
                }

                int sizeIndex =
                    cursor.GetColumnIndex(
                        IOpenableColumns.Size);

                if (sizeIndex >= 0 &&
                    !cursor.IsNull(
                        sizeIndex))
                {
                    size =
                        cursor.GetLong(
                            sizeIndex);
                }
            }
        }
        catch
        {
        }

        string mime =
            ContentResolver?.GetType(
                uri) ??
            "application/octet-stream";

        return new ShareFileItemRemoteNV
        {
            TransferId =
                Guid.NewGuid()
                    .ToString("N"),

            Name =
                name,

            MimeType =
                mime,

            SizeBytes =
                size,

            AndroidUri =
                uri.ToString() ??
                string.Empty
        };
    }

    private void OpenNovoraFolderNV()
    {
        string rootPath =
            global::Android.OS.Environment
                .ExternalStorageDirectory
                ?.AbsolutePath ??
            "/sdcard";

        var novoraFolder =
            new global::Java.IO.File(
                rootPath,
                "NOVORA");

        try
        {
            if (!novoraFolder.Exists())
            {
                _ =
                    novoraFolder.Mkdirs();
            }
        }
        catch
        {
        }

        global::Android.Net.Uri? documentUri =
            global::Android.Provider.DocumentsContract.BuildDocumentUri(
                "com.android.externalstorage.documents",
                "primary:NOVORA");

        if (documentUri is not null)
        {
            var viewIntent =
                new Intent(
                    Intent.ActionView);

            viewIntent.SetDataAndType(
                documentUri,
                "vnd.android.document/directory");

            viewIntent.AddFlags(
                ActivityFlags.GrantReadUriPermission |
                ActivityFlags.GrantWriteUriPermission);

            try
            {
                StartActivity(
                    viewIntent);

                SetMessageNV(
                    "Abriendo carpeta NOVORA en almacenamiento interno.");

                return;
            }
            catch (ActivityNotFoundException)
            {
            }
        }

        global::Android.Net.Uri? treeUri =
            global::Android.Provider.DocumentsContract.BuildTreeDocumentUri(
                "com.android.externalstorage.documents",
                "primary:");

        var treeIntent =
            new Intent(
                Intent.ActionOpenDocumentTree);

        if (treeUri is not null)
        {
            treeIntent.PutExtra(
                global::Android.Provider.DocumentsContract.ExtraInitialUri,
                treeUri);
        }

        try
        {
            StartActivity(
                treeIntent);

            SetMessageNV(
                "Selecciona NOVORA dentro de almacenamiento interno.");
        }
        catch (ActivityNotFoundException)
        {
            SetMessageNV(
                "Android no encontró una app para abrir la carpeta NOVORA.");
        }
    }

    private async Task ToggleVisionNVAsync()
    {
        if (_operationRunningNV)
        {
            return;
        }

        _operationRunningNV =
            true;

        UpdateButtonsNV();

        try
        {
            CommandRemoteNV command =
                _visionRunningNV
                    ? CommandRemoteNV.StopVision
                    : CommandRemoteNV.StartVision;

            SetMessageNV(
                _visionRunningNV
                    ? "Deteniendo transmisión..."
                    : "Iniciando VisionEngine en NOVORA PC...");

            ResultRemoteNV result =
                await _remoteNV
                    .SendCommandAsync(
                        command);

            SetMessageNV(
                result.Message);

            if (result.Success)
            {
                await RefreshPcStatusNVAsync();
            }
        }
        finally
        {
            _operationRunningNV =
                false;

            UpdateButtonsNV();
        }
    }

    private void ToggleLinkNV()
    {
        if (_operationRunningNV)
        {
            return;
        }

        if (_linkRunningNV)
        {
            _ =
                StopLinkNVAsync();

            return;
        }

        Intent? vpnIntent =
            VpnService.Prepare(
                this);

        if (vpnIntent is null)
        {
            _ =
                StartLinkNVAsync();

            return;
        }

        SetMessageNV(
            "Android necesita autorización VPN una sola vez.");

#pragma warning disable CS0618
        StartActivityForResult(
            vpnIntent,
            VpnRequestCodeLE);
#pragma warning restore CS0618
    }

    protected override void OnActivityResult(
        int requestCode,
        Result resultCode,
        Intent? data)
    {
        base.OnActivityResult(
            requestCode,
            resultCode,
            data);

        if (requestCode !=
            VpnRequestCodeLE)
        {
            return;
        }

        if (resultCode ==
            Result.Ok)
        {
            _ =
                StartLinkNVAsync();

            return;
        }

        SetMessageNV(
            "Permiso VPN cancelado. LinkEngine no se inició.");
    }

    private async Task StartLinkNVAsync()
    {
        if (_operationRunningNV)
        {
            return;
        }

        _operationRunningNV =
            true;

        UpdateButtonsNV();

        try
        {
            bool connected =
                await _remoteNV
                    .ConnectAsync();

            if (!connected)
            {
                SetPcConnectedNV(
                    false);

                SetMessageNV(
                    "NOVORA PC no está disponible.");

                return;
            }

            SetMessageNV(
                "Preparando CONTROL de LinkEngine en NOVORA PC...");

            ResultRemoteNV prepared =
                await _remoteNV
                    .SendCommandAsync(
                        CommandRemoteNV.PrepareLink);

            if (!prepared.Success)
            {
                SetMessageNV(
                    prepared.Message);

                return;
            }

            SetMessageNV(
                "CONTROL preparado. Activando VPN Android...");

            VpnNetworkLE.StartLE(
                this);
        }
        catch (Exception ex)
        {
            SetMessageNV(
                $"No se pudo iniciar LinkEngine: {ex.Message}");

            await CleanupRemoteLinkAfterLocalFailureNVAsync();
        }
        finally
        {
            _operationRunningNV =
                false;

            UpdateButtonsNV();
        }
    }

    private async Task StopLinkNVAsync()
    {
        if (_operationRunningNV)
        {
            return;
        }

        _operationRunningNV =
            true;

        UpdateButtonsNV();

        try
        {
            SetMessageNV(
                "Deteniendo Internet desde PC...");

            /*
             * Primero se libera la ruta VPN local.
             * Así nunca queda 0.0.0.0/0 apuntando a un Data Plane
             * que Windows ya haya detenido.
             */
            VpnNetworkLE.StopLE(
                this);

            ResultRemoteNV result =
                await _remoteNV
                    .SendCommandAsync(
                        CommandRemoteNV.StopLink);

            SetMessageNV(
                result.Message);

            await RefreshPcStatusNVAsync();
        }
        finally
        {
            _operationRunningNV =
                false;

            UpdateButtonsNV();
        }
    }

    private async Task StopAllNVAsync()
    {
        if (_operationRunningNV)
        {
            return;
        }

        _operationRunningNV =
            true;

        UpdateButtonsNV();

        try
        {
            SetMessageNV(
                "Deteniendo VisionEngine y LinkEngine...");

            VpnNetworkLE.StopLE(
                this);

            ResultRemoteNV result =
                await _remoteNV
                    .SendCommandAsync(
                        CommandRemoteNV.StopAll);

            SetMessageNV(
                result.Message);

            await RefreshPcStatusNVAsync();
        }
        finally
        {
            _operationRunningNV =
                false;

            UpdateButtonsNV();
        }
    }

    private async Task RefreshPcStatusNVAsync()
    {
        bool connected =
            await _remoteNV
                .ConnectAsync();

        if (!connected)
        {
            SetPcConnectedNV(
                false);

            return;
        }

        SetPcConnectedNV(
            true);

        ResultRemoteNV status =
            await _remoteNV
                .SendCommandAsync(
                    CommandRemoteNV.Status);

        if (!status.Success)
        {
            SetMessageNV(
                status.Message);

            return;
        }

        ApplyRemoteStatusNV(
            status.Message);
    }

    private void ApplyRemoteStatusNV(
        string status)
    {
        string[] parts =
            status.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries);

        foreach (string part in parts)
        {
            int separator =
                part.IndexOf('=');

            if (separator <= 0)
            {
                continue;
            }

            string key =
                part[..separator]
                    .Trim();

            string value =
                part[(separator + 1)..]
                    .Trim();

            if (string.Equals(
                    key,
                    "VISION",
                    StringComparison.OrdinalIgnoreCase))
            {
                _visionRunningNV =
                    string.Equals(
                        value,
                        "ON",
                        StringComparison.OrdinalIgnoreCase);
            }

            if (string.Equals(
                    key,
                    "LINK",
                    StringComparison.OrdinalIgnoreCase))
            {
                StatusNetworkLE local =
                    VpnNetworkLE.StatusLE;

                bool localOwnsState =
                    local.State is not
                        StateNetworkLE.Stopped and not
                        StateNetworkLE.Failed;

                if (!localOwnsState)
                {
                    _linkRunningNV =
                        string.Equals(
                            value,
                            "ON",
                            StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        UpdateEngineStateNV();
        UpdateButtonsNV();
    }

    private void Remote_ConnectionChangedNV(
        object? sender,
        bool connected)
    {
        RunOnUiThread(
            () =>
                SetPcConnectedNV(
                    connected));
    }

    private void SetPcConnectedNV(
        bool connected)
    {
        if (_pcStateNV is null)
        {
            return;
        }

        _pcStateNV.Text =
            connected
                ? "●  NOVORA CONECTADO"
                : "●  NOVORA NO DISPONIBLE";

        _pcStateNV.SetTextColor(
            AColor.ParseColor(
                connected
                    ? "#59D38C"
                    : "#E86A75"));

        if (!connected)
        {
            SetMessageNV(
                "Abre o minimiza NOVORA en Windows y selecciona este Android.");
        }
    }

    private void Vpn_StatusChangedLE(
        object? sender,
        StatusNetworkLE status)
    {
        RunOnUiThread(
            () =>
                UpdateLinkFromLocalNV(
                    status));
    }

    private void UpdateLinkFromLocalNV(
        StatusNetworkLE status)
    {
        _linkRunningNV =
            status.State is not
                StateNetworkLE.Stopped and not
                StateNetworkLE.Failed;

        UpdateEngineStateNV();
        UpdateButtonsNV();

        switch (status.State)
        {
            case StateNetworkLE.Online:

                SetMessageNV(
                    "LinkEngine ONLINE. Android usa Internet desde la PC.");

                break;

            case StateNetworkLE.Failed:

                SetMessageNV(
                    status.Message);

                _ =
                    CleanupRemoteLinkAfterLocalFailureNVAsync();

                break;

            default:

                if (!string.IsNullOrWhiteSpace(
                        status.Message))
                {
                    SetMessageNV(
                        status.Message);
                }

                break;
        }
    }

    private async Task CleanupRemoteLinkAfterLocalFailureNVAsync()
    {
        if (_linkFailureCleanupRunningNV)
        {
            return;
        }

        _linkFailureCleanupRunningNV =
            true;

        try
        {
            await _remoteNV
                .SendCommandAsync(
                    CommandRemoteNV.StopLink);
        }
        catch
        {
        }
        finally
        {
            _linkFailureCleanupRunningNV =
                false;
        }
    }

    private void UpdateEngineStateNV()
    {
        if (_visionStateNV is not null)
        {
            _visionStateNV.Text =
                _visionRunningNV
                    ? "VisionEngine     ACTIVO"
                    : "VisionEngine     DETENIDO";

            _visionStateNV.SetTextColor(
                AColor.ParseColor(
                    _visionRunningNV
                        ? "#59D38C"
                        : "#F4F7FB"));
        }

        if (_linkStateNV is not null)
        {
            _linkStateNV.Text =
                _linkRunningNV
                    ? "LinkEngine       ACTIVO"
                    : "LinkEngine       DETENIDO";

            _linkStateNV.SetTextColor(
                AColor.ParseColor(
                    _linkRunningNV
                        ? "#59D38C"
                        : "#F4F7FB"));
        }
    }

    private void UpdateButtonsNV()
    {
        if (_visionButtonNV is not null)
        {
            _visionButtonNV.Enabled =
                !_operationRunningNV;

            _visionButtonNV.Text =
                _visionRunningNV
                    ? "■  DETENER TRANSMISIÓN"
                    : "▶  INICIAR TRANSMISIÓN";
        }

        if (_linkButtonNV is not null)
        {
            _linkButtonNV.Enabled =
                !_operationRunningNV;

            _linkButtonNV.Text =
                _linkRunningNV
                    ? "DETENER INTERNET PC"
                    : "INTERNET DESDE PC";
        }

        if (_discoverButtonNV is not null)
        {
            _discoverButtonNV.Enabled =
                !_operationRunningNV;
        }

        if (_settingsButtonNV is not null)
        {
            _settingsButtonNV.Enabled =
                !_operationRunningNV;
        }
    }

    private void SetMessageNV(
        string message)
    {
        if (_messageNV is null)
        {
            return;
        }

        _messageNV.Text =
            string.IsNullOrWhiteSpace(
                message)
                ? "NOVORA listo."
                : message;
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
            DpNV(18),
            DpNV(16),
            DpNV(18),
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
        _remoteNV.ConnectionChangedNV -=
            Remote_ConnectionChangedNV;

        VpnNetworkLE.StatusChangedLE -=
            Vpn_StatusChangedLE;

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

using NOVORA.VisionEngine.Control;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace NOVORA.VisionEngine.Exchange;

/// <summary>
/// Clipboard PC &lt;-&gt; Android a través del control channel.
///
/// También expone ReadVE() para convertir el contenido actual del
/// portapapeles de Windows en solicitudes de Exchange:
///
/// - archivos / carpetas / APK -> VEExchangeRequest.FromPathVE()
/// - texto / URL               -> VEExchangeRequest.FromTextVE()
/// </summary>
public sealed class VEExchangeClipboard : IDisposable
{
    private readonly VEControlManager _controlVE;
    private readonly Func<bool>? _canUseClipboardVE;

    private readonly ConcurrentDictionary<
        ulong,
        TaskCompletionSource<bool>> _acksVE =
        new();

    private TaskCompletionSource<string>? _nextClipboardVE;

    private long _sequenceVE;

    private bool _disposedVE;

    public VEExchangeClipboard(
        VEControlManager control,
        Func<bool>? canUseClipboard = null)
    {
        _controlVE =
            control
            ?? throw new ArgumentNullException(
                nameof(control));

        _canUseClipboardVE = canUseClipboard;

        _controlVE.ClipboardChangedVE +=
            Control_ClipboardChangedVE;

        _controlVE.ClipboardAcknowledgedVE +=
            Control_ClipboardAcknowledgedVE;
    }

    public event EventHandler<string>? ClipboardChangedVE;

    /// <summary>
    /// Lee el portapapeles local de Windows y lo transforma en
    /// solicitudes de intercambio compatibles con VEExchangePaste.
    ///
    /// Si hay archivos/copias del Explorador, se priorizan sobre el texto
    /// auxiliar que Windows también puede colocar en el clipboard.
    /// </summary>
    internal static IReadOnlyList<VEExchangeRequest> ReadVE()
    {
        System.Windows.Threading.Dispatcher? dispatcher =
            System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is not null &&
            !dispatcher.CheckAccess())
        {
            return dispatcher.Invoke(
                ReadClipboardRequestsCoreVE);
        }

        return ReadClipboardRequestsCoreVE();
    }

    private static IReadOnlyList<VEExchangeRequest>
        ReadClipboardRequestsCoreVE()
    {
        try
        {
            if (System.Windows.Clipboard.ContainsFileDropList())
            {
                StringCollection fileDropList =
                    System.Windows.Clipboard.GetFileDropList();

                List<VEExchangeRequest> fileRequests =
                    new(fileDropList.Count);

                foreach (string? path in fileDropList)
                {
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    try
                    {
                        fileRequests.Add(
                            VEExchangeRequest.FromPathVE(
                                path));
                    }
                    catch
                    {
                        // Un elemento inválido no debe impedir que los
                        // demás archivos válidos del clipboard continúen.
                    }
                }

                return fileRequests;
            }

            if (System.Windows.Clipboard.ContainsText())
            {
                string text =
                    System.Windows.Clipboard.GetText(
                        System.Windows.TextDataFormat.UnicodeText)
                    ?? string.Empty;

                if (string.IsNullOrWhiteSpace(text))
                {
                    return Array.Empty<VEExchangeRequest>();
                }

                return
                [
                    VEExchangeRequest.FromTextVE(
                        text)
                ];
            }
        }
        catch
        {
            // El portapapeles de Windows puede estar bloqueado
            // temporalmente por otra aplicación.
        }

        return Array.Empty<VEExchangeRequest>();
    }

    public async Task<string> GetTextAsync(
        VEControlCopy copyKey = VEControlCopy.None,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();
        EnsurePrivacyVE();

        TaskCompletionSource<string> tcs =
            new(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);

        Interlocked.Exchange(
            ref _nextClipboardVE,
            tcs);

        await _controlVE
            .SendAsync(
                VEControlMessage.GetClipboardVE(
                    copyKey),
                cancellationToken)
            .ConfigureAwait(false);

        TimeSpan effective =
            timeout
            ?? TimeSpan.FromSeconds(3);

        try
        {
            return await tcs.Task
                .WaitAsync(
                    effective,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            Interlocked.CompareExchange(
                ref _nextClipboardVE,
                null,
                tcs);
        }
    }

    /// <summary>
    /// Alias semantico utilizado por IntegrationVE.
    ///
    /// No crea un segundo camino de clipboard.
    /// Delega al GET_CLIPBOARD event-driven existente.
    /// </summary>
    /// <summary>
    /// Sobrecarga usada por VEIntegrationClipboard.
    ///
    /// Evita interpretar CancellationToken como VEControlCopy y
    /// conserva el mismo GET_CLIPBOARD event-driven del núcleo.
    /// </summary>
    public Task<string> RequestCopyAsyncVE(
        CancellationToken cancellationToken)
        => GetTextAsync(
            VEControlCopy.None,
            timeout: null,
            cancellationToken: cancellationToken);
    public Task<string> RequestCopyAsyncVE(
        VEControlCopy copyKey = VEControlCopy.None,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => GetTextAsync(
            copyKey,
            timeout,
            cancellationToken);
    public async Task<ulong> SetTextAsync(
        string text,
        bool paste = false,
        bool waitForAck = true,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();
        EnsurePrivacyVE();

        ArgumentNullException.ThrowIfNull(
            text);

        ulong sequence =
            unchecked(
                (ulong)Interlocked.Increment(
                    ref _sequenceVE));

        TaskCompletionSource<bool>? tcs =
            null;

        if (waitForAck)
        {
            tcs =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions
                        .RunContinuationsAsynchronously);

            if (!_acksVE.TryAdd(
                    sequence,
                    tcs))
            {
                throw new InvalidOperationException(
                    "Secuencia de clipboard VisionEngine duplicada.");
            }
        }

        try
        {
            await _controlVE
                .SendAsync(
                    VEControlMessage.SetClipboardVE(
                        sequence,
                        text,
                        paste),
                    cancellationToken)
                .ConfigureAwait(false);

            if (tcs is not null)
            {
                await tcs.Task
                    .WaitAsync(
                        timeout
                        ?? TimeSpan.FromSeconds(3),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return sequence;
        }
        finally
        {
            if (tcs is not null)
            {
                _acksVE.TryRemove(
                    sequence,
                    out _);
            }
        }
    }

    private void Control_ClipboardChangedVE(
        object? sender,
        string text)
    {
        if (_canUseClipboardVE is not null && !_canUseClipboardVE())
            return;

        Interlocked.Exchange(
                ref _nextClipboardVE,
                null)
            ?.TrySetResult(
                text);

        ClipboardChangedVE?.Invoke(
            this,
            text);
    }

    private void Control_ClipboardAcknowledgedVE(
        object? sender,
        ulong sequence)
    {
        if (_acksVE.TryGetValue(
                sequence,
                out TaskCompletionSource<bool>? tcs))
        {
            tcs.TrySetResult(
                true);
        }
    }

    private void EnsurePrivacyVE()
    {
        if (_canUseClipboardVE is not null && !_canUseClipboardVE())
            throw new InvalidOperationException("PrivacyVE bloqueó el portapapeles.");
    }

    private void ThrowIfDisposedVE()
    {
        ObjectDisposedException.ThrowIf(
            _disposedVE,
            this);
    }

    public void Dispose()
    {
        if (_disposedVE)
        {
            return;
        }

        _disposedVE =
            true;

        _controlVE.ClipboardChangedVE -=
            Control_ClipboardChangedVE;

        _controlVE.ClipboardAcknowledgedVE -=
            Control_ClipboardAcknowledgedVE;

        _nextClipboardVE?.TrySetCanceled();

        foreach (TaskCompletionSource<bool> tcs
                 in _acksVE.Values)
        {
            tcs.TrySetCanceled();
        }

        _acksVE.Clear();
    }
}

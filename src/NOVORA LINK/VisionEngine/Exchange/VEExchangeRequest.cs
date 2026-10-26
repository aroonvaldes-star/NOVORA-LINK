using System;
using System.IO;

namespace NOVORA.VisionEngine.Exchange;

internal enum VEExchangeKind
{
    Unknown = 0,
    File = 1,
    Directory = 2,
    Apk = 3,
    Text = 4,
    Url = 5
}

/// <summary>
/// Solicitud genérica de ExchangeVE.
///
/// Permite que Drag & Drop y Clipboard compartan
/// la misma clasificación de contenido.
/// </summary>
internal sealed class VEExchangeRequest
{
    public VEExchangeRequest(
        VEExchangeKind kind,
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            value);

        KindVE =
            kind;

        ValueVE =
            value;
    }

    public VEExchangeKind KindVE
    {
        get;
    }

    public string ValueVE
    {
        get;
    }

    public static VEExchangeRequest FromPathVE(
        string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            path);

        string fullPath =
            Path.GetFullPath(
                path);

        if (Directory.Exists(fullPath))
        {
            return
                new VEExchangeRequest(
                    VEExchangeKind.Directory,
                    fullPath);
        }

        if (!File.Exists(fullPath))
        {
            return
                new VEExchangeRequest(
                    VEExchangeKind.Unknown,
                    fullPath);
        }

        string extension =
            Path.GetExtension(
                fullPath);

        if (
            extension.Equals(
                ".apk",
                StringComparison.OrdinalIgnoreCase))
        {
            return
                new VEExchangeRequest(
                    VEExchangeKind.Apk,
                    fullPath);
        }

        return
            new VEExchangeRequest(
                VEExchangeKind.File,
                fullPath);
    }

    public static VEExchangeRequest FromTextVE(
        string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            text);

        string normalized =
            text.Trim();

        if (
            Uri.TryCreate(
                normalized,
                UriKind.Absolute,
                out Uri? uri) &&
            (
                uri.Scheme.Equals(
                    Uri.UriSchemeHttp,
                    StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase)
            ))
        {
            return
                new VEExchangeRequest(
                    VEExchangeKind.Url,
                    normalized);
        }

        return
            new VEExchangeRequest(
                VEExchangeKind.Text,
                normalized);
    }

    public override string ToString()
    {
        return
            $"{KindVE}: {ValueVE}";
    }
}

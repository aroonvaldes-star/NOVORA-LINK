using System;
using System.IO;

namespace NOVORA.VisionEngine.Exchange;

internal enum KindExchangeVE
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
internal sealed class RequestExchangeVE
{
    public RequestExchangeVE(
        KindExchangeVE kind,
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            value);

        KindVE =
            kind;

        ValueVE =
            value;
    }

    public KindExchangeVE KindVE
    {
        get;
    }

    public string ValueVE
    {
        get;
    }

    public static RequestExchangeVE FromPathVE(
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
                new RequestExchangeVE(
                    KindExchangeVE.Directory,
                    fullPath);
        }

        if (!File.Exists(fullPath))
        {
            return
                new RequestExchangeVE(
                    KindExchangeVE.Unknown,
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
                new RequestExchangeVE(
                    KindExchangeVE.Apk,
                    fullPath);
        }

        return
            new RequestExchangeVE(
                KindExchangeVE.File,
                fullPath);
    }

    public static RequestExchangeVE FromTextVE(
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
                new RequestExchangeVE(
                    KindExchangeVE.Url,
                    normalized);
        }

        return
            new RequestExchangeVE(
                KindExchangeVE.Text,
                normalized);
    }

    public override string ToString()
    {
        return
            $"{KindVE}: {ValueVE}";
    }
}

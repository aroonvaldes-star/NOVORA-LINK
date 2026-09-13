using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NOVORA.VisionEngine.Exchange;

/// <summary>
/// Pega texto de Windows en el elemento enfocado de Android.
///
/// Se utiliza como transporte inicial para Ctrl+V.
///
/// No crea polling ni consulta el portapapeles Android.
/// </summary>
internal static class TextExchangeVE
{
    public static async Task SendAsync(
        string serial,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            serial);

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        string encoded =
            EncodeInputTextVE(
                text);

        ResultAdbExchangeVE result =
            await AdbExchangeVE
                .RunAsync(
                    serial,
                    cancellationToken,
                    "shell",
                    "input",
                    "text",
                    encoded)
                .ConfigureAwait(false);

        if (!result.SuccessVE)
        {
            string error =
                string.IsNullOrWhiteSpace(
                    result.ErrorVE)
                    ? result.OutputVE
                    : result.ErrorVE;

            throw new InvalidOperationException(
                $"Android rechazó el texto: {error.Trim()}");
        }
    }

    private static string EncodeInputTextVE(
        string text)
    {
        StringBuilder builder =
            new(
                text.Length + 16);

        foreach (char character in text)
        {
            switch (character)
            {
                case ' ':
                    builder.Append(
                        "%s");
                    break;

                case '\r':
                    break;

                case '\n':
                    builder.Append(
                        "%s");
                    break;

                /*
                 * input text no es un canal Unicode perfecto.
                 *
                 * Dejamos los caracteres restantes intactos;
                 * el siguiente paso futuro será enviarlos por
                 * el protocolo de control propio de VE.
                 */
                default:
                    builder.Append(
                        character);
                    break;
            }
        }

        return
            builder.ToString();
    }
}

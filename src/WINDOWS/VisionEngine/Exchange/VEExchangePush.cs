namespace NOVORA.VisionEngine.Exchange;

/// <summary>Stage ADB transfers before making the final file visible; never replace a destination.</summary>
internal static class VEExchangePush
{
    public static async Task<string> SendAsync(string serial, string localPath, string destination, bool directory, CancellationToken ct)
    {
        string parent = destination[..destination.LastIndexOf('/')];
        string temporary = parent + "/.novora-" + Guid.NewGuid().ToString("N") + ".partial";
        string Quote(string value) => VEExchangePath.QuoteShellArgumentVE(value);
        try
        {
            var pushed = await VEExchangeADB.RunAsync(serial, ct, "push", localPath, temporary).ConfigureAwait(false);
            if (!pushed.SuccessVE) throw new IOException(string.IsNullOrWhiteSpace(pushed.ErrorVE) ? pushed.OutputVE : pushed.ErrorVE);
            for (int index = 0; index < 100; index++)
            {
                string candidate = index == 0 ? destination : parent + "/" +
                    Path.GetFileNameWithoutExtension(destination) + "-" + Guid.NewGuid().ToString("N") + Path.GetExtension(destination);
                // -n is the final no-overwrite guard; the existence check prevents mv treating a directory as a container.
                string command = $"if [ -e {Quote(candidate)} ]; then echo NOVORA_EXISTS; else mv -n {Quote(temporary)} {Quote(candidate)} && test ! -e {Quote(temporary)} && echo NOVORA_COMMITTED; fi";
                var committed = await VEExchangeADB.RunAsync(serial, ct, "shell", command).ConfigureAwait(false);
                if (committed.SuccessVE && committed.OutputVE.Contains("NOVORA_COMMITTED", StringComparison.Ordinal)) return candidate;
                if (!committed.OutputVE.Contains("NOVORA_EXISTS", StringComparison.Ordinal)) throw new IOException("No se pudo finalizar el archivo en Android.");
            }
            throw new IOException("No se pudo reservar un nombre disponible en Android.");
        }
        finally
        {
            // The only deletion target is our generated staging path, never the caller's destination.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await VEExchangeADB.RunAsync(serial, cleanup.Token, "shell", (directory ? "rm -rf " : "rm -f ") + Quote(temporary)).ConfigureAwait(false); }
            catch (Exception) { /* Device removal may prevent cleaning an identifiable .partial file. */ }
        }
    }
}

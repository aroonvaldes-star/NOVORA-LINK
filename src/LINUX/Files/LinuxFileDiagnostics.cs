namespace NOVORA.Linux.Files;

/// <summary>
/// Full cause of FILES failures. The engine wraps the real reason ("FILES channel failed." around the
/// original exception), so the UI and the log must read the inner chain, not only the outer message.
/// </summary>
internal static class LinuxFileDiagnostics
{
    private static readonly object Gate = new();

    public static string LogPath
    {
        get
        {
            string root = Environment.GetEnvironmentVariable("XDG_STATE_HOME") is { Length: > 0 } xdg
                ? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
            return Path.Combine(root, "novora-link-linux", "files.log");
        }
    }

    /// <summary>Outer message followed by each inner cause, e.g. "FILES channel failed. → FILES response has a stale id".</summary>
    public static string Describe(Exception ex)
    {
        var parts = new List<string>();
        for (Exception? current = ex; current is not null && parts.Count < 5; current = current.InnerException)
        {
            string text = string.IsNullOrWhiteSpace(current.Message) ? current.GetType().Name : current.Message.Trim();
            if (parts.Count == 0 || parts[^1] != text) parts.Add(text);
        }
        return string.Join(" → ", parts);
    }

    /// <summary>Appends one line per failure with timestamp, context, exception types and the origin frame.</summary>
    public static void Record(string context, Exception ex)
    {
        try
        {
            var lines = new List<string> { $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} [{context}] {Describe(ex)}" };
            for (Exception? current = ex; current is not null; current = current.InnerException)
            {
                string origin = current.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? "";
                lines.Add($"    {current.GetType().FullName}: {current.Message.Trim()} {origin}");
            }
            string path = LogPath;
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllLines(path, lines);
            }
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
        {
            // Diagnostics must never break a transfer; the UI still shows the cause.
        }
    }
}

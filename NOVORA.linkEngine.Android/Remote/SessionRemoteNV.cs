namespace NOVORA.LinkEngine.Android.Remote;

// Credencial temporal del proceso. Nunca se escribe en preferencias ni archivos.
internal static class SessionRemoteNV
{
    private static string _tokenNV = string.Empty;
    internal static string TokenNV => Volatile.Read(ref _tokenNV);

    internal static bool SetTokenNV(string? token)
    {
        if (token is null || token.Length != 64 || !token.All(Uri.IsHexDigit)) return false;
        Interlocked.Exchange(ref _tokenNV, token);
        return true;
    }
}

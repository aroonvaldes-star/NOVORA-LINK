namespace NOVORA.Control;

/// <summary>
/// Frozen contract for the legacy JSON FILES v1 path. New code must use
/// <see cref="NLControlFileProtocolV2"/>; these constants only keep old peers
/// interoperable until <see cref="NLFileProtocolPolicy.LegacyRemovalOn"/>.
/// </summary>
public static class NLControlFileProtocolV1
{
    public const int Version = 1;
    public const long MaxFileBytes = 2L * 1024 * 1024 * 1024;
    public const int ChunkBytes = 24 * 1024;
    public const int MaxChunkHexChars = ChunkBytes * 2;
    public const int MaxJsonMessageBytes = 51_000;

    public const string BeginAction = "file.begin";
    public const string ChunkAction = "file.chunk";
    public const string EndAction = "file.end";
    public const string CancelAction = "file.cancel";

    public static bool IsFileAction(string action) =>
        action is BeginAction or ChunkAction or EndAction or CancelAction;
}

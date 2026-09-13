namespace NOVORA.Remote;

public sealed record ShareFileItemRemoteNV
{
    public string TransferId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string MimeType { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    public string AndroidUri { get; init; } = string.Empty;
}

public sealed record ShareFilesOfferRemoteNV
{
    public string OfferId { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public ShareFileItemRemoteNV[] Files { get; init; } =
        Array.Empty<ShareFileItemRemoteNV>();
}

public sealed record ShareFileTransferHeaderRemoteNV
{
    public string TransferId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string MimeType { get; init; } = string.Empty;

    public long SizeBytes { get; init; }
}

public sealed record ShareFileStatusRemoteNV
{
    public string TransferId { get; init; } = string.Empty;

    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;
}

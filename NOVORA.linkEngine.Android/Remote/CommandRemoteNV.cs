namespace NOVORA.LinkEngine.Android.Remote;

public enum CommandRemoteNV
{
    Status = 0,
    StartVision = 1,
    StopVision = 2,
    StartLink = 3,
    StopLink = 4,
    StartAll = 5,
    StopAll = 6,
    PrepareLink = 7,
    GetSettings = 8,
    SaveSettings = 9,
    ShareFiles = 10,
    ShareFileStatus = 11
}

public sealed record ResultRemoteNV(
    bool Success,
    string Message)
{
    public static ResultRemoteNV OkNV(
        string message)
    {
        return new ResultRemoteNV(
            true,
            message ?? string.Empty);
    }

    public static ResultRemoteNV FailNV(
        string message)
    {
        return new ResultRemoteNV(
            false,
            message ?? string.Empty);
    }
}

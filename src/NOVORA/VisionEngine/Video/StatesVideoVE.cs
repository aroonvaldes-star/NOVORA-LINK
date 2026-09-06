namespace NOVORA.VisionEngine.Video;

/// <summary>
/// Estado del pipeline de video headless.
/// </summary>
public enum StatesVideoVE
{
    Stopped = 0,
    Opening = 1,
    Streaming = 2,
    EndOfStream = 3,
    Failed = 4
}

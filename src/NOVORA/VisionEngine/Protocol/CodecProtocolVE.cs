namespace NOVORA.VisionEngine.Protocol;

/// <summary>
/// Identificadores de codec enviados por el servidor de scrcpy 4.1 en big-endian.
/// Los valores corresponden a las etiquetas ASCII del protocolo multimedia.
/// </summary>
public enum CodecProtocolVE : uint
{
    Disabled = 0,
    ConfigurationError = 1,

    H264 = 0x68323634,
    H265 = 0x68323635,
    Av1 = 0x00617631,
    Vp8 = 0x00767038,
    Vp9 = 0x00767039,

    Opus = 0x6f707573,
    Aac = 0x00616163,
    Flac = 0x666c6163,
    Raw = 0x00726177
}

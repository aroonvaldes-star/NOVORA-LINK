using System.Text.Json.Serialization;

namespace NOVORA.LinkEngine.Android.Remote;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(SettingsSnapshotRemoteNV))]
[JsonSerializable(typeof(SettingsUpdateRemoteNV))]
[JsonSerializable(typeof(ShareFilesOfferRemoteNV))]
[JsonSerializable(typeof(ShareFileTransferHeaderRemoteNV))]
[JsonSerializable(typeof(ShareFileStatusRemoteNV))]
internal sealed partial class RemoteJsonContextNV :
    JsonSerializerContext
{
}

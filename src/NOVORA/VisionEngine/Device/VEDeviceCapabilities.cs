namespace NOVORA.VisionEngine.Device;

/// <summary>
/// Capacidades que VisionEngine puede obtener de Android sin iniciar captura.
/// No supone soporte de codecs que el dispositivo no haya reportado.
/// </summary>
public sealed record VEDeviceCapabilities(
    string Manufacturer,
    string Model,
    int? AndroidSdk,
    string Abi,
    int? PhysicalWidth,
    int? PhysicalHeight)
{
    public string DisplayNameVE
    {
        get
        {
            string manufacturer = Manufacturer.Trim();
            string model = Model.Trim();

            if (string.IsNullOrWhiteSpace(manufacturer))
            {
                return string.IsNullOrWhiteSpace(model)
                    ? "Android"
                    : model;
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                return manufacturer;
            }

            return $"{manufacturer} {model}";
        }
    }
}

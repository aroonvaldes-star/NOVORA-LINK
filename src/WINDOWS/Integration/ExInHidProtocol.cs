using System.Text.Json;

namespace NOVORA.Integration;

/// <summary>Commands for Android's built-in `hid -` shell tool.</summary>
public static class ExInHidProtocol
{
    public const int MaxDescriptorBytes = 4096;
    public const int MaxReportBytes = 1024;

    public static string Register(ushort id, string name, ushort vendorId, ushort productId,
        byte[] descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.Length is 0 or > MaxDescriptorBytes)
            throw new ArgumentOutOfRangeException(nameof(descriptor));
        return JsonSerializer.Serialize(new
        {
            id,
            command = "register",
            name,
            vid = vendorId,
            pid = productId,
            bus = "usb",
            descriptor = descriptor.Select(value => (int)value).ToArray()
        });
    }

    public static string Report(ushort id, byte[] report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Length is 0 or > MaxReportBytes)
            throw new ArgumentOutOfRangeException(nameof(report));
        // Sent ~125 times per second per pad: built directly instead of through reflection
        // serialization of an anonymous object. Same JSON as JsonSerializer produced.
        var json = new System.Text.StringBuilder(32 + report.Length * 4);
        json.Append("{\"id\":").Append(id).Append(",\"command\":\"report\",\"report\":[");
        for (int index = 0; index < report.Length; index++)
        {
            if (index > 0) json.Append(',');
            json.Append(report[index]);
        }
        return json.Append("]}").ToString();
    }
}

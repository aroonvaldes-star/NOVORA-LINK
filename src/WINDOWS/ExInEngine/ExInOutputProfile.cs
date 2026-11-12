namespace NOVORA.ExInEngine;

public sealed record ExInOutputProfile(
    ExInInputMode Mode,
    ExInDevice Device,
    byte[] Descriptor,
    byte[] NeutralReport,
    Func<ExInState, byte[]> BuildReportVE)
{
    private const string XboxNameVE = "Microsoft X-Box 360 Pad";
    private const string PointerNameVE = "NOVORA Controller Pointer";

    public static ExInOutputProfile CreateVE(
        ExInInputMode mode,
        ExInControllerIdentity identity,
        uint instanceId,
        ushort uhidId)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (mode == ExInInputMode.Ui)
        {
            ExInDevice pointer = new(instanceId, uhidId, PointerNameVE, identity.VendorId, identity.ProductId,
                DateTimeOffset.UtcNow, identity);
            return new(mode, pointer, ExInMouseDescriptor.GetVE(), new byte[5], _ => new byte[5]);
        }

        ExInDevice gamepad = new(
            instanceId,
            uhidId,
            XboxNameVE,
            0x045E,
            0x028E,
            DateTimeOffset.UtcNow,
            identity);
        return new(mode, gamepad, ExInDescriptor.GetVE(), ExInReport.BuildVE(default), ExInReport.BuildVE);
    }
}

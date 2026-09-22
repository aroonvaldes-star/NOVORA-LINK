using NOVORA.ExInEngine;
using Xunit;

namespace NOVORA.Tests.Test;

public sealed class NLTestExInIdentity
{
    [Theory]
    [InlineData(0x045E, 0x028E, ExInControllerFamily.Xbox)]
    [InlineData(0x054C, 0x05C4, ExInControllerFamily.DualShock4)]
    [InlineData(0x054C, 0x09CC, ExInControllerFamily.DualShock4)]
    [InlineData(0x045E, 0xFFFF, ExInControllerFamily.Generic)]
    [InlineData(0x1234, 0x5678, ExInControllerFamily.Generic)]
    public void Identity_classifies_family(ushort vendor, ushort product, ExInControllerFamily expected)
    {
        ExInControllerIdentity value =
            ExInControllerIdentity.CreateVE(1, vendor, product, Guid.Empty, "Pad", null, "path-a");

        Assert.Equal(expected, value.Family);
    }

    [Fact]
    public void Serial_is_preferred_over_path_and_instance()
    {
        ExInControllerIdentity first = CreateVE(1, "serial-a", "path-a", Guid.Empty);
        ExInControllerIdentity second = CreateVE(2, "serial-a", "path-b", Guid.NewGuid());

        Assert.Equal(first.ProfileKey, second.ProfileKey);
    }

    [Fact]
    public void Path_separates_identical_controllers_without_serials()
    {
        ExInControllerIdentity first = CreateVE(1, null, "path-a");
        ExInControllerIdentity second = CreateVE(1, null, "path-b");

        Assert.NotEqual(first.ProfileKey, second.ProfileKey);
    }

    [Fact]
    public void Instance_separates_controllers_without_stable_discriminator()
    {
        ExInControllerIdentity first = CreateVE(1, null, null);
        ExInControllerIdentity second = CreateVE(2, null, null);

        Assert.NotEqual(first.ProfileKey, second.ProfileKey);
    }

    [Fact]
    public void Name_does_not_override_vid_pid_family()
    {
        ExInControllerIdentity value =
            ExInControllerIdentity.CreateVE(1, 0x1234, 0x5678, Guid.Empty, "DualShock 4", null, null);

        Assert.Equal(ExInControllerFamily.Generic, value.Family);
    }

    private static ExInControllerIdentity CreateVE(
        uint instanceId,
        string? serial,
        string? path,
        Guid? guid = null)
        => ExInControllerIdentity.CreateVE(
            instanceId,
            0x054C,
            0x09CC,
            guid ?? new Guid("00112233-4455-6677-8899-aabbccddeeff"),
            "Wireless Controller",
            serial,
            path);
}

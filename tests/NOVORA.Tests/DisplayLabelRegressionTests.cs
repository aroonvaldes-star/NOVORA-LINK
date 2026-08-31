using NOVORA.Models;
using Xunit;

namespace NOVORA.Tests;

public sealed class DisplayLabelRegressionTests
{
    [Fact]
    public void DeviceInfo_ToString_returns_DisplayLabel()
    {
        var device = new DeviceInfo
        {
            Model = "SM-A566E",
            Connected = true,
            ConnectionType = "USB"
        };

        Assert.Equal(device.DisplayLabel, device.ToString());
        Assert.Equal("SM-A566E • USB", device.ToString());
    }

    [Fact]
    public void MonitorInfo_ToString_returns_DisplayLabel()
    {
        var monitor = new MonitorInfo(
            "\\\\.\\DISPLAY2",
            "Monitor 2",
            0,
            0,
            1920,
            1080,
            60,
            false);

        Assert.Equal(monitor.DisplayLabel, monitor.ToString());
        Assert.Equal("Monitor 2 — 1920x1080 @ 60 Hz", monitor.ToString());
    }
}

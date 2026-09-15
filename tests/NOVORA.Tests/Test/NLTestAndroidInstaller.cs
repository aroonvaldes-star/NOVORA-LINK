using System.IO;
using System.Security.Cryptography;
using NOVORA.Service;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestAndroidInstaller
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SamsungAdditionalUserSectionDoesNotHideInstalledState(bool duplicateInstalledState)
    {
        using var f = new NLTestInstallerFixture
        {
            DumpOverride = "Packages:\n  Package [com.novora.appcontrol] (abc):\n    versionCode=5 minSdk=26 targetSdk=36\n    versionName=1.4.4\n    User 0: installed=true hidden=false suspended=false enabled=0\nDexopt state:\n  User 0:\n"
                + (duplicateInstalledState ? "    User 0: installed=true hidden=false suspended=false enabled=0\n" : "")
        };
        var installer = new NLServiceAndroidInstaller(f.Run);
        if (duplicateInstalledState)
            await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InspectAsync("PHONE", f.Package));
        else
        {
            var state = await installer.InspectAsync("PHONE", f.Package);
            Assert.Equal(5L, state.InstalledVersionCode);
            Assert.Equal(NLServiceAndroidInstallAction.Update, state.Action);
        }
    }
    private sealed class NLTestInstallerFixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NOVORA-installer-" + Guid.NewGuid().ToString("N") + ".apk");
        public NLServiceAndroidPackageInfo Package { get; }
        public List<string[]> Commands { get; } = [];
        public long? Version = 5;
        public string DeviceState = "device usb:1-2 product:test";
        public string UsbSerial = "PHONE";
        public string User = "installed=true hidden=false suspended=false enabled=0";
        public string Sdk = "26";
        public string CurrentUser = "0";
        public string? DumpOverride;
        public string InstallResponse = "Performing Streamed Install\nSuccess\n";
        public bool CancelInstall;
        public bool VerifyLease;
        public bool FailPostCheck;
        private bool _installed;
        public NLTestInstallerFixture()
        {
            File.WriteAllBytes(Path, [1, 2, 3, 4]);
            Package = new(Path, "com.novora.appcontrol", 6, "1.4.5", 26, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path))));
        }
        public Task<string> Run(string[] args, CancellationToken ct)
        {
            Commands.Add(args);
            if (args.SequenceEqual(new[] { "devices", "-l" }))
                return Task.FromResult("List of devices attached\nPHONE " + DeviceState + "\n");
            if (args.SequenceEqual(new[] { "-d", "get-serialno" })) return Task.FromResult(UsbSerial);
            Assert.Equal("-s", args[0]); Assert.Equal("PHONE", args[1]);
            string command = string.Join(" ", args.Skip(2));
            if (command.StartsWith("install ", StringComparison.Ordinal))
            {
                Assert.Equal(new[] { "-s", "PHONE", "install", "--user", "0", "-r", Path }, args);
                if (VerifyLease) Assert.Throws<IOException>(() => File.WriteAllBytes(Path, [9]));
                if (CancelInstall) throw new OperationCanceledException(ct);
                if (InstallResponse.Contains("Success")) { Version = 6; _installed = true; }
                return Task.FromResult(InstallResponse);
            }
            if (_installed && FailPostCheck) throw new IOException("USB lost");
            return Task.FromResult(command switch
            {
                "get-state" => "device\n",
                "shell getprop ro.build.version.sdk" => Sdk,
                "shell am get-current-user" => CurrentUser,
                "shell pm list packages -u --user 0 com.novora.appcontrol" => Version is null ? "" : "package:com.novora.appcontrol\n",
                "shell pm path --user 0 com.novora.appcontrol" => "package:/data/app/example/base.apk\n",
                "shell dumpsys package com.novora.appcontrol" => DumpOverride ?? (Version is null
                    ? "Unable to find package: com.novora.appcontrol\n"
                    : $"Packages:\n  Package [com.novora.appcontrol] (abc):\n    versionCode={Version} minSdk=26 targetSdk=35\n    versionName={(Version == 6 ? "1.4.5" : "1.4.4")}\n    User 0: ceDataInode=1 {User}\n"),
                _ => throw new InvalidOperationException("Unexpected command: " + command)
            });
        }
        public void Dispose() => File.Delete(Path);
    }

    [Theory]
    [InlineData(null, NLServiceAndroidInstallAction.Install)]
    [InlineData(5L, NLServiceAndroidInstallAction.Update)]
    [InlineData(6L, NLServiceAndroidInstallAction.Current)]
    public async Task InspectionDistinguishesFirstInstallUpdateAndCurrent(long? version, NLServiceAndroidInstallAction action)
    {
        using var f = new NLTestInstallerFixture { Version = version };
        var installer = new NLServiceAndroidInstaller(f.Run);
        var state = await installer.InspectAsync("PHONE", f.Package);
        Assert.Equal(action, state.Action);
        Assert.DoesNotContain(f.Commands, x => x.Contains("install"));
    }

    [Theory]
    [InlineData("unauthorized usb:1-2")]
    [InlineData("offline usb:1-2")]
    public async Task RejectsUnauthorizedAndOfflineDevices(string state)
    {
        using var f = new NLTestInstallerFixture { DeviceState = state };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new NLServiceAndroidInstaller(f.Run).InspectAsync("PHONE", f.Package));
        Assert.Single(f.Commands);
    }

    [Fact]
    public async Task WindowsAdbWithoutUsbFieldUsesUniqueUsbSelector()
    {
        using var f = new NLTestInstallerFixture { DeviceState = "device product:a56xnsxx model:SM_A566E device:a56x transport_id:4" };
        var installer = new NLServiceAndroidInstaller(f.Run);
        var state = await installer.InspectAsync("PHONE", f.Package);
        Assert.Equal(NLServiceAndroidInstallAction.Update, state.Action);
        Assert.Contains(f.Commands, x => x.SequenceEqual(new[] { "-d", "get-serialno" }));
    }

    [Theory]
    [InlineData("OTHERPHONE")]
    [InlineData("error: more than one device/emulator")]
    [InlineData("unknown")]
    public async Task RejectsWrongOrNonuniqueUsbEvenWhenSelectedDeviceIsAuthorized(string usbSerial)
    {
        using var f = new NLTestInstallerFixture { DeviceState = "device product:test transport_id:1", UsbSerial = usbSerial };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new NLServiceAndroidInstaller(f.Run).InspectAsync("PHONE", f.Package));
        Assert.Equal(2, f.Commands.Count);
        Assert.DoesNotContain(f.Commands, x => x.Contains("install"));
    }

    [Fact]
    public async Task UsbSelectionIsRecheckedBeforeInstallation()
    {
        using var f = new NLTestInstallerFixture();
        var installer = new NLServiceAndroidInstaller(f.Run);
        var before = await installer.InspectAsync("PHONE", f.Package);
        f.UsbSerial = "OTHERPHONE";
        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync("PHONE", f.Package, before));
        Assert.DoesNotContain(f.Commands, x => x.Contains("install"));
    }

    [Theory]
    [InlineData("installed=false hidden=false suspended=false enabled=0")]
    [InlineData("installed=true hidden=true suspended=false enabled=0")]
    [InlineData("installed=true hidden=false suspended=true enabled=0")]
    [InlineData("installed=true hidden=false suspended=false enabled=3")]
    public async Task DoesNotMistakeInactiveUserPackageForUsableInstallation(string user)
    {
        using var f = new NLTestInstallerFixture { User = user };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new NLServiceAndroidInstaller(f.Run).InspectAsync("PHONE", f.Package));
    }

    [Fact]
    public async Task UnrecognizedAbsentResponseFailsClosed()
    {
        using var f = new NLTestInstallerFixture { Version = null, DumpOverride = "error: device disconnected" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new NLServiceAndroidInstaller(f.Run).InspectAsync("PHONE", f.Package));
    }

    [Fact]
    public async Task DuplicateVersionFieldsFailClosed()
    {
        using var f = new NLTestInstallerFixture { DumpOverride = "versionCode=5\nversionCode=4\nversionName=1.4.4\nUser 0: installed=true" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new NLServiceAndroidInstaller(f.Run).InspectAsync("PHONE", f.Package));
    }

    [Fact]
    public async Task RejectsOlderAndroidAnotherUserAndDowngrade()
    {
        using var f = new NLTestInstallerFixture { Sdk = "25" };
        var installer = new NLServiceAndroidInstaller(f.Run);
        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InspectAsync("PHONE", f.Package));
        f.Sdk = "26"; f.CurrentUser = "10";
        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InspectAsync("PHONE", f.Package));
        f.CurrentUser = "0"; f.Version = 7;
        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InspectAsync("PHONE", f.Package));
    }

    [Fact]
    public async Task UpdateRechecksAndHoldsVerifiedApkWithoutDestructiveCommands()
    {
        using var f = new NLTestInstallerFixture { VerifyLease = true };
        var installer = new NLServiceAndroidInstaller(f.Run);
        var before = await installer.InspectAsync("PHONE", f.Package);
        var after = await installer.InstallAsync("PHONE", f.Package, before);
        Assert.Equal(6, after.InstalledVersionCode);
        Assert.Equal(3, f.Commands.Count(x => x.SequenceEqual(new[] { "devices", "-l" })));
        Assert.Single(f.Commands, x => x.Contains("install"));
        Assert.DoesNotContain(f.Commands.SelectMany(x => x), x => x is "uninstall" or "clear" or "-g");
        Assert.DoesNotContain(f.Commands.Where(x => x.Contains("install")).SelectMany(x => x), x => x == "-d");
    }

    [Fact]
    public async Task CurrentVersionNeverCallsInstall()
    {
        using var f = new NLTestInstallerFixture { Version = 6 };
        var installer = new NLServiceAndroidInstaller(f.Run);
        var before = await installer.InspectAsync("PHONE", f.Package);
        await installer.InstallAsync("PHONE", f.Package, before);
        Assert.DoesNotContain(f.Commands, x => x.Contains("install"));
    }

    [Fact]
    public async Task ChangedVersionOrBytesInvalidateConfirmation()
    {
        using var f = new NLTestInstallerFixture();
        var installer = new NLServiceAndroidInstaller(f.Run);
        var before = await installer.InspectAsync("PHONE", f.Package);
        f.Version = 6;
        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync("PHONE", f.Package, before));
        f.Version = 5; File.WriteAllBytes(f.Path, [9]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync("PHONE", f.Package, before));
        Assert.DoesNotContain(f.Commands, x => x.Contains("install"));
    }

    [Fact]
    public async Task SignatureMismatchRetainsDataAndExplainsSigningKey()
    {
        using var f = new NLTestInstallerFixture { InstallResponse = "Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE]" };
        var installer = new NLServiceAndroidInstaller(f.Run);
        var before = await installer.InspectAsync("PHONE", f.Package);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync("PHONE", f.Package, before));
        Assert.Contains("firma", ex.Message);
        Assert.DoesNotContain(f.Commands.SelectMany(x => x), x => x is "uninstall" or "clear");
    }

    [Fact]
    public async Task CancellationDuringInstallReportsUnknownInsteadOfRollback()
    {
        using var f = new NLTestInstallerFixture { CancelInstall = true };
        var installer = new NLServiceAndroidInstaller(f.Run);
        var before = await installer.InspectAsync("PHONE", f.Package);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync("PHONE", f.Package, before));
        Assert.Contains("desconocido", ex.Message);
    }

    [Fact]
    public async Task AdbSuccessWithoutPostVerificationDoesNotClaimVerifiedInstall()
    {
        using var f = new NLTestInstallerFixture { FailPostCheck = true };
        var installer = new NLServiceAndroidInstaller(f.Run);
        var before = await installer.InspectAsync("PHONE", f.Package);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync("PHONE", f.Package, before));
        Assert.Contains("no se pudo verificar", ex.Message);
    }
}

using System.IO;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using NOVORA.Control;
using Xunit;
namespace NOVORA.Tests;
public sealed class NLTestAndroidTrust
{
    private static NLControlSnapshot State => new(7, "PC", "1.4.0", "4M", "Gaming", "default", "default", true,
        [new("4M", "4 Mbps")], [new("Gaming", "Juegos")], [new("default", "Windows")]);
    private static Task<NLControlReply> Handle(NLControlRequest request) => Task.FromResult(new NLControlReply(1, request.Id, request.Action is "pair" or "get", "OK", State));
    private sealed class NLTestTrustFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NOVORA-Trust-" + Guid.NewGuid().ToString("N"));
        public NLTestTrustFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
    [Fact]
    public void StoreExcludesConcurrentPcInstancesAndReleasesLockAfterDispose()
    {
        using var folder = new NLTestTrustFolder();
        string fingerprint;
        using (var first = new NLControlTrustStore(folder.Path))
        {
            fingerprint = first.Fingerprint;
            Assert.Throws<IOException>(() => new NLControlTrustStore(folder.Path));
        }
        using var reopened = new NLControlTrustStore(folder.Path);
        Assert.Equal(fingerprint, reopened.Fingerprint);
    }

    [Fact]
    public void ConstructorCorruptionFailureReleasesExclusiveProfileLock()
    {
        using var folder = new NLTestTrustFolder();
        string path = System.IO.Path.Combine(folder.Path, NLControlTrustStore.FileName);
        File.WriteAllBytes(path, [1, 2, 3]);
        Assert.ThrowsAny<CryptographicException>(() => new NLControlTrustStore(folder.Path));
        using var exclusive = new FileStream(path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
    }

    [Fact]
    public void IdentityAndSettingsPersistAndCorruptionDoesNotRegenerate()
    {
        using var folder = new NLTestTrustFolder();
        string fingerprint;
        using (var store = new NLControlTrustStore(folder.Path)) { fingerprint = store.Fingerprint; store.SetListening("192.168.1.9", true); }
        using (var store = new NLControlTrustStore(folder.Path)) { Assert.Equal(fingerprint, store.Fingerprint); Assert.True(store.ListenEnabled); Assert.Equal("192.168.1.9", store.LastHost); }
        string path = System.IO.Path.Combine(folder.Path, NLControlTrustStore.FileName);
        File.WriteAllBytes(path, [1, 2, 3]);
        Assert.ThrowsAny<CryptographicException>(() => new NLControlTrustStore(folder.Path));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
    }
    [Fact]
    public async Task EnrollRestartResumeAndRevokeAreRealTlsAndDoNotReplayMutations()
    {
        using var folder = new NLTestTrustFolder();
        NLControlTrustedPc trusted;
        int mutations = 0;
        Task<NLControlReply> Handler(NLControlRequest request)
        { if (request.Action != "pair" && request.Action != "get") mutations++; return Handle(request); }
        using (var store = new NLControlTrustStore(folder.Path))
        await using (var server = new NLControlTrustServer(IPAddress.Loopback, Handler, store, 0))
        {
            server.Start();
            await using var client = new NLControlClient();
            Assert.True((await client.ConnectLanAsync(server.Invitation, true)).Success);
            var grant = await client.SendAsync("trust.enroll", "Samsung");
            trusted = Assert.IsType<NLControlTrustedPc>(grant.TrustedPc);
            Assert.True(grant.Success); Assert.Single(store.Devices);
            Assert.False((await client.SendAsync("trust.enroll", "Duplicado")).Success);
            byte[] raw = File.ReadAllBytes(System.IO.Path.Combine(folder.Path, NLControlTrustStore.FileName));
            Assert.DoesNotContain(trusted.Token, System.Text.Encoding.UTF8.GetString(raw));
        }
        using (var store = new NLControlTrustStore(folder.Path))
        await using (var server = new NLControlTrustServer(IPAddress.Loopback, Handler, store, 0, false))
        {
            server.Start(); trusted = trusted with { Port = server.Port };
            Assert.Equal(trusted.Fingerprint, store.Fingerprint);
            await using (var client = new NLControlClient())
            {
                Assert.True((await client.ConnectTrustedAsync(trusted, true)).Success);
                Assert.True((await client.SendAsync("get")).Success);
                Assert.False((await client.SendAsync("trust.enroll", "No permitido")).Success);
            }
            store.Revoke(trusted.DeviceId);
            await using var revoked = new NLControlClient();
            await Assert.ThrowsAsync<AuthenticationException>(() => revoked.ConnectTrustedAsync(trusted, true));
            Assert.Empty(store.Devices); Assert.Equal(0, mutations);
        }
    }
    [Fact]
    public async Task WrongPinAndWrongTokenNeverInvokeHandler()
    {
        using var folder = new NLTestTrustFolder(); using var store = new NLControlTrustStore(folder.Path);
        int invoked = 0;
        await using var server = new NLControlTrustServer(IPAddress.Loopback, request => { invoked++; return Handle(request); }, store, 0, false);
        server.Start();
        var trusted = new NLControlTrustedPc("PC", "127.0.0.1", server.Port, new string('0', 64), Guid.NewGuid().ToString("N"), new string('1', 64));
        await using (var client = new NLControlClient()) await Assert.ThrowsAnyAsync<AuthenticationException>(() => client.ConnectTrustedAsync(trusted, true));
        await using (var client = new NLControlClient()) await Assert.ThrowsAsync<AuthenticationException>(() => client.ConnectTrustedAsync(trusted with { Fingerprint = store.Fingerprint }, true));
        Assert.Equal(0, invoked);
    }
    [Fact]
    public async Task CancelledInvitationCannotEnrollOrReadState()
    {
        using var folder = new NLTestTrustFolder(); using var store = new NLControlTrustStore(folder.Path);
        int invoked = 0;
        await using var server = new NLControlTrustServer(IPAddress.Loopback, request => { invoked++; return Handle(request); }, store, 0);
        server.Start(); server.CancelInvitation();
        Assert.False(server.IsInvitationOpen);
        await using var client = new NLControlClient();
        var reply = await client.ConnectLanAsync(server.Invitation, true);
        Assert.False(reply.Success); Assert.Null(reply.Snapshot); Assert.Equal(0, invoked);
    }
    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void PersistedPublicOrLoopbackDestinationIsRejected(string host)
    {
        var pc = new NLControlTrustedPc("PC", host, 27215, new string('A', 64), Guid.NewGuid().ToString("N"), new string('B', 64));
        Assert.Throws<InvalidDataException>(() => pc.Validate());
    }
}

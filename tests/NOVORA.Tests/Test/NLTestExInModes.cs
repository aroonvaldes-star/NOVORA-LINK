using NOVORA.ExInEngine;
using Xunit;

namespace NOVORA.Tests.Test;

public sealed class NLTestExInModes
{
    [Fact]
    public void Reactivation_contract_has_no_floating_ui_dependency()
    {
        var method = typeof(ExInCoreEngine).GetMethod(nameof(ExInCoreEngine.ReactivateAsync));

        Assert.NotNull(method);
        Assert.Equal([typeof(CancellationToken)], method.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public async Task Switch_neutralizes_destroys_and_recreates()
    {
        RecordingOutputVE output = new();
        await using ExInOutputGeneration owner = CreateOwnerVE(output, XboxIdentityVE());
        await owner.CreateAsync(default);
        output.Operations.Clear();

        ExInModeResult result = await owner.SetModeAsync(ExInInputMode.Ui, default);

        Assert.True(result.Success);
        Assert.Equal(["send:3:15", "destroy:3", "create:NOVORA Controller Pointer", "send:3:5"], output.Operations);
    }

    [Fact]
    public async Task Failed_switch_restores_last_valid_output()
    {
        RecordingOutputVE output = new();
        await using ExInOutputGeneration owner = CreateOwnerVE(output, XboxIdentityVE());
        await owner.CreateAsync(default);
        output.FailNextCreate = true;

        ExInModeResult result = await owner.SetModeAsync(ExInInputMode.Ui, default);

        Assert.False(result.Success);
        Assert.Equal(ExInInputMode.Game, owner.ModeVE);
        Assert.Equal("Microsoft X-Box 360 Pad", owner.ProfileVE.Device.Name);
    }

    [Fact]
    public async Task Late_generation_is_rejected_after_switch()
    {
        RecordingOutputVE output = new();
        await using ExInOutputGeneration owner = CreateOwnerVE(output, XboxIdentityVE());
        await owner.CreateAsync(default);
        long oldGeneration = owner.GenerationVE;
        await owner.SetModeAsync(ExInInputMode.Ui, default);
        output.Operations.Clear();

        Assert.False(await owner.SendAsync(oldGeneration, new byte[5], default));
        Assert.Empty(output.Operations);
    }

    [Fact]
    public async Task Inflight_send_finishes_before_mode_transition()
    {
        RecordingOutputVE output = new();
        await using ExInOutputGeneration owner = CreateOwnerVE(output, XboxIdentityVE());
        await owner.CreateAsync(default);
        output.BlockNextSendVE();

        Task<bool> send = owner.SendStateAsync(default, default);
        await output.SendStartedVE.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task<ExInModeResult> transition = owner.SetModeAsync(ExInInputMode.Ui, default);
        Assert.False(transition.IsCompleted);

        output.ReleaseSendVE();
        Assert.True(await send);
        Assert.True((await transition).Success);
        Assert.True(output.Operations.IndexOf("send:3:15") < output.Operations.LastIndexOf("destroy:3"));
    }

    [Fact]
    public async Task Cancellation_rolls_back_before_propagating()
    {
        RecordingOutputVE output = new();
        await using ExInOutputGeneration owner = CreateOwnerVE(output, XboxIdentityVE());
        await owner.CreateAsync(default);
        output.CancelNextCreate = true;
        using CancellationTokenSource cancellation = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner.SetModeAsync(ExInInputMode.Ui, cancellation.Token));
        Assert.Equal(ExInInputMode.Game, owner.ModeVE);
    }

    [Fact]
    public async Task Output_not_ready_is_not_marked_created()
    {
        RecordingOutputVE output = new() { Ready = false };
        await using ExInOutputGeneration owner = CreateOwnerVE(output, XboxIdentityVE());
        await owner.CreateAsync(default);
        Assert.False(await owner.SendStateAsync(default, default));

        output.Ready = true;
        await owner.SynchronizeAsync(default);

        Assert.Equal(["create:Microsoft X-Box 360 Pad", "send:3:15"], output.Operations);
        Assert.True(await owner.SendStateAsync(default, default));
    }

    [Fact]
    public async Task Pointer_report_is_rejected_while_game_mode_is_active()
    {
        RecordingOutputVE output = new();
        await using ExInOutputGeneration owner = CreateOwnerVE(output, XboxIdentityVE());
        await owner.CreateAsync(default);
        output.Operations.Clear();

        Assert.False(await owner.SendPointerAsync(new(1, 2, 3, 4, 1, ExInUiAction.None), default));
        Assert.Empty(output.Operations);
    }

    [Fact]
    public async Task Pointer_report_is_serialized_through_active_ui_generation()
    {
        RecordingOutputVE output = new();
        await using ExInOutputGeneration owner = CreateOwnerVE(output, XboxIdentityVE());
        await owner.CreateAsync(default);
        await owner.SetModeAsync(ExInInputMode.Ui, default);
        output.Operations.Clear();
        output.Reports.Clear();

        Assert.True(await owner.SendPointerAsync(new(1, -2, 3, -4, 1, ExInUiAction.None), default));
        Assert.Equal(["send:3:5"], output.Operations);
        Assert.Equal(new byte[] { 1, 1, 254, 3, 252 }, output.Reports.Single());
    }

    [Fact]
    public async Task Failed_initial_neutral_destroys_partial_device()
    {
        RecordingOutputVE output = new() { FailNextSend = true };
        await using ExInOutputGeneration owner = CreateOwnerVE(output, XboxIdentityVE());

        await Assert.ThrowsAsync<IOException>(() => owner.CreateAsync(default));
        Assert.Contains("destroy:3", output.Operations);
        Assert.False(await owner.SendStateAsync(default, default));
    }

    [Fact]
    public async Task Failed_synchronize_neutral_destroys_partial_device()
    {
        RecordingOutputVE output = new() { FailNextSend = true };
        await using ExInOutputGeneration owner = CreateOwnerVE(output, XboxIdentityVE());

        await Assert.ThrowsAsync<IOException>(() => owner.SynchronizeAsync(default));
        Assert.Equal(["create:Microsoft X-Box 360 Pad", "send:3:15", "destroy:3"], output.Operations);
        Assert.False(await owner.SendStateAsync(default, default));
    }

    [Fact]
    public void DualShock_profile_keeps_physical_identity_but_uses_android_standard_gamepad_output()
    {
        ExInControllerIdentity identity = ExInControllerIdentity.CreateVE(1, 0x054C, 0x09CC, Guid.Empty, "DS4", "serial", null);
        ExInOutputProfile profile = ExInOutputProfile.CreateVE(ExInInputMode.Game, identity, 1, 3);
        byte[] report = profile.BuildReportVE(new(short.MinValue, 0, 0, 0, 0, 0, 0));

        Assert.Equal(ExInControllerFamily.DualShock4, profile.Device.Identity?.Family);
        Assert.Equal((ushort)0x045E, profile.Device.VendorId);
        Assert.Equal((ushort)0x028E, profile.Device.ProductId);
        Assert.Equal("Microsoft X-Box 360 Pad", profile.Device.Name);
        Assert.Equal(ExInDescriptor.GetVE(), profile.Descriptor);
        Assert.Equal(0, report[0]);
        Assert.Equal(0, report[1]);
    }

    [Theory]
    [InlineData(0x045E, 0x028E, "Microsoft X-Box 360 Pad")]
    [InlineData(0x054C, 0x09CC, "Microsoft X-Box 360 Pad")]
    [InlineData(0x1234, 0x5678, "Microsoft X-Box 360 Pad")]
    public void Game_profile_uses_consistent_android_virtual_identity(ushort vendor, ushort product, string expectedName)
    {
        ExInControllerIdentity identity = ExInControllerIdentity.CreateVE(1, vendor, product, Guid.Empty, "Pad", "serial", null);
        ExInOutputProfile profile = ExInOutputProfile.CreateVE(ExInInputMode.Game, identity, 1, 3);

        Assert.Equal(expectedName, profile.Device.Name);
        Assert.Equal((ushort)0x045E, profile.Device.VendorId);
        Assert.Equal((ushort)0x028E, profile.Device.ProductId);
    }

    private static ExInOutputGeneration CreateOwnerVE(RecordingOutputVE output, ExInControllerIdentity identity)
    {
        ExInOutputProfile Factory(ExInInputMode mode) => ExInOutputProfile.CreateVE(mode, identity, 1, 3);
        return new(output, Factory(ExInInputMode.Game), Factory);
    }

    private static ExInControllerIdentity XboxIdentityVE()
        => ExInControllerIdentity.CreateVE(1, 0x045E, 0x028E, Guid.Empty, "Xbox", "serial", null);

    private sealed class RecordingOutputVE : IExInOutput
    {
        private TaskCompletionSource<bool>? _sendReleaseVE;
        public bool Ready { get; set; } = true;
        public bool IsReady => Ready;
        public bool FailNextCreate { get; set; }
        public bool CancelNextCreate { get; set; }
        public bool FailNextSend { get; set; }
        public List<string> Operations { get; } = [];
        public List<byte[]> Reports { get; } = [];
        public TaskCompletionSource<bool> SendStartedVE { get; private set; } = NewCompletionVE();

        public Task CreateAsync(ExInDevice device, byte[] descriptor, CancellationToken cancellationToken)
        {
            if (FailNextCreate)
            {
                FailNextCreate = false;
                throw new IOException("create failed");
            }
            if (CancelNextCreate)
            {
                CancelNextCreate = false;
                throw new OperationCanceledException(cancellationToken);
            }
            Operations.Add("create:" + device.Name);
            return Task.CompletedTask;
        }

        public async Task SendAsync(ushort deviceId, byte[] report, CancellationToken cancellationToken)
        {
            Operations.Add($"send:{deviceId}:{report.Length}");
            Reports.Add(report.ToArray());
            if (FailNextSend)
            {
                FailNextSend = false;
                throw new IOException("send failed");
            }
            if (_sendReleaseVE is not null)
            {
                SendStartedVE.TrySetResult(true);
                await _sendReleaseVE.Task.WaitAsync(cancellationToken);
                _sendReleaseVE = null;
            }
        }

        public Task DestroyAsync(ushort deviceId, CancellationToken cancellationToken)
        {
            Operations.Add("destroy:" + deviceId);
            return Task.CompletedTask;
        }

        public void BlockNextSendVE()
        {
            SendStartedVE = NewCompletionVE();
            _sendReleaseVE = NewCompletionVE();
        }

        public void ReleaseSendVE() => _sendReleaseVE?.TrySetResult(true);
        private static TaskCompletionSource<bool> NewCompletionVE()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

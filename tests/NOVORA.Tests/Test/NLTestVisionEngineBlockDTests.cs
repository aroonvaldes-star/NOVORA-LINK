using NOVORA.VisionEngine.Renderer;
using Xunit;

namespace NOVORA.Tests;

public sealed class NLTestVisionEngineBlockDTests
{
    [Fact]
    public void ScalingRendererVE_preserves_source_aspect_ratio()
    {
        VERendererRect result = VERendererScaling.FitVE(
            sourceWidth: 1080,
            sourceHeight: 2400,
            targetWidth: 1280,
            targetHeight: 720);

        Assert.Equal(324f, result.Width, 1);
        Assert.Equal(720f, result.Height, 1);
        Assert.Equal(478f, result.X, 1);
        Assert.Equal(0f, result.Y, 1);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 90)]
    [InlineData(450, 90)]
    [InlineData(-90, 270)]
    public void RotationRendererVE_normalizes_quarter_turns(int input, int expected)
    {
        Assert.Equal(expected, VERendererRotation.NormalizeVE(input));
    }

    [Fact]
    public void ViewportRendererVE_swaps_dimensions_for_portrait_rotation()
    {
        VERendererRect result = VERendererViewport.CalculateVE(
            frameWidth: 1920,
            frameHeight: 1080,
            outputWidth: 720,
            outputHeight: 1280,
            rotationDegrees: 90);

        Assert.Equal(720f, result.Width, 1);
        Assert.Equal(1280f, result.Height, 1);
        Assert.Equal(0f, result.X, 1);
        Assert.Equal(0f, result.Y, 1);
    }

    [Fact]
    public void QueueRendererVE_drops_oldest_when_capacity_is_two()
    {
        using VERendererQueue<NLTestTestFrame> queue = new(capacity: 2);
        NLTestTestFrame first = new(1);
        NLTestTestFrame second = new(2);
        NLTestTestFrame third = new(3);

        Assert.Equal(0, queue.EnqueueVE(first));
        Assert.Equal(0, queue.EnqueueVE(second));
        Assert.Equal(1, queue.EnqueueVE(third));
        Assert.True(first.DisposedVE);
        Assert.False(second.DisposedVE);
        Assert.False(third.DisposedVE);

        Assert.True(queue.TryTakeLatestVE(out NLTestTestFrame? latest, out int stale));
        Assert.Equal(1, stale);
        Assert.True(second.DisposedVE);
        Assert.Same(third, latest);

        latest!.Dispose();
        Assert.True(third.DisposedVE);
    }

    [Fact]
    public void StatusRendererVE_starts_with_renderer_disabled()
    {
        VERendererStatus status = VERendererStatus.CreateInitialVE();

        Assert.False(status.RendererEnabled);
        Assert.Equal(VERendererStates.Stopped, status.State);
        Assert.Equal(0, status.Metrics.FramesPresented);
    }

    private sealed class NLTestTestFrame : IDisposable
    {
        public NLTestTestFrame(int id)
        {
            IdVE = id;
        }

        public int IdVE { get; }
        public bool DisposedVE { get; private set; }

        public void Dispose()
        {
            DisposedVE = true;
        }
    }
}

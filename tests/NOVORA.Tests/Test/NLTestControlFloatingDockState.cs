using NOVORA.Control;
using Xunit;

namespace NOVORA.Tests.Test;

public sealed class NLTestControlFloatingDockState
{
    [Fact]
    public void Docks_only_after_four_idle_seconds_when_interaction_is_clear()
    {
        Assert.False(NLControlFloatingDockState.ShouldDock(TimeSpan.FromMilliseconds(3999), false, false, false));
        Assert.True(NLControlFloatingDockState.ShouldDock(TimeSpan.FromSeconds(4), false, false, false));
        Assert.False(NLControlFloatingDockState.ShouldDock(TimeSpan.FromSeconds(5), true, false, false));
        Assert.False(NLControlFloatingDockState.ShouldDock(TimeSpan.FromSeconds(5), false, true, false));
        Assert.False(NLControlFloatingDockState.ShouldDock(TimeSpan.FromSeconds(5), false, false, true));
    }

    [Theory]
    [InlineData(20, NLControlDockEdge.Left)]
    [InlineData(500, NLControlDockEdge.Right)]
    public void Uses_nearest_edge_and_leaves_only_the_handle_visible(int x, NLControlDockEdge edge)
    {
        Assert.Equal(edge, NLControlFloatingDockState.NearestEdge(x, 600, 56));
        Assert.Equal(edge == NLControlDockEdge.Left ? -46 : 590,
            NLControlFloatingDockState.DockedX(edge, 600, 56, 10));
    }
}

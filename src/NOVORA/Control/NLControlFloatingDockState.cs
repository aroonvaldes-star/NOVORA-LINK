namespace NOVORA.Control;

public enum NLControlDockEdge
{
    Left,
    Right
}

public static class NLControlFloatingDockState
{
    public static readonly TimeSpan InactivityDelay = TimeSpan.FromSeconds(4);

    public static bool ShouldDock(TimeSpan inactiveFor, bool expanded, bool busy, bool dragging)
        => inactiveFor >= InactivityDelay && !expanded && !busy && !dragging;

    public static NLControlDockEdge NearestEdge(int x, int surfaceWidth, int bubbleWidth)
    {
        int center = x + Math.Max(0, bubbleWidth) / 2;
        return center <= Math.Max(0, surfaceWidth) / 2 ? NLControlDockEdge.Left : NLControlDockEdge.Right;
    }

    public static int DockedX(NLControlDockEdge edge, int surfaceWidth, int bubbleWidth, int visibleHandleWidth)
    {
        int bubble = Math.Max(0, bubbleWidth);
        int handle = Math.Clamp(visibleHandleWidth, 0, bubble);
        return edge == NLControlDockEdge.Left
            ? -(bubble - handle)
            : Math.Max(0, surfaceWidth - handle);
    }
}

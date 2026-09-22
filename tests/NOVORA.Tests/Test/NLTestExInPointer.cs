using NOVORA.ExInEngine;
using Xunit;

namespace NOVORA.Tests.Test;

public sealed class NLTestExInPointer
{
    [Fact]
    public void Xbox_maps_sticks_click_and_navigation()
    {
        ExInPointerTranslator value = new(0.08);
        ExInPointerReport pointer = value.FromXboxVE(new(6000, 0, 0, 12000, 0, 0,
            ExInButtons.South | ExInButtons.DPadUp));
        Assert.Equal(2, pointer.X);
        Assert.Equal(-1, pointer.Wheel);
        Assert.Equal(1, pointer.Buttons);
        Assert.Equal(ExInUiAction.Up, pointer.Action);
    }

    [Fact]
    public void Xbox_deadzone_suppresses_drift_and_b_maps_back()
    {
        ExInPointerTranslator value = new(0.08);
        ExInPointerReport pointer = value.FromXboxVE(new(2000, -2000, 0, 0, 0, 0, ExInButtons.East));
        Assert.Equal(0, pointer.X);
        Assert.Equal(0, pointer.Y);
        Assert.Equal(ExInUiAction.Back, pointer.Action);
    }

    [Fact]
    public void DualShock_one_finger_moves_and_clicks()
    {
        ExInPointerTranslator value = new();
        value.TouchDownVE(0, 10, 0.4f, 0.4f, false);
        ExInPointerReport report = value.TouchMotionVE(0, 10, 0.41f, 0.42f, true);
        Assert.Equal(9, report.X);
        Assert.Equal(18, report.Y);
        Assert.Equal(1, report.Buttons);
    }

    [Fact]
    public void DualShock_two_fingers_scroll_regardless_of_order()
    {
        ExInPointerTranslator value = new();
        value.TouchDownVE(0, 20, 0.7f, 0.5f, false);
        value.TouchDownVE(0, 10, 0.3f, 0.5f, false);
        ExInPointerReport first = value.TouchMotionVE(0, 10, 0.3f, 0.55f, false);
        ExInPointerReport second = value.TouchMotionVE(0, 20, 0.7f, 0.55f, false);
        Assert.Equal(-2, first.Wheel + second.Wheel);
        Assert.Equal(0, first.X);
    }

    [Fact]
    public void Malformed_touch_resets_only_gesture_state()
    {
        ExInPointerTranslator value = new();
        value.TouchDownVE(0, 1, 0.5f, 0.5f, false);
        Assert.Throws<ArgumentOutOfRangeException>(() => value.TouchMotionVE(0, 1, float.NaN, 0.5f, false));
        Assert.Equal(default, value.TouchMotionVE(0, 1, 0.6f, 0.6f, false));
        Assert.Equal(2, value.FromXboxVE(new(6000, 0, 0, 0, 0, 0, 0)).X);
    }

    [Fact]
    public void Held_navigation_action_is_edge_triggered()
    {
        ExInPointerTranslator value = new();
        ExInState held = new(0, 0, 0, 0, 0, 0, ExInButtons.DPadRight);
        Assert.Equal(ExInUiAction.Right, value.FromXboxVE(held).Action);
        Assert.Equal(ExInUiAction.None, value.FromXboxVE(held).Action);
        value.FromXboxVE(default);
        Assert.Equal(ExInUiAction.Right, value.FromXboxVE(held).Action);
    }

    [Fact]
    public void Simultaneous_buttons_do_not_retrigger_held_action()
    {
        ExInPointerTranslator value = new();
        ExInState aHeld = new(0, 0, 0, 0, 0, 0, ExInButtons.South);
        Assert.Equal(1, value.FromXboxVE(aHeld).Buttons);
        value.FromXboxVE(aHeld with { Buttons = ExInButtons.South | ExInButtons.DPadUp });
        ExInPointerReport afterDpadRelease = value.FromXboxVE(aHeld);
        Assert.Equal(ExInUiAction.None, afterDpadRelease.Action);
        Assert.Equal(1, afterDpadRelease.Buttons);
    }

    [Fact]
    public void Calibrated_input_does_not_apply_second_deadzone()
    {
        ExInPointerTranslator value = new(0.08);
        Assert.Equal(0, value.FromXboxVE(new(1000, 0, 0, 0, 0, 0, 0)).X);
        Assert.NotEqual(0, value.FromXboxVE(new(1000, 0, 0, 0, 0, 0, 0), inputAlreadyCalibrated: true).X);
    }

    [Fact]
    public void Invalid_touch_order_resets_gesture()
    {
        ExInPointerTranslator value = new();
        value.TouchDownVE(0, 1, 0.5f, 0.5f, false);
        Assert.Throws<InvalidOperationException>(() => value.TouchDownVE(0, 1, 0.6f, 0.6f, false));
        Assert.Equal(default, value.TouchMotionVE(0, 1, 0.7f, 0.7f, false));
    }

    [Fact]
    public void Mouse_report_preserves_signed_deltas()
    {
        byte[] report = ExInMouseReport.BuildVE(new(-2, 3, -1, 1, 1, ExInUiAction.None));
        Assert.Equal([1, 254, 3, 255, 1], report);
    }

    [Fact]
    public void Repeated_axes_keep_motion_without_retriggering_navigation()
    {
        ExInPointerTranslator value = new();
        ExInState held = new(12000, 0, 0, 0, 0, 0, ExInButtons.DPadRight);

        Assert.Equal(ExInUiAction.Right, value.FromXboxVE(held).Action);
        ExInPointerReport repeat = value.FromAxesVE(held);
        Assert.NotEqual(0, repeat.X);
        Assert.Equal(ExInUiAction.None, repeat.Action);
        Assert.Equal(ExInUiAction.None, value.FromXboxVE(held).Action);
    }

    [Fact]
    public void Priming_buttons_prevents_privacy_exit_from_replaying_held_action()
    {
        ExInPointerTranslator value = new();
        ExInState held = new(0, 0, 0, 0, 0, 0, ExInButtons.East);

        value.PrimeButtonsVE(held.Buttons);

        Assert.Equal(ExInUiAction.None, value.FromXboxVE(held).Action);
        Assert.Equal(ExInUiAction.None, value.FromXboxVE(default).Action);
        Assert.Equal(ExInUiAction.Back, value.FromXboxVE(held).Action);
    }
}

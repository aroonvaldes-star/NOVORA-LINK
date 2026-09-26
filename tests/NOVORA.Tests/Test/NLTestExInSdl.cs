using NOVORA.ExInEngine;
using Xunit;

namespace NOVORA.Tests.Test;

public sealed class NLTestExInSdl
{
    [Fact]
    public void Background_gamepad_events_are_enabled_with_the_official_sdl_hint()
    {
        string? name = null;
        string? value = null;

        bool enabled = ExInSdl.EnableBackgroundEventsVE((hintName, hintValue) =>
        {
            name = hintName;
            value = hintValue;
            return true;
        });

        Assert.True(enabled);
        Assert.Equal("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", name);
        Assert.Equal("1", value);
    }
}

using NOVORA.Service;
using Xunit;

namespace NOVORA.Tests.Test;

public sealed class NLTestSingleInstance
{
    [Fact]
    public void Second_owner_is_rejected_until_the_first_owner_exits()
    {
        string name = "Local\\NOVORA-LINK.Tests." + Guid.NewGuid().ToString("N");

        using NLServiceSingleInstance? first = NLServiceSingleInstance.TryAcquireNV(name);
        using NLServiceSingleInstance? second = NLServiceSingleInstance.TryAcquireNV(name);

        Assert.NotNull(first);
        Assert.Null(second);

        first.Dispose();
        using NLServiceSingleInstance? replacement = NLServiceSingleInstance.TryAcquireNV(name);
        Assert.NotNull(replacement);
    }
}

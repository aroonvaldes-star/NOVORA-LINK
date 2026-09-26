using NOVORA.Contracts.Input;
using NOVORA.ExInEngine;
using NOVORA.VisionEngine.Control;
using Xunit;

namespace NOVORA.Tests.Test;

public sealed class NLTestEngineBoundaryContracts
{
    [Fact]
    public void ExIn_and_VisionEngine_share_only_the_neutral_input_output_contract()
    {
        Assert.Contains(typeof(INLInputOutput), typeof(VEControlManager).GetInterfaces());
        Assert.DoesNotContain(
            typeof(VEControlManager).GetInterfaces(),
            contract => contract.Namespace == typeof(ExInCoreEngine).Namespace);
    }
}

using System.Reflection;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class ReasoningProviderBoundaryTests
{
    [Fact]
    public void ApprovedReasoningRequest_HasNoPublicConstructorOrFactory()
    {
        var type = typeof(ApprovedReasoningRequest);

        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.DoesNotContain(
            type.GetMethods(BindingFlags.Public | BindingFlags.Static),
            method => method.ReturnType == type);
    }
}

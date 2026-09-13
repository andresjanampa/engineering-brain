using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class SystemSecurityPolicyCatalogTests
{
    [Fact]
    public void Definitions_ContainFiveUniquePoliciesInStableOrder()
    {
        var definitions = SystemSecurityPolicyCatalog.Definitions;

        Assert.Equal(
            [
                "SYS_REMOTE_COMPLETE_REPOSITORY",
                "SYS_REMOTE_RAW_SNAPSHOT",
                "SYS_REMOTE_SOURCE_BODIES",
                "SYS_REMOTE_SECRETS",
                "SYS_REMOTE_ABSOLUTE_PATHS"
            ],
            definitions.Select(definition => definition.PolicyId));
        Assert.Equal(definitions.Count, definitions.Select(definition => definition.PolicyId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(definitions.Count, definitions.Select(definition => definition.Category).Distinct().Count());
    }

    [Fact]
    public void Definitions_MapEveryProhibitedScopeToBlockVersionOne()
    {
        Assert.Equal(
            [
                PolicyContentScope.CompleteRepository,
                PolicyContentScope.RawSnapshot,
                PolicyContentScope.SourceBodies,
                PolicyContentScope.Secrets,
                PolicyContentScope.AbsoluteLocalPaths
            ],
            SystemSecurityPolicyCatalog.Definitions.Select(definition => definition.Category));
        Assert.All(SystemSecurityPolicyCatalog.Definitions, definition =>
        {
            Assert.Equal(1, definition.PolicyVersion);
            Assert.Equal(PolicySeverity.Block, definition.Severity);
            Assert.False(string.IsNullOrWhiteSpace(definition.AllowedDiagnostic));
            Assert.False(string.IsNullOrWhiteSpace(definition.BlockedDiagnostic));
            Assert.False(string.IsNullOrWhiteSpace(definition.UnknownDiagnostic));
        });
    }
}

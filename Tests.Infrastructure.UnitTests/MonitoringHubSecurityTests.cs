using System.Reflection;
using FluentAssertions;
using Infrastructure.Hubs;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public class MonitoringHubSecurityTests
{
    [Fact]
    public void MonitoringHub_ShouldNotExposeCallerInvokableMethods()
    {
        var callerInvokableMethods = typeof(MonitoringHub)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetBaseDefinition().DeclaringType == typeof(MonitoringHub))
            .Select(method => method.Name);

        callerInvokableMethods.Should().BeEmpty();
    }
}

using System.Net;
using Microsoft.Extensions.Configuration;
using TelemetryLoom.Service.Access;

namespace TelemetryLoom.Core.Tests;

public sealed class AccessPolicyTests
{
    [Fact]
    public void MissingConfigurationDefaultsToLoopbackOnly()
    {
        var configuration = new ConfigurationBuilder().Build();

        var policy = AccessPolicy.FromConfiguration(configuration);

        Assert.Equal(AccessMode.LocalOnly, policy.Mode);
        Assert.Equal(IPAddress.Loopback, policy.ConfiguredAddress);
        Assert.Equal(5198, policy.Port);
        Assert.Equal([IPAddress.Loopback], policy.ListenAddresses);
        Assert.Equal(["http://127.0.0.1:5198"], policy.ListenUrls);
        Assert.False(policy.RemoteClientsReadOnly);
    }

    [Fact]
    public void ExplicitLanReadOnlyPreservesLoopbackAndAddsOnlySelectedAddress()
    {
        var policy = AccessPolicy.Create(new AccessOptions
        {
            Mode = AccessMode.LanReadOnly,
            ListenAddress = "192.0.2.10",
            Port = 5210
        });

        Assert.Equal([IPAddress.Loopback, IPAddress.Parse("192.0.2.10")], policy.ListenAddresses);
        Assert.Equal(["http://127.0.0.1:5210", "http://192.0.2.10:5210"], policy.ListenUrls);
        Assert.True(policy.RemoteClientsReadOnly);
    }

    [Theory]
    [InlineData(AccessMode.LocalOnly, "192.0.2.10", 5198)]
    [InlineData(AccessMode.LanReadOnly, "127.0.0.1", 5198)]
    [InlineData(AccessMode.LanReadOnly, "0.0.0.0", 5198)]
    [InlineData(AccessMode.LanReadOnly, "224.0.0.1", 5198)]
    [InlineData(AccessMode.LanReadOnly, "not-an-address", 5198)]
    [InlineData(AccessMode.LocalOnly, "127.0.0.1", 0)]
    [InlineData(AccessMode.LocalOnly, "127.0.0.1", 65536)]
    public void UnsafeOrInvalidCombinationsAreRejected(AccessMode mode, string address, int port)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => AccessPolicy.Create(new AccessOptions
        {
            Mode = mode,
            ListenAddress = address,
            Port = port
        }));

        Assert.NotEmpty(exception.Message);
    }

    [Fact]
    public void UnsupportedConfiguredModeIsRejectedClearly()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["TelemetryLoom:Access:Mode"] = "RemoteAdmin"
        });

        var exception = Assert.Throws<InvalidOperationException>(() => AccessPolicy.FromConfiguration(configuration));

        Assert.Contains("Invalid TelemetryLoom:Access configuration", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyKestrelEndpointConfigurationIsRejectedRatherThanCombined()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Kestrel:Endpoints:Http:Url"] = "http://0.0.0.0:5198"
        });

        var exception = Assert.Throws<InvalidOperationException>(() => AccessPolicy.FromConfiguration(configuration));

        Assert.Contains("product access policy remains authoritative", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ClassifierUsesConnectionOriginAndConfiguredMode()
    {
        var localOnly = new RequestAuthorityClassifier(AccessPolicy.Create(new AccessOptions()));
        var lanReadOnly = new RequestAuthorityClassifier(AccessPolicy.Create(new AccessOptions
        {
            Mode = AccessMode.LanReadOnly,
            ListenAddress = "192.0.2.10"
        }));

        Assert.Equal(RequestAuthority.LocalAdmin, localOnly.Classify(IPAddress.Loopback));
        Assert.Equal(RequestAuthority.LocalAdmin, lanReadOnly.Classify(IPAddress.IPv6Loopback));
        Assert.Equal(RequestAuthority.Denied, localOnly.Classify(IPAddress.Parse("192.0.2.20")));
        Assert.Equal(RequestAuthority.RemoteRead, lanReadOnly.Classify(IPAddress.Parse("192.0.2.20")));
        Assert.Equal(RequestAuthority.Denied, lanReadOnly.Classify((IPAddress?)null));
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}

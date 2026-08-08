using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TelemetryLoom.Core.Configuration;
using TelemetryLoom.Core.Sensors;
using TelemetryLoom.Service.Access;

namespace TelemetryLoom.Core.Tests;

public sealed class AccessIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "telemetry-loom-tests", Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _lanFactory;
    private readonly WebApplicationFactory<Program> _localFactory;

    public AccessIntegrationTests()
    {
        Directory.CreateDirectory(_root);
        _lanFactory = CreateFactory(AccessMode.LanReadOnly, "192.0.2.10", "lan-config.json");
        _localFactory = CreateFactory(AccessMode.LocalOnly, "127.0.0.1", "local-config.json");
    }

    [Fact]
    public async Task RemoteTelemetryReadsAndSseSucceedOnlyInLanReadOnlyMode()
    {
        using var remote = CreateClient(_lanFactory, "192.0.2.20");

        foreach (var path in new[] { "/api/status", "/api/sensors", "/api/aliases", "/api/calculations" })
        {
            Assert.Equal(HttpStatusCode.OK, (await remote.GetAsync(path)).StatusCode);
        }

        using var status = JsonDocument.Parse(await remote.GetStringAsync("/api/status"));
        Assert.Equal("telemetry-loom", status.RootElement.GetProperty("service").GetString());
        var access = status.RootElement.GetProperty("access");
        Assert.Equal("LanReadOnly", access.GetProperty("mode").GetString());
        Assert.True(access.GetProperty("remoteClientsReadOnly").GetBoolean());
        Assert.Equal(
            ["http://127.0.0.1:5198", "http://192.0.2.10:5198"],
            access.GetProperty("listenAddresses").EnumerateArray().Select(item => item.GetString()!).ToArray());

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var streamRequest = new HttpRequestMessage(HttpMethod.Get, "/api/sensors/stream");
        using var streamResponse = await remote.SendAsync(
            streamRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellation.Token);
        Assert.Equal(HttpStatusCode.OK, streamResponse.StatusCode);
        await using var body = await streamResponse.Content.ReadAsStreamAsync(cancellation.Token);
        using var reader = new StreamReader(body);
        Assert.StartsWith("id: ", await reader.ReadLineAsync(cancellation.Token), StringComparison.Ordinal);
        Assert.Equal("event: sensors", await reader.ReadLineAsync(cancellation.Token));

        using var blocked = CreateClient(_localFactory, "192.0.2.20");
        Assert.Equal(HttpStatusCode.Forbidden, (await blocked.GetAsync("/api/status")).StatusCode);
    }

    [Fact]
    public async Task RemoteBrowserIsReadOnlyAndAdministrationPagesAreForbidden()
    {
        using var remote = CreateClient(_lanFactory, "192.0.2.20");

        var overview = await remote.GetStringAsync("/");
        var sensors = await remote.GetStringAsync("/sensors");

        Assert.Contains("Remote read-only", overview, StringComparison.Ordinal);
        Assert.Contains("data-access-authority=\"RemoteRead\"", overview, StringComparison.Ordinal);
        Assert.Contains("LanReadOnly", overview, StringComparison.Ordinal);
        Assert.Contains("Remote read-only", sensors, StringComparison.Ordinal);
        Assert.DoesNotContain("Create alias", sensors, StringComparison.Ordinal);
        Assert.DoesNotContain("Manage alias", sensors, StringComparison.Ordinal);
        Assert.DoesNotContain("Manage calculation", sensors, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/aliases", overview, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/calculations", overview, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await remote.GetAsync("/aliases")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await remote.GetAsync("/calculations")).StatusCode);

        using var local = CreateClient(_lanFactory);
        var localSensors = await local.GetStringAsync("/sensors");
        Assert.Contains("Create alias", localSensors, StringComparison.Ordinal);
        Assert.DoesNotContain("Remote read-only", localSensors, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await local.GetAsync("/aliases")).StatusCode);
    }

    [Fact]
    public async Task RemoteMutationsReturnForbiddenAndHostSpoofingCannotUpgradeAuthority()
    {
        using var local = CreateClient(_lanFactory);
        Assert.Equal(HttpStatusCode.OK, (await local.PutAsJsonAsync(
            "/api/aliases/security.input",
            new { displayName = "Security Input", sensorId = SimulatedSensorSource.TemperatureSensorId })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await local.PutAsJsonAsync(
            "/api/calculations/security.scaled",
            new { displayName = "Security Scaled", formula = "security.input * 2" })).StatusCode);

        var configuration = _lanFactory.Services.GetRequiredService<TelemetryConfigurationRegistry>();
        var revisionBefore = configuration.GetStatus().Revision;
        var fileBefore = await File.ReadAllTextAsync(Path.Combine(_root, "lan-config.json"));
        using var remote = CreateClient(_lanFactory, "192.0.2.20", "localhost:5198");

        var attempts = new[]
        {
            await remote.PutAsJsonAsync("/api/aliases/security.remote", new
            {
                displayName = "Remote Alias",
                sensorId = SimulatedSensorSource.TemperatureSensorId
            }),
            await remote.PutAsJsonAsync("/api/aliases/security.input", new
            {
                displayName = "Spoofed Rename",
                sensorId = SimulatedSensorSource.TemperatureSensorId
            }),
            await remote.DeleteAsync("/api/aliases/security.input"),
            await remote.PutAsJsonAsync("/api/calculations/security.remote", new
            {
                displayName = "Remote Calculation",
                formula = "security.input * 3"
            }),
            await remote.PutAsJsonAsync("/api/calculations/security.scaled", new
            {
                displayName = "Spoofed Formula",
                formula = "security.input * 4"
            }),
            await remote.DeleteAsync("/api/calculations/security.scaled"),
            await remote.PostAsync("/aliases?handler=Save", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Key"] = "security.remote.page",
                ["Input.DisplayName"] = "Remote Page Alias",
                ["Input.SensorId"] = SimulatedSensorSource.TemperatureSensorId,
                ["Input.Revision"] = revisionBefore.ToString()
            })),
            await remote.PostAsync("/calculations?handler=Save", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Key"] = "security.remote.page",
                ["Input.DisplayName"] = "Remote Page Calculation",
                ["Input.Formula"] = "security.input * 5",
                ["Input.Revision"] = revisionBefore.ToString()
            }))
        };

        Assert.All(attempts, response => Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode));
        Assert.Equal(HttpStatusCode.OK, (await remote.GetAsync("/api/status")).StatusCode);
        Assert.Equal(revisionBefore, configuration.GetStatus().Revision);
        Assert.Equal(fileBefore, await File.ReadAllTextAsync(Path.Combine(_root, "lan-config.json")));
        Assert.Null(configuration.GetSnapshot().Aliases.SingleOrDefault(alias => alias.Key == "security.remote"));
        Assert.Equal("Security Input", configuration.GetSnapshot().Aliases.Single().DisplayName);
        Assert.Equal("security.input * 2", configuration.GetSnapshot().CalculatedSensors.Single().Formula);
        Assert.Equal(HttpStatusCode.NoContent, (await local.DeleteAsync("/api/calculations/security.scaled")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await local.DeleteAsync("/api/aliases/security.input")).StatusCode);
    }

    [Fact]
    public async Task LocalMutationBehaviorRemainsAvailableInLanReadOnlyMode()
    {
        using var local = CreateClient(_lanFactory);

        var alias = await local.PutAsJsonAsync("/api/aliases/local.write", new
        {
            displayName = "Local Write",
            sensorId = SimulatedSensorSource.TemperatureSensorId
        });
        var calculation = await local.PutAsJsonAsync("/api/calculations/local.scaled", new
        {
            displayName = "Local Scaled",
            formula = "local.write * 2"
        });

        Assert.Equal(HttpStatusCode.OK, alias.StatusCode);
        Assert.Equal(HttpStatusCode.OK, calculation.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await local.DeleteAsync("/api/calculations/local.scaled")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await local.DeleteAsync("/api/aliases/local.write")).StatusCode);
    }

    public void Dispose()
    {
        _lanFactory.Dispose();
        _localFactory.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private WebApplicationFactory<Program> CreateFactory(AccessMode mode, string address, string configName) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            var testAccessPolicy = AccessPolicy.Create(new AccessOptions
            {
                Mode = mode,
                ListenAddress = address,
                Port = 5198
            });
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TelemetryLoom:ConfigPath"] = Path.Combine(_root, configName),
                    ["Hwmon:RootPath"] = Path.Combine(_root, "missing-hwmon"),
                    ["TelemetryLoom:Access:Mode"] = mode.ToString(),
                    ["TelemetryLoom:Access:ListenAddress"] = address,
                    ["TelemetryLoom:Access:Port"] = "5198",
                    ["TelemetryLoom:LiveUpdates:IntervalMilliseconds"] = "100"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<AccessPolicy>();
                services.AddSingleton(testAccessPolicy);
                services.AddSingleton<IStartupFilter, TestRemoteAddressStartupFilter>();
            });
        });

    private static HttpClient CreateClient(
        WebApplicationFactory<Program> factory,
        string? remoteAddress = null,
        string? host = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        if (remoteAddress is not null)
        {
            client.DefaultRequestHeaders.Add(TestRemoteAddressStartupFilter.HeaderName, remoteAddress);
        }

        if (host is not null)
        {
            client.DefaultRequestHeaders.Host = host;
        }

        return client;
    }
}

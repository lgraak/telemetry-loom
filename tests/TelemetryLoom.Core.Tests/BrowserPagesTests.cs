using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TelemetryLoom.Contracts.Aliases;
using TelemetryLoom.Contracts.Calculations;
using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Core.Aliases;
using TelemetryLoom.Core.Calculations;
using TelemetryLoom.Core.Configuration;
using TelemetryLoom.Core.Sensors;

namespace TelemetryLoom.Core.Tests;

public sealed class BrowserPagesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "telemetry-loom-tests", Guid.NewGuid().ToString("N"));
    private readonly string _configPath;
    private readonly WebApplicationFactory<Program> _factory;

    public BrowserPagesTests()
    {
        Directory.CreateDirectory(_root);
        _configPath = Path.Combine(_root, "config.json");
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TelemetryLoom:ConfigPath"] = _configPath,
                    ["Hwmon:RootPath"] = Path.Combine(_root, "missing-hwmon")
                }));
            builder.ConfigureServices(services =>
                services.AddSingleton<ISensorSource>(new BrowserFixtureSensorSource()));
        });
    }

    [Fact]
    public async Task OverviewRendersReadOnlyConfigurationStatus()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Telemetry Loom is running", html, StringComparison.Ordinal);
        Assert.Contains(_configPath, html, StringComparison.Ordinal);
        Assert.Contains($"{_configPath}.previous", html, StringComparison.Ordinal);
        Assert.Contains("None since startup", html, StringComparison.Ordinal);
        Assert.Contains("Out-of-process edits", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<form", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("type=\"submit\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SensorsRenderGroupedPresentationAndDistinctStableIdentitySafely()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/sensors");
        var html = await response.Content.ReadAsStringAsync();
        var renderedText = WebUtility.HtmlDecode(html);
        var script = await client.GetStringAsync("/js/sensors.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("AMD GPU", html, StringComparison.Ordinal);
        Assert.Contains("GPU Edge Temperature", html, StringComparison.Ordinal);
        Assert.Contains("Raw: edge", html, StringComparison.Ordinal);
        Assert.Contains("Temperature reported for the edge sensor", html, StringComparison.Ordinal);
        Assert.Contains("browser-fixture:gpu:edge", html, StringComparison.Ordinal);
        Assert.Contains("Available", html, StringComparison.Ordinal);
        Assert.Contains("Stale", html, StringComparison.Ordinal);
        Assert.Contains("Unavailable", html, StringComparison.Ordinal);
        Assert.Contains("—", renderedText, StringComparison.Ordinal);
        Assert.Contains("°C", renderedText, StringComparison.Ordinal);
        Assert.Contains("2026-08-07 12:00:00Z", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;unsafe()&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>unsafe()</script>", html, StringComparison.Ordinal);
        Assert.Contains("/api/sensors/stream", script, StringComparison.Ordinal);
        Assert.DoesNotContain("fetch(\"/api/sensors\"", script, StringComparison.Ordinal);
        Assert.Contains("Manage alias", script, StringComparison.Ordinal);
        Assert.Contains("encodeURIComponent(reading.alias)", script, StringComparison.Ordinal);
        Assert.Contains("encodeURIComponent(reading.id)", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AliasWorkflowCreatesAndRendersHtmlEncodedAlias()
    {
        using var client = CreateNoRedirectClient();
        var token = await GetAntiforgeryToken(client, "/aliases?targetId=browser-fixture%3Agpu%3Aedge");

        var response = await PostForm(client, "/aliases?handler=Save", token, new Dictionary<string, string>
        {
            ["Input.Key"] = "temperature.gpu.edge",
            ["Input.DisplayName"] = "<script>GPU Edge</script>",
            ["Input.SensorId"] = "browser-fixture:gpu:edge",
            ["Input.Revision"] = "0"
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/aliases/temperature.gpu.edge", response.Headers.Location?.OriginalString);
        var html = await client.GetStringAsync(response.Headers.Location);
        Assert.Contains("Saved alias", html, StringComparison.Ordinal);
        Assert.Contains("Configuration revision: 1", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;GPU Edge&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>GPU Edge</script>", html, StringComparison.Ordinal);

        var saved = new JsonTelemetryConfigurationStore(_configPath).Load();
        var alias = Assert.Single(saved.Aliases);
        Assert.Equal("temperature.gpu.edge", alias.Key);
        Assert.Equal("browser-fixture:gpu:edge", alias.SensorId);
    }

    [Fact]
    public async Task AliasSaveWithoutRevisionDoesNotWriteAtRevisionZero()
    {
        using var client = CreateNoRedirectClient();
        var token = await GetAntiforgeryToken(client, "/aliases");

        var response = await PostForm(client, "/aliases?handler=Save", token, new Dictionary<string, string>
        {
            ["Input.Key"] = "temperature.gpu.edge",
            ["Input.DisplayName"] = "GPU Edge",
            ["Input.SensorId"] = "browser-fixture:gpu:edge"
        });
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("A valid configuration revision is required", html, StringComparison.Ordinal);
        Assert.Contains("GPU Edge", html, StringComparison.Ordinal);
        Assert.Empty(_factory.Services.GetRequiredService<SensorAliasRegistry>().GetDefinitions());
        Assert.Equal(0, _factory.Services.GetRequiredService<TelemetryConfigurationRegistry>().GetStatus().Revision);
        Assert.False(File.Exists(_configPath));
    }

    [Fact]
    public async Task AliasSaveWithMalformedRevisionDoesNotWrite()
    {
        using var client = CreateNoRedirectClient();
        var token = await GetAntiforgeryToken(client, "/aliases");

        var response = await PostForm(client, "/aliases?handler=Save", token, new Dictionary<string, string>
        {
            ["Input.Key"] = "temperature.gpu.edge",
            ["Input.DisplayName"] = "GPU Edge",
            ["Input.SensorId"] = "browser-fixture:gpu:edge",
            ["Input.Revision"] = "not-a-revision"
        });
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("A valid configuration revision is required", html, StringComparison.Ordinal);
        Assert.Contains("GPU Edge", html, StringComparison.Ordinal);
        Assert.Empty(_factory.Services.GetRequiredService<SensorAliasRegistry>().GetDefinitions());
        Assert.Equal(0, _factory.Services.GetRequiredService<TelemetryConfigurationRegistry>().GetStatus().Revision);
        Assert.False(File.Exists(_configPath));
    }

    [Fact]
    public async Task AliasDeleteWithoutRevisionDoesNotDelete()
    {
        SaveConfiguration(new SensorAliasDefinition(
            "temperature.gpu.edge", "GPU Edge", "browser-fixture:gpu:edge",
            QuantityKind.Temperature, UnitCode.Celsius, "hwmon"));
        using var client = CreateNoRedirectClient();
        var token = await GetAntiforgeryToken(client, "/aliases/temperature.gpu.edge");

        var response = await PostForm(
            client,
            "/aliases/temperature.gpu.edge?handler=Delete",
            token,
            new Dictionary<string, string> { ["ConfirmDelete"] = "true" });
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("A valid configuration revision is required", html, StringComparison.Ordinal);
        Assert.Single(new JsonTelemetryConfigurationStore(_configPath).Load().Aliases);
        Assert.Single(_factory.Services.GetRequiredService<SensorAliasRegistry>().GetDefinitions());
        Assert.Equal(0, _factory.Services.GetRequiredService<TelemetryConfigurationRegistry>().GetStatus().Revision);
    }

    [Fact]
    public async Task AliasDeleteWithMalformedRevisionDoesNotDelete()
    {
        SaveConfiguration(new SensorAliasDefinition(
            "temperature.gpu.edge", "GPU Edge", "browser-fixture:gpu:edge",
            QuantityKind.Temperature, UnitCode.Celsius, "hwmon"));
        using var client = CreateNoRedirectClient();
        var token = await GetAntiforgeryToken(client, "/aliases/temperature.gpu.edge");

        var response = await PostForm(
            client,
            "/aliases/temperature.gpu.edge?handler=Delete",
            token,
            new Dictionary<string, string>
            {
                ["ConfirmDelete"] = "true",
                ["Input.Revision"] = "not-a-revision"
            });
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("A valid configuration revision is required", html, StringComparison.Ordinal);
        Assert.Single(new JsonTelemetryConfigurationStore(_configPath).Load().Aliases);
        Assert.Single(_factory.Services.GetRequiredService<SensorAliasRegistry>().GetDefinitions());
        Assert.Equal(0, _factory.Services.GetRequiredService<TelemetryConfigurationRegistry>().GetStatus().Revision);
    }

    [Fact]
    public async Task QuantityChangingRebindRequiresServerConfirmationAndListsDependents()
    {
        SaveConfiguration(
            new SensorAliasDefinition(
                "temperature.gpu.edge", "GPU Edge", "browser-fixture:gpu:edge",
                QuantityKind.Temperature, UnitCode.Celsius, "hwmon"),
            new CalculatedSensorDefinition(
                "temperature.gpu.double", "Double GPU Temperature", "temperature.gpu.edge * 2",
                QuantityKind.Temperature, UnitCode.Celsius));
        using var client = CreateNoRedirectClient();
        var token = await GetAntiforgeryToken(client, "/aliases/temperature.gpu.edge");
        var values = new Dictionary<string, string>
        {
            ["Input.Key"] = "temperature.gpu.edge",
            ["Input.DisplayName"] = "GPU Power",
            ["Input.SensorId"] = "browser-fixture:gpu:power",
            ["Input.Revision"] = "0"
        };

        var warningResponse = await PostForm(
            client, "/aliases/temperature.gpu.edge?handler=Save", token, values);
        var warningHtml = await warningResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, warningResponse.StatusCode);
        Assert.Contains("Quantity or unit change", warningHtml, StringComparison.Ordinal);
        Assert.Contains("temperature.gpu.double", warningHtml, StringComparison.Ordinal);
        Assert.Equal("browser-fixture:gpu:edge", Assert.Single(
            new JsonTelemetryConfigurationStore(_configPath).Load().Aliases).SensorId);

        values["Input.ConfirmRebindImpact"] = "true";
        token = GetAntiforgeryToken(warningHtml);
        var savedResponse = await PostForm(
            client, "/aliases/temperature.gpu.edge?handler=Save", token, values);

        Assert.Equal(HttpStatusCode.Redirect, savedResponse.StatusCode);
        var saved = Assert.Single(new JsonTelemetryConfigurationStore(_configPath).Load().Aliases);
        Assert.Equal("browser-fixture:gpu:power", saved.SensorId);
        Assert.Equal(QuantityKind.Power, saved.Quantity);
        Assert.Equal(UnitCode.Watts, saved.Unit);
    }

    [Fact]
    public async Task DeleteRequiresConfirmationAndRetainsDependentCalculation()
    {
        SaveConfiguration(
            new SensorAliasDefinition(
                "temperature.gpu.edge", "GPU Edge", "browser-fixture:gpu:edge",
                QuantityKind.Temperature, UnitCode.Celsius, "hwmon"),
            new CalculatedSensorDefinition(
                "temperature.gpu.double", "Double GPU Temperature", "temperature.gpu.edge * 2",
                QuantityKind.Temperature, UnitCode.Celsius));
        using var client = CreateNoRedirectClient();
        var token = await GetAntiforgeryToken(client, "/aliases/temperature.gpu.edge");
        var values = new Dictionary<string, string>
        {
            ["Input.Revision"] = "0"
        };

        var warningResponse = await PostForm(
            client, "/aliases/temperature.gpu.edge?handler=Delete", token, values);
        var warningHtml = await warningResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, warningResponse.StatusCode);
        Assert.Contains("Deletion does not cascade", warningHtml, StringComparison.Ordinal);
        Assert.Contains("MissingDependency", warningHtml, StringComparison.Ordinal);
        Assert.Contains("temperature.gpu.double", warningHtml, StringComparison.Ordinal);
        Assert.Single(new JsonTelemetryConfigurationStore(_configPath).Load().Aliases);

        values["ConfirmDelete"] = "true";
        token = GetAntiforgeryToken(warningHtml);
        var deletedResponse = await PostForm(
            client, "/aliases/temperature.gpu.edge?handler=Delete", token, values);

        Assert.Equal(HttpStatusCode.Redirect, deletedResponse.StatusCode);
        var saved = new JsonTelemetryConfigurationStore(_configPath).Load();
        Assert.Empty(saved.Aliases);
        Assert.Equal("temperature.gpu.double", Assert.Single(saved.CalculatedSensors).Key);
    }

    [Fact]
    public async Task StaleAliasFormPreservesInputAndDoesNotOverwriteNewerConfiguration()
    {
        using var client = CreateNoRedirectClient();
        var token = await GetAntiforgeryToken(client, "/aliases");
        var apiResponse = await client.PutAsJsonAsync(
            "/api/aliases/temperature.gpu.edge",
            new { displayName = "GPU Edge", sensorId = "browser-fixture:gpu:edge" });
        apiResponse.EnsureSuccessStatusCode();

        var staleResponse = await PostForm(client, "/aliases?handler=Save", token, new Dictionary<string, string>
        {
            ["Input.Key"] = "power.gpu.board",
            ["Input.DisplayName"] = "Preserved board power",
            ["Input.SensorId"] = "browser-fixture:gpu:power",
            ["Input.Revision"] = "0"
        });
        var html = await staleResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, staleResponse.StatusCode);
        Assert.Contains("Reload and review", html, StringComparison.Ordinal);
        Assert.Contains("Preserved board power", html, StringComparison.Ordinal);
        var alias = Assert.Single(new JsonTelemetryConfigurationStore(_configPath).Load().Aliases);
        Assert.Equal("temperature.gpu.edge", alias.Key);
    }

    [Fact]
    public async Task MissingTargetAliasCanBeRenamedWithoutChangingStableTarget()
    {
        SaveConfiguration(new SensorAliasDefinition(
            "temperature.missing", "Missing Temperature", "fixture:missing",
            QuantityKind.Temperature, UnitCode.Celsius, "fixture"));
        using var client = CreateNoRedirectClient();
        var token = await GetAntiforgeryToken(client, "/aliases/temperature.missing");

        var response = await PostForm(
            client,
            "/aliases/temperature.missing?handler=Save",
            token,
            new Dictionary<string, string>
            {
                ["Input.Key"] = "temperature.missing",
                ["Input.DisplayName"] = "Renamed Missing Temperature",
                ["Input.SensorId"] = "fixture:missing",
                ["Input.Revision"] = "0"
            });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var alias = Assert.Single(new JsonTelemetryConfigurationStore(_configPath).Load().Aliases);
        Assert.Equal("Renamed Missing Temperature", alias.DisplayName);
        Assert.Equal("fixture:missing", alias.SensorId);
        Assert.Equal(QuantityKind.Temperature, alias.Quantity);
        Assert.Equal(UnitCode.Celsius, alias.Unit);
    }

    [Fact]
    public async Task PersistenceFailureLeavesActiveFileAndInMemoryAliasUnchanged()
    {
        using var client = CreateNoRedirectClient();
        var apiResponse = await client.PutAsJsonAsync(
            "/api/aliases/temperature.gpu.edge",
            new { displayName = "Original GPU Edge", sensorId = "browser-fixture:gpu:edge" });
        apiResponse.EnsureSuccessStatusCode();
        Directory.CreateDirectory($"{_configPath}.previous");
        var token = await GetAntiforgeryToken(client, "/aliases/temperature.gpu.edge");

        var response = await PostForm(
            client,
            "/aliases/temperature.gpu.edge?handler=Save",
            token,
            new Dictionary<string, string>
            {
                ["Input.Key"] = "temperature.gpu.edge",
                ["Input.DisplayName"] = "Replacement GPU Edge",
                ["Input.SensorId"] = "browser-fixture:gpu:edge",
                ["Input.Revision"] = "1"
            });
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("active file and in-memory configuration were unchanged", html, StringComparison.Ordinal);
        Assert.Equal("Original GPU Edge", Assert.Single(
            new JsonTelemetryConfigurationStore(_configPath).Load().Aliases).DisplayName);
        Assert.Equal("Original GPU Edge", Assert.Single(
            _factory.Services.GetRequiredService<SensorAliasRegistry>().GetDefinitions()).DisplayName);
        Assert.Equal(1, _factory.Services.GetRequiredService<TelemetryConfigurationRegistry>().GetStatus().Revision);
    }

    [Fact]
    public async Task CalculationValidateDoesNotWriteAndCreateUsesPostRedirectGet()
    {
        SaveConfiguration(new SensorAliasDefinition(
            "temperature.gpu.edge", "GPU Edge", "browser-fixture:gpu:edge",
            QuantityKind.Temperature, UnitCode.Celsius, "hwmon"));
        using var client = CreateNoRedirectClient();
        var fileBefore = File.ReadAllText(_configPath);
        var token = await GetAntiforgeryToken(client, "/calculations");
        var values = new Dictionary<string, string>
        {
            ["Input.Key"] = "temperature.gpu.scaled",
            ["Input.DisplayName"] = "<script>Scaled GPU</script>",
            ["Input.Formula"] = "temperature.gpu.edge * 2",
            ["Input.Revision"] = "0"
        };

        var validationResponse = await PostForm(
            client, "/calculations?handler=Validate", token, values);
        var validationHtml = await validationResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, validationResponse.StatusCode);
        Assert.Contains("Validation passed", validationHtml, StringComparison.Ordinal);
        Assert.Contains("Inferred Temperature", validationHtml, StringComparison.Ordinal);
        Assert.Contains("temperature.gpu.edge", validationHtml, StringComparison.Ordinal);
        Assert.Equal(fileBefore, File.ReadAllText(_configPath));
        Assert.Empty(_factory.Services.GetRequiredService<CalculatedSensorRegistry>().GetDefinitions());
        Assert.Equal(0, _factory.Services.GetRequiredService<TelemetryConfigurationRegistry>().GetStatus().Revision);
        Assert.False(File.Exists($"{_configPath}.previous"));

        token = GetAntiforgeryToken(validationHtml);
        var savedResponse = await PostForm(
            client, "/calculations?handler=Save", token, values);

        Assert.Equal(HttpStatusCode.Redirect, savedResponse.StatusCode);
        Assert.Equal("/calculations/temperature.gpu.scaled", savedResponse.Headers.Location?.OriginalString);
        var savedHtml = await client.GetStringAsync(savedResponse.Headers.Location);
        Assert.Contains("Saved calculated sensor", savedHtml, StringComparison.Ordinal);
        Assert.Contains("Status: Available", savedHtml, StringComparison.Ordinal);
        Assert.Contains("84 °C", WebUtility.HtmlDecode(savedHtml), StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;Scaled GPU&lt;/script&gt;", savedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>Scaled GPU</script>", savedHtml, StringComparison.Ordinal);
        Assert.Equal(1, _factory.Services.GetRequiredService<TelemetryConfigurationRegistry>().GetStatus().Revision);
    }

    [Fact]
    public async Task CalculationEditPreservesImmutableKeyAndSavesFormulaChanges()
    {
        SaveConfiguration(
            new SensorAliasDefinition(
                "temperature.gpu.edge", "GPU Edge", "browser-fixture:gpu:edge",
                QuantityKind.Temperature, UnitCode.Celsius, "hwmon"),
            new CalculatedSensorDefinition(
                "temperature.gpu.scaled", "Scaled GPU", "temperature.gpu.edge * 2",
                QuantityKind.Temperature, UnitCode.Celsius));
        using var client = CreateNoRedirectClient();
        var token = await GetAntiforgeryToken(client, "/calculations/temperature.gpu.scaled");

        var immutableResponse = await PostForm(
            client,
            "/calculations/temperature.gpu.scaled?handler=Save",
            token,
            new Dictionary<string, string>
            {
                ["Input.Key"] = "temperature.gpu.renamed",
                ["Input.DisplayName"] = "Renamed GPU",
                ["Input.Formula"] = "temperature.gpu.edge * 3",
                ["Input.Revision"] = "0"
            });
        var immutableHtml = await immutableResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, immutableResponse.StatusCode);
        Assert.Contains("keys are immutable", immutableHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("temperature.gpu.edge * 2", Assert.Single(
            new JsonTelemetryConfigurationStore(_configPath).Load().CalculatedSensors).Formula);

        token = GetAntiforgeryToken(immutableHtml);
        var savedResponse = await PostForm(
            client,
            "/calculations/temperature.gpu.scaled?handler=Save",
            token,
            new Dictionary<string, string>
            {
                ["Input.Key"] = "temperature.gpu.scaled",
                ["Input.DisplayName"] = "Scaled GPU x3",
                ["Input.Formula"] = "temperature.gpu.edge * 3",
                ["Input.Revision"] = "0"
            });

        Assert.Equal(HttpStatusCode.Redirect, savedResponse.StatusCode);
        var saved = Assert.Single(new JsonTelemetryConfigurationStore(_configPath).Load().CalculatedSensors);
        Assert.Equal("temperature.gpu.scaled", saved.Key);
        Assert.Equal("Scaled GPU x3", saved.DisplayName);
        Assert.Equal("temperature.gpu.edge * 3", saved.Formula);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-revision")]
    public async Task CalculationSaveRequiresValidSubmittedRevision(string? submittedRevision)
    {
        SaveConfiguration(new SensorAliasDefinition(
            "temperature.gpu.edge", "GPU Edge", "browser-fixture:gpu:edge",
            QuantityKind.Temperature, UnitCode.Celsius, "hwmon"));
        using var client = CreateNoRedirectClient();
        var token = await GetAntiforgeryToken(client, "/calculations");
        var values = new Dictionary<string, string>
        {
            ["Input.Key"] = "temperature.gpu.scaled",
            ["Input.DisplayName"] = "Scaled GPU",
            ["Input.Formula"] = "temperature.gpu.edge * 2"
        };
        if (submittedRevision is not null)
        {
            values["Input.Revision"] = submittedRevision;
        }

        var response = await PostForm(client, "/calculations?handler=Save", token, values);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("A valid configuration revision is required", html, StringComparison.Ordinal);
        Assert.Contains("Scaled GPU", html, StringComparison.Ordinal);
        Assert.Empty(_factory.Services.GetRequiredService<CalculatedSensorRegistry>().GetDefinitions());
        Assert.Equal(0, _factory.Services.GetRequiredService<TelemetryConfigurationRegistry>().GetStatus().Revision);
        Assert.Empty(new JsonTelemetryConfigurationStore(_configPath).Load().CalculatedSensors);
    }

    [Fact]
    public async Task StaleCalculationFormPreservesInputAndDoesNotOverwriteNewerConfiguration()
    {
        SaveConfiguration(
            new SensorAliasDefinition(
                "temperature.gpu.edge", "GPU Edge", "browser-fixture:gpu:edge",
                QuantityKind.Temperature, UnitCode.Celsius, "hwmon"),
            new CalculatedSensorDefinition(
                "temperature.gpu.scaled", "Scaled GPU", "temperature.gpu.edge * 2",
                QuantityKind.Temperature, UnitCode.Celsius));
        using var client = CreateNoRedirectClient();
        var token = await GetAntiforgeryToken(client, "/calculations/temperature.gpu.scaled");
        var apiResponse = await client.PutAsJsonAsync(
            "/api/calculations/temperature.gpu.other",
            new { displayName = "Other GPU", formula = "temperature.gpu.edge * 4" });
        apiResponse.EnsureSuccessStatusCode();

        var staleResponse = await PostForm(
            client,
            "/calculations/temperature.gpu.scaled?handler=Save",
            token,
            new Dictionary<string, string>
            {
                ["Input.Key"] = "temperature.gpu.scaled",
                ["Input.DisplayName"] = "Preserved scaled GPU",
                ["Input.Formula"] = "temperature.gpu.edge * 3",
                ["Input.Revision"] = "0"
            });
        var html = await staleResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, staleResponse.StatusCode);
        Assert.Contains("Reload and review", html, StringComparison.Ordinal);
        Assert.Contains("Preserved scaled GPU", html, StringComparison.Ordinal);
        var definitions = new JsonTelemetryConfigurationStore(_configPath).Load().CalculatedSensors;
        Assert.Equal(2, definitions.Count);
        Assert.Equal("temperature.gpu.edge * 2", definitions.Single(
            definition => definition.Key == "temperature.gpu.scaled").Formula);
    }

    [Fact]
    public async Task CalculationPersistenceFailureLeavesFileAndRegistryUnchanged()
    {
        SaveConfiguration(
            new SensorAliasDefinition(
                "temperature.gpu.edge", "GPU Edge", "browser-fixture:gpu:edge",
                QuantityKind.Temperature, UnitCode.Celsius, "hwmon"),
            new CalculatedSensorDefinition(
                "temperature.gpu.scaled", "Original Scaled GPU", "temperature.gpu.edge * 2",
                QuantityKind.Temperature, UnitCode.Celsius));
        using var client = CreateNoRedirectClient();
        var token = await GetAntiforgeryToken(client, "/calculations/temperature.gpu.scaled");
        Directory.CreateDirectory($"{_configPath}.previous");

        var response = await PostForm(
            client,
            "/calculations/temperature.gpu.scaled?handler=Save",
            token,
            new Dictionary<string, string>
            {
                ["Input.Key"] = "temperature.gpu.scaled",
                ["Input.DisplayName"] = "Replacement Scaled GPU",
                ["Input.Formula"] = "temperature.gpu.edge * 3",
                ["Input.Revision"] = "0"
            });
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("active file and in-memory configuration were unchanged", html, StringComparison.Ordinal);
        Assert.Equal("Original Scaled GPU", Assert.Single(
            new JsonTelemetryConfigurationStore(_configPath).Load().CalculatedSensors).DisplayName);
        Assert.Equal("Original Scaled GPU", _factory.Services
            .GetRequiredService<CalculatedSensorRegistry>()
            .GetDefinition("temperature.gpu.scaled")?.DisplayName);
        Assert.Equal(0, _factory.Services.GetRequiredService<TelemetryConfigurationRegistry>().GetStatus().Revision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-revision")]
    public async Task CalculationDeleteRequiresValidSubmittedRevision(string? submittedRevision)
    {
        SaveConfiguration(
            new SensorAliasDefinition(
                "temperature.gpu.edge", "GPU Edge", "browser-fixture:gpu:edge",
                QuantityKind.Temperature, UnitCode.Celsius, "hwmon"),
            new CalculatedSensorDefinition(
                "temperature.gpu.scaled", "Scaled GPU", "temperature.gpu.edge * 2",
                QuantityKind.Temperature, UnitCode.Celsius));
        using var client = CreateNoRedirectClient();
        var token = await GetAntiforgeryToken(client, "/calculations/temperature.gpu.scaled");
        var values = new Dictionary<string, string> { ["ConfirmDelete"] = "true" };
        if (submittedRevision is not null)
        {
            values["Input.Revision"] = submittedRevision;
        }

        var response = await PostForm(
            client, "/calculations/temperature.gpu.scaled?handler=Delete", token, values);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("A valid configuration revision is required", html, StringComparison.Ordinal);
        Assert.NotNull(_factory.Services.GetRequiredService<CalculatedSensorRegistry>()
            .GetDefinition("temperature.gpu.scaled"));
        Assert.Single(new JsonTelemetryConfigurationStore(_configPath).Load().CalculatedSensors);
        Assert.Equal(0, _factory.Services.GetRequiredService<TelemetryConfigurationRegistry>().GetStatus().Revision);
    }

    [Fact]
    public async Task CalculationDeleteIsConfirmedNonCascadingAndDependentBecomesMissing()
    {
        SaveConfiguration(
            [new SensorAliasDefinition(
                "temperature.gpu.edge", "GPU Edge", "browser-fixture:gpu:edge",
                QuantityKind.Temperature, UnitCode.Celsius, "hwmon")],
            [
                new CalculatedSensorDefinition(
                    "temperature.gpu.base", "Base GPU", "temperature.gpu.edge * 2",
                    QuantityKind.Temperature, UnitCode.Celsius),
                new CalculatedSensorDefinition(
                    "temperature.gpu.dependent", "Dependent GPU", "temperature.gpu.base * 2",
                    QuantityKind.Temperature, UnitCode.Celsius)
            ]);
        using var client = CreateNoRedirectClient();
        var token = await GetAntiforgeryToken(client, "/calculations/temperature.gpu.base");
        var values = new Dictionary<string, string> { ["Input.Revision"] = "0" };

        var warningResponse = await PostForm(
            client, "/calculations/temperature.gpu.base?handler=Delete", token, values);
        var warningHtml = await warningResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, warningResponse.StatusCode);
        Assert.Contains("Deletion does not cascade", warningHtml, StringComparison.Ordinal);
        Assert.Contains("temperature.gpu.dependent", warningHtml, StringComparison.Ordinal);

        values["ConfirmDelete"] = "true";
        token = GetAntiforgeryToken(warningHtml);
        var deletedResponse = await PostForm(
            client, "/calculations/temperature.gpu.base?handler=Delete", token, values);

        Assert.Equal(HttpStatusCode.Redirect, deletedResponse.StatusCode);
        var saved = new JsonTelemetryConfigurationStore(_configPath).Load();
        Assert.Equal("temperature.gpu.dependent", Assert.Single(saved.CalculatedSensors).Key);
        var dependent = _factory.Services.GetRequiredService<CalculatedSensorCatalog>()
            .GetSensorByAlias("temperature.gpu.dependent");
        Assert.Equal(SensorStatus.MissingDependency, dependent?.Status);
        var dependentHtml = await client.GetStringAsync("/calculations/temperature.gpu.dependent");
        Assert.Contains("MissingDependency", dependentHtml, StringComparison.Ordinal);
        Assert.Contains("temperature.gpu.base", dependentHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CalculationValidationShowsDependencyWarningsAndEncodesFormulaErrors()
    {
        SaveConfiguration(new SensorAliasDefinition(
            "temperature.gpu.stale", "Stale GPU", "browser-fixture:gpu:junction",
            QuantityKind.Temperature, UnitCode.Celsius, "hwmon"));
        using var client = CreateNoRedirectClient();
        var token = await GetAntiforgeryToken(client, "/calculations");
        var validValues = new Dictionary<string, string>
        {
            ["Input.Key"] = "temperature.gpu.warning",
            ["Input.DisplayName"] = "Warning GPU",
            ["Input.Formula"] = "temperature.gpu.stale * 2",
            ["Input.Revision"] = "0"
        };

        var warningResponse = await PostForm(
            client, "/calculations?handler=Validate", token, validValues);
        var warningHtml = await warningResponse.Content.ReadAsStringAsync();
        Assert.Contains("Current dependency state may block a reading", warningHtml, StringComparison.Ordinal);
        Assert.Contains("Stale", warningHtml, StringComparison.Ordinal);

        token = GetAntiforgeryToken(warningHtml);
        validValues["Input.Formula"] = "temperature.gpu.stale + <script>alert(1)</script>";
        var errorResponse = await PostForm(
            client, "/calculations?handler=Validate", token, validValues);
        var errorHtml = await errorResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, errorResponse.StatusCode);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", errorHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert(1)</script>", errorHtml, StringComparison.Ordinal);
        Assert.Empty(_factory.Services.GetRequiredService<CalculatedSensorRegistry>().GetDefinitions());
    }

    [Fact]
    public async Task CalculationPageShowsAuthoritativeCalculationErrorDetail()
    {
        SaveConfiguration(
            new SensorAliasDefinition(
                "power.gpu.board", "GPU Board Power", "browser-fixture:gpu:power",
                QuantityKind.Power, UnitCode.Watts, "hwmon"),
            new CalculatedSensorDefinition(
                "temperature.bad.metadata", "Bad <metadata>", "power.gpu.board * 2",
                QuantityKind.Temperature, UnitCode.Celsius));
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/calculations/temperature.bad.metadata");

        Assert.Contains("CalculationError", html, StringComparison.Ordinal);
        Assert.Contains("result type changed", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Bad &lt;metadata&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Bad <metadata>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CalculationWritesRequireAntiforgeryToken()
    {
        using var client = CreateNoRedirectClient();

        var response = await client.PostAsync(
            "/calculations?handler=Save",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Key"] = "value.scalar",
                ["Input.DisplayName"] = "Scalar",
                ["Input.Formula"] = "1",
                ["Input.Revision"] = "0"
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.Services.GetRequiredService<CalculatedSensorRegistry>().GetDefinitions());
    }

    [Fact]
    public async Task SensorsPageLinksPhysicalReadingsToAliasWorkflows()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/sensors");

        Assert.Contains("/aliases?targetId=browser-fixture%3Agpu%3Aedge", html, StringComparison.Ordinal);
        Assert.Contains("Create alias", html, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private HttpClient CreateNoRedirectClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    private static async Task<string> GetAntiforgeryToken(HttpClient client, string path) =>
        GetAntiforgeryToken(await client.GetStringAsync(path));

    private static string GetAntiforgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, "The response did not contain an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static Task<HttpResponseMessage> PostForm(
        HttpClient client,
        string path,
        string token,
        IReadOnlyDictionary<string, string> values)
    {
        var fields = values
            .Append(new KeyValuePair<string, string>("__RequestVerificationToken", token));
        return client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    private void SaveConfiguration(
        SensorAliasDefinition alias,
        CalculatedSensorDefinition? calculation = null)
    {
        new JsonTelemetryConfigurationStore(_configPath).Save(new TelemetryConfigurationDocument
        {
            Aliases = [alias],
            CalculatedSensors = calculation is null ? [] : [calculation]
        });
    }

    private void SaveConfiguration(
        IReadOnlyList<SensorAliasDefinition> aliases,
        IReadOnlyList<CalculatedSensorDefinition> calculations)
    {
        new JsonTelemetryConfigurationStore(_configPath).Save(new TelemetryConfigurationDocument
        {
            Aliases = [.. aliases],
            CalculatedSensors = [.. calculations]
        });
    }

    private sealed class BrowserFixtureSensorSource : ISensorSource
    {
        private static readonly DateTimeOffset Timestamp = DateTimeOffset.Parse("2026-08-07T12:00:00Z");
        private static readonly IReadOnlyList<SensorReading> Readings =
        [
            Reading("browser-fixture:gpu:edge", "edge", 42, SensorStatus.Available, "amdgpu", "gpu-1"),
            Reading("browser-fixture:gpu:junction", "junction", 55, SensorStatus.Stale, "amdgpu", "gpu-1"),
            Reading("browser-fixture:gpu:power", "power1", 180, SensorStatus.Available, "amdgpu", "gpu-1", QuantityKind.Power, UnitCode.Watts, "W"),
            Reading("browser-fixture:unsafe", "<script>unsafe()</script>", null, SensorStatus.Unavailable, "unsafe", "unsafe-1")
        ];

        public string Name => "hwmon";
        public IReadOnlyList<SensorReading> GetSensors() => Readings;
        public SensorReading? GetSensor(string id) => Readings.FirstOrDefault(reading => reading.Id == id);

        private static SensorReading Reading(
            string id,
            string rawLabel,
            double? value,
            SensorStatus status,
            string driver,
            string deviceKey,
            QuantityKind quantity = QuantityKind.Temperature,
            UnitCode unit = UnitCode.Celsius,
            string unitSymbol = "°C") =>
            new(
                id,
                rawLabel,
                null,
                value,
                quantity,
                unit,
                unitSymbol,
                "hwmon",
                status,
                value is null ? null : Timestamp,
                new Dictionary<string, string>
                {
                    ["driver"] = driver,
                    ["deviceKey"] = deviceKey,
                    ["measurement"] = "input"
                });
    }
}

using System.Text.Json;
using TelemetryLoom.Contracts.Aliases;
using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Core.Aliases;
using TelemetryLoom.Core.Configuration;

namespace TelemetryLoom.Core.Tests;

public sealed class JsonTelemetryConfigurationStoreTests
{
    [Fact]
    public void FirstSaveCreatesActiveWithoutPreviousAndCleansTemporaryFile()
    {
        var root = CreateTemporaryDirectory();
        var path = Path.Combine(root, "nested", "config.json");

        try
        {
            var store = new JsonTelemetryConfigurationStore(path);
            store.Save(new TelemetryConfigurationDocument { Aliases = [
                CreateAlias("z.last", "sensor:z"),
                CreateAlias("a.first", "sensor:a")
            ] });

            var loaded = store.Load();
            using var json = JsonDocument.Parse(File.ReadAllText(path));

            Assert.Equal(TelemetryConfigurationDocument.CurrentSchemaVersion, json.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(["a.first", "z.last"], loaded.Aliases.Select(alias => alias.Key));
            Assert.False(File.Exists(store.PreviousPath));
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SubsequentSavesReplacePreviousWithPriorActiveConfiguration()
    {
        var root = CreateTemporaryDirectory();
        var path = Path.Combine(root, "config.json");

        try
        {
            var store = new JsonTelemetryConfigurationStore(path);
            store.Save(new TelemetryConfigurationDocument { Aliases = [CreateAlias("first.alias", "sensor:first")] });
            store.Save(new TelemetryConfigurationDocument { Aliases = [CreateAlias("second.alias", "sensor:second")] });

            Assert.Equal("second.alias", Assert.Single(store.Load().Aliases).Key);
            Assert.Equal("first.alias", Assert.Single(new JsonTelemetryConfigurationStore(store.PreviousPath).Load().Aliases).Key);

            store.Save(new TelemetryConfigurationDocument { Aliases = [CreateAlias("third.alias", "sensor:third")] });

            Assert.Equal("third.alias", Assert.Single(store.Load().Aliases).Key);
            Assert.Equal("second.alias", Assert.Single(new JsonTelemetryConfigurationStore(store.PreviousPath).Load().Aliases).Key);
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MalformedActiveConfigurationReportsPreviousWithoutLoadingIt()
    {
        var root = CreateTemporaryDirectory();
        var path = Path.Combine(root, "config.json");

        try
        {
            var store = new JsonTelemetryConfigurationStore(path);
            store.Save(new TelemetryConfigurationDocument { Aliases = [CreateAlias("first.alias", "sensor:first")] });
            store.Save(new TelemetryConfigurationDocument { Aliases = [CreateAlias("second.alias", "sensor:second")] });
            File.WriteAllText(path, "{ malformed");

            var exception = Assert.Throws<InvalidDataException>(() => store.Load());

            Assert.Contains(store.PreviousPath, exception.Message, StringComparison.Ordinal);
            Assert.Contains("restored deliberately", exception.Message, StringComparison.Ordinal);
            Assert.Equal("first.alias", Assert.Single(new JsonTelemetryConfigurationStore(store.PreviousPath).Load().Aliases).Key);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsUnsupportedSchemaVersion()
    {
        var root = CreateTemporaryDirectory();
        var path = Path.Combine(root, "config.json");

        try
        {
            File.WriteAllText(path, """{"schemaVersion":99,"aliases":[]}""");

            var exception = Assert.Throws<InvalidDataException>(() => new JsonTelemetryConfigurationStore(path).Load());

            Assert.Contains("Unsupported telemetry configuration schema 99", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static SensorAliasDefinition CreateAlias(string key, string sensorId) =>
        new(key, key, sensorId, QuantityKind.Temperature, UnitCode.Celsius, "fixture");

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "telemetry-loom-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

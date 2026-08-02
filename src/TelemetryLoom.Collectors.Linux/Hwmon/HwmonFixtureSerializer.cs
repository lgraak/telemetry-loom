using System.Text.Json;
using System.Text.Json.Serialization;

namespace TelemetryLoom.Collectors.Linux.Hwmon;

public static class HwmonFixtureSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static HwmonFixture Load(string path)
    {
        var fixture = JsonSerializer.Deserialize<HwmonFixture>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException($"Fixture is empty: {path}");

        if (fixture.SchemaVersion != HwmonFixture.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported hwmon fixture schema {fixture.SchemaVersion}; expected {HwmonFixture.CurrentSchemaVersion}.");
        }

        return fixture;
    }

    public static void Save(string path, HwmonFixture fixture)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(fixture, Options));
    }
}

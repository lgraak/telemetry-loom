using System.Text.Json;
using System.Text.Json.Serialization;
using TelemetryLoom.Contracts.Aliases;

namespace TelemetryLoom.Core.Aliases;

public sealed class JsonAliasConfigurationStore(string path) : IAliasConfigurationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Path { get; } = System.IO.Path.GetFullPath(path);

    public IReadOnlyList<SensorAliasDefinition> Load()
    {
        if (!File.Exists(Path))
        {
            return [];
        }

        var document = JsonSerializer.Deserialize<AliasConfigurationDocument>(File.ReadAllText(Path), SerializerOptions)
            ?? throw new InvalidDataException($"Alias configuration is empty: {Path}");

        if (document.SchemaVersion != AliasConfigurationDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported alias configuration schema {document.SchemaVersion}; " +
                $"expected {AliasConfigurationDocument.CurrentSchemaVersion}.");
        }

        return document.Aliases
            ?? throw new InvalidDataException($"Alias configuration has a null aliases collection: {Path}");
    }

    public void Save(IReadOnlyCollection<SensorAliasDefinition> aliases)
    {
        var directory = System.IO.Path.GetDirectoryName(Path)
            ?? throw new InvalidOperationException($"Configuration path has no parent directory: {Path}");
        Directory.CreateDirectory(directory);

        var temporaryPath = System.IO.Path.Combine(
            directory,
            $".{System.IO.Path.GetFileName(Path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            var document = new AliasConfigurationDocument
            {
                Aliases = [.. aliases.OrderBy(alias => alias.Key, StringComparer.Ordinal)]
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions);

            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

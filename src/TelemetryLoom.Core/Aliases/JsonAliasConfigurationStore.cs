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
    public string PreviousPath => $"{Path}.previous";

    public IReadOnlyList<SensorAliasDefinition> Load()
    {
        if (!File.Exists(Path))
        {
            return [];
        }

        try
        {
            return DeserializeDocument(File.ReadAllBytes(Path), Path).Aliases;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            var recovery = File.Exists(PreviousPath)
                ? $" A previous configuration exists at '{PreviousPath}' and can be restored deliberately."
                : string.Empty;
            throw new InvalidDataException(
                $"Alias configuration is invalid: {Path}. {exception.Message}{recovery}",
                exception);
        }
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
            _ = DeserializeDocument(bytes, "serialized alias configuration");

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

            _ = DeserializeDocument(File.ReadAllBytes(temporaryPath), temporaryPath);

            if (File.Exists(Path))
            {
                File.Replace(temporaryPath, Path, PreviousPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, Path);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static AliasConfigurationDocument DeserializeDocument(ReadOnlySpan<byte> bytes, string source)
    {
        var document = JsonSerializer.Deserialize<AliasConfigurationDocument>(bytes, SerializerOptions)
            ?? throw new InvalidDataException($"Alias configuration is empty: {source}");

        if (document.SchemaVersion != AliasConfigurationDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported alias configuration schema {document.SchemaVersion}; " +
                $"expected {AliasConfigurationDocument.CurrentSchemaVersion}.");
        }

        if (document.Aliases is null)
        {
            throw new InvalidDataException($"Alias configuration has a null aliases collection: {source}");
        }

        return document;
    }
}

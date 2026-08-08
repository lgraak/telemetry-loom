using System.Text.Json;
using System.Text.Json.Serialization;

namespace TelemetryLoom.Core.Configuration;

public sealed class JsonTelemetryConfigurationStore(string path) :
    ITelemetryConfigurationStore,
    ITelemetryConfigurationStoreMetadata
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Path { get; } = System.IO.Path.GetFullPath(path);
    public string ActivePath => Path;
    public string PreviousPath => $"{Path}.previous";
    public bool PreviousExists => File.Exists(PreviousPath);

    public TelemetryConfigurationDocument Load()
    {
        if (!File.Exists(Path))
        {
            return new TelemetryConfigurationDocument();
        }

        try
        {
            return DeserializeDocument(File.ReadAllBytes(Path), Path);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            var recovery = File.Exists(PreviousPath)
                ? $" A previous configuration exists at '{PreviousPath}' and can be restored deliberately."
                : string.Empty;
            throw new InvalidDataException(
                $"Telemetry configuration is invalid: {Path}. {exception.Message}{recovery}",
                exception);
        }
    }

    public void Save(TelemetryConfigurationDocument document)
    {
        var directory = System.IO.Path.GetDirectoryName(Path)
            ?? throw new InvalidOperationException($"Configuration path has no parent directory: {Path}");
        Directory.CreateDirectory(directory);

        var temporaryPath = System.IO.Path.Combine(
            directory,
            $".{System.IO.Path.GetFileName(Path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            var normalized = document with
            {
                SchemaVersion = TelemetryConfigurationDocument.CurrentSchemaVersion,
                Aliases = [.. document.Aliases.OrderBy(alias => alias.Key, StringComparer.Ordinal)],
                CalculatedSensors = [.. document.CalculatedSensors.OrderBy(sensor => sensor.Key, StringComparer.Ordinal)]
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(normalized, SerializerOptions);
            _ = DeserializeDocument(bytes, "serialized telemetry configuration");

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

    private static TelemetryConfigurationDocument DeserializeDocument(ReadOnlySpan<byte> bytes, string source)
    {
        var document = JsonSerializer.Deserialize<TelemetryConfigurationDocument>(bytes, SerializerOptions)
            ?? throw new InvalidDataException($"Telemetry configuration is empty: {source}");

        if (document.SchemaVersion != TelemetryConfigurationDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported telemetry configuration schema {document.SchemaVersion}; " +
                $"expected {TelemetryConfigurationDocument.CurrentSchemaVersion}.");
        }

        if (document.Aliases is null || document.CalculatedSensors is null)
        {
            throw new InvalidDataException($"Telemetry configuration has a null collection: {source}");
        }

        return document;
    }
}

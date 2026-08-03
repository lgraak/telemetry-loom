using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Contracts.Streaming;
using TelemetryLoom.Core.Aliases;
using TelemetryLoom.Core.Calculations;
using TelemetryLoom.Core.Configuration;
using TelemetryLoom.Core.Sensors;
using TelemetryLoom.Core.Streaming;

namespace TelemetryLoom.Core.Tests;

public sealed class SensorSnapshotStreamTests
{
    [Fact]
    public async Task DeliversImmediateVersionedSnapshotsWithMonotonicSequence()
    {
        var stream = CreateStream();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var enumerator = stream
            .ReadAllAsync(TimeSpan.FromMilliseconds(10), cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        Assert.True(await enumerator.MoveNextAsync());
        var first = enumerator.Current;
        Assert.True(await enumerator.MoveNextAsync());
        var second = enumerator.Current;

        Assert.Equal(SensorSnapshot.CurrentSchemaVersion, first.SchemaVersion);
        Assert.Equal(first.Sequence + 1, second.Sequence);
        Assert.Single(first.Sensors);
        Assert.Equal("fixture:temperature", first.Sensors[0].Id);
        Assert.True(second.CapturedAt >= first.CapturedAt);
    }

    [Fact]
    public async Task CancellationStopsAWaitingStream()
    {
        var stream = CreateStream();
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = stream
            .ReadAllAsync(TimeSpan.FromSeconds(30), cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        Assert.True(await enumerator.MoveNextAsync());
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await enumerator.MoveNextAsync().AsTask());
    }

    [Fact]
    public async Task RejectsNonPositiveIntervals()
    {
        var stream = CreateStream();
        await using var enumerator = stream.ReadAllAsync(TimeSpan.Zero).GetAsyncEnumerator();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await enumerator.MoveNextAsync().AsTask());
    }

    private static SensorSnapshotStream CreateStream()
    {
        var sensors = new SensorCatalog([new FixtureSource()]);
        var configuration = new TelemetryConfigurationRegistry(new MemoryConfigurationStore());
        var aliases = new SensorAliasRegistry(sensors, configuration);
        var physical = new AliasedSensorCatalog(sensors, aliases);
        var calculations = new CalculatedSensorRegistry(configuration, aliases);
        var calculated = new CalculatedSensorCatalog(physical, calculations);
        return new SensorSnapshotStream(
            new TelemetrySensorCatalog(physical, calculated),
            TimeProvider.System);
    }

    private sealed class FixtureSource : ISensorSource
    {
        private static readonly SensorReading Reading = new(
            "fixture:temperature", "Temperature", null, 20, QuantityKind.Temperature,
            UnitCode.Celsius, "°C", "fixture", SensorStatus.Available,
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"), new Dictionary<string, string>());

        public string Name => "fixture";
        public IReadOnlyList<SensorReading> GetSensors() => [Reading];
        public SensorReading? GetSensor(string id) => id == Reading.Id ? Reading : null;
    }

    private sealed class MemoryConfigurationStore : ITelemetryConfigurationStore
    {
        public TelemetryConfigurationDocument Load() => new();
        public void Save(TelemetryConfigurationDocument document) { }
    }
}

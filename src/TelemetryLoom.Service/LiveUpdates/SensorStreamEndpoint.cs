using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace TelemetryLoom.Service.LiveUpdates;

public static class SensorStreamEndpoint
{
    public static async Task Stream(
        HttpContext context,
        SensorSnapshotPublisher snapshots,
        IOptions<JsonOptions> jsonOptions)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache, no-transform";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await foreach (var snapshot in snapshots.Subscribe(context.RequestAborted))
            {
                var json = JsonSerializer.Serialize(snapshot, jsonOptions.Value.SerializerOptions);
                await context.Response.WriteAsync(
                    $"id: {snapshot.Sequence}\nevent: sensors\ndata: {json}\n\n",
                    context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client disconnected. Request cancellation is normal stream termination.
        }
    }
}

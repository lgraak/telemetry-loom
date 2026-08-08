using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace TelemetryLoom.Core.Tests;

internal sealed class TestRemoteAddressStartupFilter : IStartupFilter
{
    public const string HeaderName = "X-TelemetryLoom-Test-Remote-IP";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            var configuredAddress = context.Request.Headers[HeaderName].SingleOrDefault();
            context.Connection.RemoteIpAddress = string.IsNullOrWhiteSpace(configuredAddress)
                ? IPAddress.Loopback
                : IPAddress.Parse(configuredAddress);
            await nextMiddleware();
        });
        next(app);
    };
}

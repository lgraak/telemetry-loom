using System.Net;

namespace TelemetryLoom.Service.Access;

public enum RequestAuthority
{
    Denied,
    RemoteRead,
    LocalAdmin
}

public sealed class RequestAuthorityClassifier(AccessPolicy policy)
{
    public RequestAuthority Classify(HttpContext context) =>
        Classify(context.Connection.RemoteIpAddress);

    public RequestAuthority Classify(IPAddress? remoteAddress)
    {
        if (remoteAddress is null)
        {
            return RequestAuthority.Denied;
        }

        if (remoteAddress.IsIPv4MappedToIPv6)
        {
            remoteAddress = remoteAddress.MapToIPv4();
        }

        if (IPAddress.IsLoopback(remoteAddress))
        {
            return RequestAuthority.LocalAdmin;
        }

        return policy.Mode == AccessMode.LanReadOnly
            ? RequestAuthority.RemoteRead
            : RequestAuthority.Denied;
    }
}

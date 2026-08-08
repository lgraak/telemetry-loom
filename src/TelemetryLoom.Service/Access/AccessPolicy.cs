using System.Net;

namespace TelemetryLoom.Service.Access;

public sealed class AccessPolicy
{
    private AccessPolicy(AccessMode mode, IPAddress configuredAddress, int port)
    {
        Mode = mode;
        ConfiguredAddress = configuredAddress;
        Port = port;
        ListenAddresses = mode == AccessMode.LocalOnly
            ? [configuredAddress]
            : [IPAddress.Loopback, configuredAddress];
        ListenUrls = ListenAddresses.Select(address => FormatUrl(address, port)).ToArray();
    }

    public AccessMode Mode { get; }
    public IPAddress ConfiguredAddress { get; }
    public int Port { get; }
    public IReadOnlyList<IPAddress> ListenAddresses { get; }
    public IReadOnlyList<string> ListenUrls { get; }
    public bool RemoteClientsReadOnly => Mode == AccessMode.LanReadOnly;

    public static AccessPolicy FromConfiguration(IConfiguration configuration)
    {
        if (configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
        {
            throw new InvalidOperationException(
                "Kestrel endpoint configuration is not supported. Configure TelemetryLoom:Access so the product access policy remains authoritative.");
        }

        AccessOptions options;
        try
        {
            options = configuration.GetSection(AccessOptions.SectionName).Get<AccessOptions>() ?? new AccessOptions();
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"Invalid {AccessOptions.SectionName} configuration: {exception.Message}",
                exception);
        }

        return Create(options);
    }

    public static AccessPolicy Create(AccessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!Enum.IsDefined(options.Mode))
        {
            throw new InvalidOperationException($"Unsupported access mode: {options.Mode}");
        }

        if (options.Port is < 1 or > 65535)
        {
            throw new InvalidOperationException("TelemetryLoom:Access:Port must be between 1 and 65535.");
        }

        if (!IPAddress.TryParse(options.ListenAddress, out var address))
        {
            throw new InvalidOperationException(
                $"TelemetryLoom:Access:ListenAddress is not a valid IP address: {options.ListenAddress}");
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IsWildcardOrMulticast(address))
        {
            throw new InvalidOperationException(
                "TelemetryLoom:Access:ListenAddress must be one specific unicast IP address; wildcard and multicast addresses are not supported.");
        }

        if (options.Mode == AccessMode.LocalOnly && !IPAddress.IsLoopback(address))
        {
            throw new InvalidOperationException(
                "LocalOnly requires a loopback ListenAddress so local administrative reachability is preserved without LAN exposure.");
        }

        if (options.Mode == AccessMode.LanReadOnly && IPAddress.IsLoopback(address))
        {
            throw new InvalidOperationException(
                "LanReadOnly requires an explicit non-loopback ListenAddress; loopback remains bound separately for local administration.");
        }

        return new AccessPolicy(options.Mode, address, options.Port);
    }

    private static bool IsWildcardOrMulticast(IPAddress address)
    {
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.Broadcast) || address.Equals(IPAddress.None))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            ? bytes[0] is >= 224 and <= 239
            : bytes[0] == 0xff;
    }

    private static string FormatUrl(IPAddress address, int port) =>
        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"http://[{address}]:{port}"
            : $"http://{address}:{port}";
}

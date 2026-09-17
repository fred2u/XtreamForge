using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;
using XtreamForge.Categories;
using XtreamForge.Data;
using XtreamForge.Source;

namespace XtreamForge.Xtream;

public sealed class XtreamSourceDiscoveryService(
    XtreamUpstreamDestinationResolver destinationResolver,
    XtreamUpstreamClient upstreamClient,
    SourceService sourceService)
{
    private readonly XtreamUpstreamClient _upstreamClient = upstreamClient;

    public async Task<XtreamSourceDiscoveryResult> DiscoverAsync(
        XtreamSourceDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var destination = ParseAndValidateDestination(request);
        var vodCategories = await FetchCategoriesAsync(destination, request.Username!, request.Password!, "get_vod_categories", cancellationToken);
        var seriesCategories = await FetchCategoriesAsync(destination, request.Username!, request.Password!, "get_series_categories", cancellationToken);
        var sourceDescriptor = new XtreamSourceDescriptor(destination.Protocol, destination.Host, destination.Port);

        var vodSynchronization = await sourceService.SynchronizeCategoriesAsync(
            sourceDescriptor,
            ContentType.Vod,
            vodCategories,
            cancellationToken);
        await sourceService.SynchronizeCategoriesAsync(
            sourceDescriptor,
            ContentType.Series,
            seriesCategories,
            cancellationToken);

        return new XtreamSourceDiscoveryResult(vodSynchronization.SourceId, vodCategories.Count, seriesCategories.Count);
    }

    private XtreamValidatedSourceDestination ParseAndValidateDestination(XtreamSourceDiscoveryRequest request)
    {
        var protocol = NormalizeProtocol(request.Protocol);
        var normalizedInput = request.HostOrBaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedInput))
        {
            throw new InvalidOperationException("Host or base URL is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Username))
        {
            throw new InvalidOperationException("Username is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new InvalidOperationException("Password is required.");
        }

        if (!TryParseInputUri(protocol, normalizedInput, out var parsedUri))
        {
            throw new InvalidOperationException("Enter a valid host or base URL.");
        }

        if (!string.IsNullOrWhiteSpace(parsedUri.UserInfo))
        {
            throw new InvalidOperationException("Credentials must be entered separately.");
        }

        if (!string.IsNullOrWhiteSpace(parsedUri.Query) || !string.IsNullOrWhiteSpace(parsedUri.Fragment))
        {
            throw new InvalidOperationException("Host or base URL must not include a query string or fragment.");
        }

        if (!parsedUri.Scheme.Equals(protocol, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Protocol must match the host or base URL.");
        }

        int? embeddedPort = parsedUri.IsDefaultPort ? null : parsedUri.Port;
        if (request.Port is int requestedPort && embeddedPort is int uriPort && requestedPort != uriPort)
        {
            throw new InvalidOperationException("Port must match the host or base URL.");
        }

        var port = request.Port ?? embeddedPort ?? GetDefaultPort(protocol);
        if (port is < 1 or > 65535)
        {
            throw new InvalidOperationException("Port must be between 1 and 65535.");
        }

        if (Uri.CheckHostName(parsedUri.Host) is not (UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6))
        {
            throw new InvalidOperationException("Enter a valid host.");
        }

        var rest = BuildPlayerApiRest(parsedUri.AbsolutePath);
        var resolution = destinationResolver.Resolve(protocol, parsedUri.Host, port.ToString(CultureInfo.InvariantCulture), rest, QueryString.Empty);
        if (!resolution.IsValid || resolution.Destination is null)
        {
            throw new InvalidOperationException(resolution.Error ?? "The source destination is invalid.");
        }

        return new XtreamValidatedSourceDestination(protocol, parsedUri.Host.ToLowerInvariant(), port, rest, resolution.Destination.TargetUri);
    }

    private async Task<IReadOnlyList<DiscoveredCategory>> FetchCategoriesAsync(
        XtreamValidatedSourceDestination destination,
        string username,
        string password,
        string action,
        CancellationToken cancellationToken)
    {
        using var requestMessage = new HttpRequestMessage(HttpMethod.Get, BuildCategoryRequestUri(destination.TargetUri, username, password, action));
        using var responseMessage = await _upstreamClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        responseMessage.EnsureSuccessStatusCode();

        var payload = await _upstreamClient.ReadFromJsonAsync<List<XtreamUpstreamCategoryDto>>(responseMessage.Content, cancellationToken) ?? [];
        return payload
            .Select(category => new DiscoveredCategory(category.CategoryId ?? string.Empty, category.CategoryName ?? string.Empty))
            .ToList();
    }

    private static Uri BuildCategoryRequestUri(Uri targetUri, string username, string password, string action) =>
        new(QueryHelpers.AddQueryString(
            targetUri.ToString(),
            new Dictionary<string, string?>
            {
                ["username"] = username,
                ["password"] = password,
                ["action"] = action
            }),
            UriKind.Absolute);

    private static bool TryParseInputUri(string protocol, string input, out Uri parsedUri)
    {
        if (Uri.TryCreate(input, UriKind.Absolute, out var explicitUri))
        {
            parsedUri = explicitUri;
            return parsedUri.Scheme is "http" or "https";
        }

        if (Uri.TryCreate($"{protocol}://{input.TrimStart('/')}", UriKind.Absolute, out var implicitUri))
        {
            parsedUri = implicitUri;
            return true;
        }

        parsedUri = null!;
        return false;
    }

    private static string BuildPlayerApiRest(string path)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(path) || path == "/"
            ? string.Empty
            : path.Trim('/');

        if (normalizedPath.EndsWith("player_api.php", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedPath;
        }

        return string.IsNullOrEmpty(normalizedPath)
            ? "player_api.php"
            : $"{normalizedPath}/player_api.php";
    }

    private static string NormalizeProtocol(string? value)
    {
        var protocol = value?.Trim().ToLowerInvariant();
        return protocol is "http" or "https"
            ? protocol
            : throw new InvalidOperationException("Protocol must be HTTP or HTTPS.");
    }

    private static int GetDefaultPort(string protocol) => protocol == Uri.UriSchemeHttps ? 443 : 80;
}

public sealed record XtreamSourceDiscoveryRequest(
    string? Protocol,
    string? HostOrBaseUrl,
    int? Port,
    string? Username,
    string? Password);

public sealed record XtreamSourceDiscoveryResult(
    int SourceId,
    int VodCategoryCount,
    int SeriesCategoryCount);

internal sealed record XtreamValidatedSourceDestination(
    string Protocol,
    string Host,
    int Port,
    string Rest,
    Uri TargetUri);

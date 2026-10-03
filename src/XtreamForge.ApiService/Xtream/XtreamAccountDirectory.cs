using System.Collections.Concurrent;

namespace XtreamForge.ApiService.Xtream;

/// <summary>Upstream provider of an Xtream account.</summary>
public sealed record XtreamUpstream(string Protocol, string Host, int Port);

/// <summary>
/// Upstream provider of each Xtream account authenticated through XtreamForge, kept in memory only and lost on restart.
/// Stream URLs built by clients from the rewritten <c>server_info</c> do not carry the upstream destination,
/// so it is found from the credentials of the stream path.
/// </summary>
public sealed class XtreamAccountDirectory
{
    private readonly ConcurrentDictionary<(string Username, string Password), XtreamUpstream> _upstreams = new();

    public void Remember(string username, string password, XtreamUpstream upstream)
    {
        ArgumentException.ThrowIfNullOrEmpty(username);
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentNullException.ThrowIfNull(upstream);

        _upstreams[(username, password)] = upstream;
    }

    /// <summary>Returns the upstream of an account, or null when the account has not authenticated since the start.</summary>
    public XtreamUpstream? Find(string username, string password)
        => _upstreams.TryGetValue((username, password), out var upstream) ? upstream : null;
}

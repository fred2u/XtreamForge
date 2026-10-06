using Microsoft.JSInterop;

namespace XtreamForge.Web.Components.Shared;

/// <summary>
/// Time zone of the browser of the circuit. The server (a container usually running in UTC) does not know it,
/// so <see cref="DateTimeOffset.ToLocalTime"/> would show the server time instead of the time of the user.
/// </summary>
public sealed class BrowserTimeZone(IJSRuntime jsRuntime)
{
    // Function defined by App.razor, returning the IANA time zone of the browser.
    private const string GetTimeZoneFunction = "xtreamForge.getTimeZone";

    private TimeZoneInfo? _timeZone;

    /// <summary>
    /// Reads the time zone of the browser once per circuit; UTC when the browser zone is missing or unknown to the server.
    /// JS interop is only available once the component has rendered (<c>OnAfterRenderAsync</c>).
    /// </summary>
    public async ValueTask<TimeZoneInfo> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_timeZone is null)
        {
            var id = await jsRuntime.InvokeAsync<string?>(GetTimeZoneFunction, cancellationToken);
            _timeZone = !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var timeZone)
                ? timeZone
                : TimeZoneInfo.Utc;
        }

        return _timeZone;
    }
}

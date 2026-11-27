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

    /// <summary>Time zone already read for this circuit, null until <see cref="GetAsync"/> has completed once.</summary>
    public TimeZoneInfo? Current => _timeZone;

    /// <summary>
    /// Reads the time zone of the browser once per circuit; UTC when the browser zone is missing, cannot be read or is unknown to the server.
    /// JS interop is only available once the component has rendered (<c>OnAfterRenderAsync</c>).
    /// </summary>
    public async ValueTask<TimeZoneInfo> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_timeZone is null)
        {
            string? id;
            try
            {
                id = await jsRuntime.InvokeAsync<string?>(GetTimeZoneFunction, cancellationToken);
            }
            catch (JSException)
            {
                // the function failed in the browser: fall back to UTC
                id = null;
            }
            catch (JSDisconnectedException)
            {
                // the circuit is gone: nothing is rendered anymore
                id = null;
            }

            _timeZone = !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var timeZone)
                ? timeZone
                : TimeZoneInfo.Utc;
        }

        return _timeZone;
    }
}

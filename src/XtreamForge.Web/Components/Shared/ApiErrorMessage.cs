using System.Text.Json;

namespace XtreamForge.Web.Components.Shared;

/// <summary>Turns a failed backend call into a message suitable for users; technical details are shown separately.</summary>
public static class ApiErrorMessage
{
    public static string Describe(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: { } statusCode } =>
            $"The server returned an unexpected response ({(int)statusCode} {statusCode}).",
        HttpRequestException => "The server could not be reached. Check that the API service is running.",
        JsonException or NotSupportedException => "The server returned data in an unexpected format.",
        TaskCanceledException or TimeoutException => "The server did not respond in time.",
        _ => "An unexpected error occurred."
    };
}

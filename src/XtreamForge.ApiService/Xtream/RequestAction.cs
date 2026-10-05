namespace XtreamForge.ApiService.Xtream;

public enum RequestAction
{
    Undefined = 0,
    GetCategories = 1,
    GetItems = 2,
    GetInfo = 3,

    /// <summary><c>player_api.php</c> without action: authentication, returning <c>user_info</c> and <c>server_info</c>.</summary>
    Authenticate = 4,
}

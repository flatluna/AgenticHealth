namespace PersonalAgent.Security;

public sealed class AuthenticationSettings
{
    public string ClientId { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string Authority { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = string.Empty;

    public string PostLogoutRedirectUri { get; set; } = string.Empty;

    public string Scope { get; set; } = "User.Read";
}

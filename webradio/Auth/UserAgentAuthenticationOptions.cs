using Microsoft.AspNetCore.Authentication;

namespace Webradio.Auth;

public sealed class UserAgentAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "User Agent";
    public string Scheme => DefaultScheme;
    public string AuthenticationType => DefaultScheme;
}

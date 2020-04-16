using Microsoft.AspNetCore.Authentication;

namespace Webradio.Auth
{
    public class UserAgentAuthenticationOptions : AuthenticationSchemeOptions
    {
        public const string DefaultScheme = "User Agent";
        public string Scheme => DefaultScheme;
        public string AuthenticationType => DefaultScheme;
    }
}

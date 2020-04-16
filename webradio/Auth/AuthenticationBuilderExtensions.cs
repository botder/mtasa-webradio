using Microsoft.AspNetCore.Authentication;
using System;

namespace Webradio.Auth
{
    public static class AuthenticationBuilderExtensions
    {
        public static AuthenticationBuilder AddApiKeySupport(this AuthenticationBuilder authenticationBuilder, Action<ApiKeyAuthenticationOptions> options)
        {
            return authenticationBuilder.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationOptions.DefaultScheme, options);
        }

        public static AuthenticationBuilder AddUserAgentSupport(this AuthenticationBuilder authenticationBuilder, Action<UserAgentAuthenticationOptions> options)
        {
            return authenticationBuilder.AddScheme<UserAgentAuthenticationOptions, UserAgentAuthenticationHandler>(UserAgentAuthenticationOptions.DefaultScheme, options);
        }
    }
}

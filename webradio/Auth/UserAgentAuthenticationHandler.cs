using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


namespace Webradio.Auth
{
    public class UserAgentAuthenticationHandler : AuthenticationHandler<UserAgentAuthenticationOptions>
    {
        private static readonly Regex currentServerRegex = new Regex(@"^MTA:SA Server\s+(\S+)");
        private static readonly Regex windowMediaRegex = new Regex(@"^(?:NSPlayer|NSServer|WMCacheProxy)\/\d+\.\d+(?:\.\d+\.\d+)");

        private readonly ApiKeyManager apiKeyManager;
        private bool isEnabled = true;
        private bool logUserAgent = false;

        public UserAgentAuthenticationHandler(
            IOptionsMonitor<UserAgentAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock,
            IOptionsMonitor<ApplicationOptions> applicationOptionsMonitor,
            ApiKeyManager apiKeyManager) : base(options, logger, encoder, clock)
        {
            this.apiKeyManager = apiKeyManager ?? throw new ArgumentNullException(paramName: nameof(apiKeyManager));

            isEnabled = applicationOptionsMonitor.CurrentValue?.UseUserAgentAuthentication ?? true;
            logUserAgent = applicationOptionsMonitor.CurrentValue?.LogUserAgent ?? false;

            applicationOptionsMonitor.OnChange(options =>
            {
                isEnabled = options?.UseUserAgentAuthentication ?? true;
                logUserAgent = options?.LogUserAgent ?? false;
            });
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            IPAddress ipAddress = Request.HttpContext.Connection.RemoteIpAddress;
            string userAgent = Request.Headers["User-Agent"];
            string path = $"{Request.Path}{Request.QueryString}";

            if (!isEnabled)
            {
                if (logUserAgent)
                {
                    Logger.LogInformation($"{ipAddress} -> {path} (user-agent: {userAgent})");
                }

                return Success("Nobody");
            }

            if (string.IsNullOrWhiteSpace(userAgent))
            {
                Logger.LogInformation($"Access for ip {ipAddress} denied - empty (path: {path}, user-agent: {userAgent})");
                return Task.FromResult(AuthenticateResult.Fail("invalid user agent"));
            }

            if (windowMediaRegex.Match(userAgent).Success)
            {
                return Success("MS-WMSP"); // Windows Media HTTP Streaming Protocol
            }

            Match currentServerMatch = currentServerRegex.Match(userAgent);

            if (currentServerMatch.Success)
            {
                string serverAddress = currentServerMatch.Groups[1].Value;
                ApiKey apiKey = apiKeyManager.GetApiKeyFromServer(serverAddress);

                if (apiKey != null)
                {
                    return Success(apiKey.Owner);
                }
                else
                {
                    Logger.LogInformation($"Access for ip {ipAddress} denied - unknown server (path: {path}, user-agent: {userAgent})");
                    return Task.FromResult(AuthenticateResult.Fail("invalid user agent"));
                }
            }

            Logger.LogInformation($"Access for ip {ipAddress} denied - unmatched (path: {path}, user-agent: {userAgent})");
            return Task.FromResult(AuthenticateResult.Fail("invalid user agent"));
        }

        private Task<AuthenticateResult> Success(string owerName)
        {
            var claims = new List<Claim>()
            {
                new Claim(ClaimTypes.Name, owerName),
            };
            var identity = new ClaimsIdentity(claims, Options.AuthenticationType);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Options.Scheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}

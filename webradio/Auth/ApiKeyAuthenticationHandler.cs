using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace Webradio.Auth;

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private const string ApiKeyHeaderName = "X-Api-Key";

    private readonly ApiKeyManager apiKeyManager;
    private bool isEnabled = true;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptionsMonitor<ApplicationOptions> applicationOptionsMonitor,
        ApiKeyManager apiKeyManager) : base(options, logger, encoder)
    {
        this.apiKeyManager = apiKeyManager ?? throw new ArgumentNullException(paramName: nameof(apiKeyManager));

        isEnabled = applicationOptionsMonitor.CurrentValue?.UseApikeyAuthentication ?? true;

        applicationOptionsMonitor.OnChange(options =>
        {
            isEnabled = options?.UseApikeyAuthentication ?? true;
        });
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!isEnabled)
        {
            return Success("Nobody");
        }

        if (!Request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKeyHeaderValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var providedApiKey = apiKeyHeaderValues.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(providedApiKey))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        ApiKey apiKey = apiKeyManager.GetApiKeyFromKey(providedApiKey);

        if (apiKey == null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key provided"));
        }

        if (apiKey.AllowedIPAddresses.Count > 0)
        {
            IPAddress ipAddress = Request.HttpContext.Connection.RemoteIpAddress;

            if (!apiKey.AllowedIPAddresses.Contains(ipAddress))
            {
                return Task.FromResult(AuthenticateResult.Fail($"client ip address {ipAddress} is not allowed"));
            }
        }

        return Success(apiKey.Owner);
    }

    private Task<AuthenticateResult> Success(string owerName)
    {
        var claims = new List<Claim>()
        {
            new(ClaimTypes.Name, owerName),
        };
        var identity = new ClaimsIdentity(claims, Options.AuthenticationType);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Options.Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

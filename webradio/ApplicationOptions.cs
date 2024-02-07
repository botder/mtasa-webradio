using System.Collections.Generic;

namespace Webradio;

public sealed class ApiKeyConfiguration
{
    public string Owner { get; set; }
    public string Key { get; set; }
    public string ServerAddress { get; set; }
    public IReadOnlyList<string> AllowedIPAddresses { get; set; }
}

public sealed class ApplicationOptions
{
    public IReadOnlyList<ApiKeyConfiguration> ApiKeys { get; set; }
    public bool? UseApikeyAuthentication { get; set; }
    public bool? UseUserAgentAuthentication { get; set; }
    public bool? LogUserAgent { get; set; }
}

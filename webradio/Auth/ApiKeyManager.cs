using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Net;

namespace Webradio.Auth
{
    public class ApiKeyManager
    {
        private readonly ILogger<ApiKeyManager> logger;
        private IReadOnlyDictionary<string, ApiKey> fromKey;
        private IReadOnlyDictionary<string, ApiKey> fromServerAddress;

        public ApiKeyManager(ILogger<ApiKeyManager> logger, IOptionsMonitor<ApplicationOptions> monitor)
        {
            this.logger = logger;

            monitor.OnChange(LoadFromApplicationOptions);
            LoadFromApplicationOptions(monitor.CurrentValue);
        }

        private void LoadFromApplicationOptions(ApplicationOptions options)
        {
            var fromKey = new Dictionary<string, ApiKey>();
            var fromServerAddress = new Dictionary<string, ApiKey>();

            if (options != null && options.ApiKeys != null)
            {
                foreach (ApiKeyConfiguration config in options.ApiKeys)
                {
                    if (string.IsNullOrWhiteSpace(config.Owner))
                    {
                        logger.LogWarning("API key entry in application config is missing an owner");
                        continue;
                    }
                    else if (string.IsNullOrWhiteSpace(config.Key))
                    {
                        logger.LogWarning($"API key entry in application config is missing a key (owner: {config.Owner})");
                        continue;
                    }
                    else if (string.IsNullOrWhiteSpace(config.ServerAddress))
                    {
                        logger.LogWarning($"API key entry in application config is missing a server address (owner: {config.Owner})");
                        continue;
                    }

                    if (fromKey.ContainsKey(config.Key))
                    {
                        logger.LogCritical($"API key entry duplicates key (owner: {config.Owner})");
                        continue;
                    }
                    else if (fromServerAddress.ContainsKey(config.ServerAddress))
                    {
                        logger.LogCritical($"API key entry duplicates server address (owner: {config.Owner})");
                        continue;
                    }

                    var addresses = new List<IPAddress>();

                    if (config.AllowedIPAddresses != null)
                    {
                        foreach (string ipAddress in config.AllowedIPAddresses)
                        {
                            if (IPAddress.TryParse(ipAddress, out IPAddress address))
                            {
                                addresses.Add(address);
                            }
                            else
                            {
                                try
                                {
                                    IPHostEntry hostInfo = Dns.GetHostEntry(ipAddress);
                                    logger.LogInformation($"API key for {config.Owner} has resolved {hostInfo.AddressList.Length} addresses from allowed hostname: {ipAddress}");

                                    if (hostInfo.AddressList.Length > 0)
                                    {
                                        foreach (var hostAddress in hostInfo.AddressList)
                                        {
                                            addresses.Add(hostAddress);
                                            logger.LogInformation($"API key for {config.Owner} added resolved address: {hostAddress}");
                                        }
                                    }
                                }
                                catch
                                {
                                    logger.LogInformation($"API key for {config.Owner} has an invalid allowed hostname: {ipAddress}");
                                }
                            }
                        }
                    }

                    var apiKey = new ApiKey(config.Owner, config.Key, config.ServerAddress, addresses);

                    fromKey[apiKey.Key] = apiKey;
                    fromServerAddress[apiKey.ServerAddress] = apiKey;
                }
            }

            this.fromKey = fromKey;
            this.fromServerAddress = fromServerAddress;

            logger.LogInformation($"{fromKey.Count} API keys are registered");
        }

        public ApiKey GetApiKeyFromServer(string serverAddress)
        {
            fromServerAddress.TryGetValue(serverAddress, out var apiKey);
            return apiKey;
        }

        public ApiKey GetApiKeyFromKey(string rawKey)
        {
            fromKey.TryGetValue(rawKey, out var apiKey);
            return apiKey;
        }
    }
}

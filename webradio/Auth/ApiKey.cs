using System;
using System.Collections.Generic;
using System.Net;

namespace Webradio.Auth
{
    public class ApiKey
    {
        public ApiKey(string owner, string key, string serverAddress, IReadOnlyList<IPAddress> allowedIPAddresses = null)
        {
            Owner = owner ?? throw new ArgumentNullException(paramName: nameof(owner));
            Key = key ?? throw new ArgumentNullException(paramName: nameof(key));
            ServerAddress = !string.IsNullOrWhiteSpace(serverAddress) ? serverAddress : "";
            AllowedIPAddresses = allowedIPAddresses ?? new List<IPAddress>();
        }

        public string Owner { get; }
        public string Key { get; }
        public string ServerAddress { get; }
        public IReadOnlyList<IPAddress> AllowedIPAddresses { get; }
    }
}

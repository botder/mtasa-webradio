﻿using System;
using System.Collections.Generic;
using System.Net;

namespace Webradio.Auth;

public sealed class ApiKey
{
    public ApiKey(string owner, string key, string serverAddress, IReadOnlyList<IPAddress> allowedIPAddresses = null)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Key = key ?? throw new ArgumentNullException(nameof(key));
        ServerAddress = !string.IsNullOrWhiteSpace(serverAddress) ? serverAddress : "";
        AllowedIPAddresses = allowedIPAddresses ?? [];
    }

    public string Owner { get; }
    public string Key { get; }
    public string ServerAddress { get; }
    public IReadOnlyList<IPAddress> AllowedIPAddresses { get; }
}

using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using static Webradio.Service.Webradio;

namespace Webradio.Service;

public class ServiceManager : IServiceManager
{
    private readonly Dictionary<string, WebradioService> services = new Dictionary<string, WebradioService>();
    private readonly ILogger<ServiceManager> logger;

    public ServiceManager(ILogger<ServiceManager> logger)
    {
        this.logger = logger;

        RegisterService("soundcloud", "http://webradio-soundcloud-service/");
        RegisterService("youtube", "http://webradio-youtube-service/");
    }

    private void RegisterService(string serviceName, string address)
    {
        var options = new GrpcChannelOptions()
        {
            ThrowOperationCanceledOnCancellation = true,
        };

        GrpcChannel channel = GrpcChannel.ForAddress(address, options);
        var client = new WebradioClient(channel);
        services.Add(serviceName, new WebradioService(client));

        logger.LogInformation("Service {Name} (address: {Address}) has been registered", serviceName, address);
    }

    public WebradioService GetService(string serviceName)
    {
        services.TryGetValue(serviceName, out var client);
        return client;
    }
}

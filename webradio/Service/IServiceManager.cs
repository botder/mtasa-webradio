namespace Webradio.Service;

public interface IServiceManager
{
    WebradioService GetService(string serviceName);
}

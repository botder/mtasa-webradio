using static Webradio.Service.Webradio;

namespace Webradio.Service;

public class WebradioService
{
    public WebradioClient Client { get; }
    public Configuration Configuration { get; }

    public WebradioService(WebradioClient client)
    {
        Client = client;
        Configuration = Client.GetConfiguration(new ConfigurationRequest());
    }
}

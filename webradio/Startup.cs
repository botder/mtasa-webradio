using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Webradio.Auth;
using Webradio.Service;

namespace Webradio;

public sealed class Startup
{
    private readonly IConfiguration configuration;

    public Startup(IConfiguration configuration)
    {
        this.configuration = configuration;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.Configure<ApplicationOptions>(configuration.GetSection("ApplicationOptions"));
        
        services.AddSingleton<IServiceManager, ServiceManager>();
        services.AddSingleton<ApiKeyManager>();

        services.AddAuthentication(options =>
        {
            options.DefaultScheme = ApiKeyAuthenticationOptions.DefaultScheme;
            options.DefaultChallengeScheme = ApiKeyAuthenticationOptions.DefaultScheme;
        })
        .AddApiKeySupport(options => { })
        .AddUserAgentSupport(options => { });

        services.AddDistributedRedisCache(options =>
        {
            options.Configuration = "redis";
            options.InstanceName = "webradio";
        });

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownProxies.Clear();
            options.KnownNetworks.Clear();
        });

        services.AddControllers();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseForwardedHeaders();

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });
    }
}

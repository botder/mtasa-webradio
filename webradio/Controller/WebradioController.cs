using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.Collections;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Webradio.Service;

namespace Webradio.Controller;

[ApiController]
public class WebradioController : ControllerBase
{
    private const double SearchRequestTimeoutInSeconds = 7;
    private const double StreamRequestTimeoutInSeconds = 7;

    private readonly ILogger<WebradioController> logger;
    private readonly IDistributedCache cache;
    private readonly IServiceManager services;

    public WebradioController(ILogger<WebradioController> logger, IDistributedCache cache, IServiceManager services)
    {
        this.logger = logger;
        this.cache = cache;
        this.services = services;
    }

    [Authorize(AuthenticationSchemes = Auth.ApiKeyAuthenticationOptions.DefaultScheme)]
    [HttpGet("{serviceName}/search")]
    public async Task<ActionResult> Search([FromRoute] string serviceName, [FromQuery] string query)
    {
        // Check if parameters fulfill our basic requirements
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return NotFound();
        }

        logger.LogInformation("{UserIdentity} @ {RemoteIpAddress} -> {Path}{Query}",
            User.Identity.Name, HttpContext.Connection.RemoteIpAddress, Request.Path, Request.QueryString);

        // Use the cache before contacting the requested service
        string cacheKey = GenerateCacheKey("search", serviceName, query);
        string cacheValue = null;

        try
        {
            using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromSeconds(1));
            cacheValue = await cache.GetStringAsync(cacheKey, cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogError("Search: Cache took too long to respond (> 1 second) [key: {Key}]", cacheKey);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Search: Cache threw an exception for data lookup [key: {Key}]", cacheKey);
        }

        if (!string.IsNullOrWhiteSpace(cacheValue))
        {
            try
            {
                var items = JsonConvert.DeserializeObject<RepeatedField<SearchResponseItem>>(cacheValue);

                return new JsonResult(new
                {
                    success = true,
                    items,
                });
            }
            catch (JsonReaderException readerException)
            {
                logger.LogError(readerException, "{Service} threw an exception during cache deserialization", serviceName);
                RemoveCacheString(cacheKey);
            }
        }

        // Use the respective service client to fetch data from a remote api or remote service
        WebradioService service = services.GetService(serviceName);

        if (service == null)
        {
            return SearchFailure("service is not supported");
        }

        SearchResponse response;

        try
        {
            using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromSeconds(SearchRequestTimeoutInSeconds));
            response = await service.Client.SearchAsync(new SearchRequest { Query = query }, cancellationToken: cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogError("Search: {Service} took too long to respond (> {Timeout} seconds)", serviceName, SearchRequestTimeoutInSeconds);
            return SearchFailure($"service timeout reached after {SearchRequestTimeoutInSeconds} seconds");
        }
        catch (RpcException exception)
        {
            logger.LogError(exception, "{Service} threw an exception", serviceName);
            return SearchFailure("service is out of order");
        }

        // Check the response from the service
        if (response.Status == null || response.Items == null || !response.Status.Success)
        {
            return SearchFailure(response.Status?.ErrorMessage);
        }

        if (response.Items.Count > 0)
        {
            SetCacheString(cacheKey, JsonConvert.SerializeObject(response.Items), service.Configuration.SearchExpirationInSeconds);
        }

        return new JsonResult(new
        {
            success = true,
            items = response.Items,
        });
    }

    [Authorize(AuthenticationSchemes = Auth.UserAgentAuthenticationOptions.DefaultScheme)]
    [HttpGet("{serviceName}/stream/{id}")]
    public async Task<ActionResult> Stream([FromRoute] string serviceName, [FromRoute] string id)
    {
        // Check if parameters fulfill our basic requirements
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return NotFound();
        }

        logger.LogInformation("{RemoteIpAddress} @ {UserIdentity} -> {Path}{Query}",
            HttpContext.Connection.RemoteIpAddress, User.Identity.Name, Request.Path, Request.QueryString);

        // Use the cache before contacting the requested service
        string cacheKey = GenerateCacheKey("stream", serviceName, id);
        string cacheValue = null;

        try
        {
            using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromSeconds(1));
            cacheValue = await cache.GetStringAsync(cacheKey, cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogError("Stream: Cache took too long to respond (> 1 second) [key: {Key}]", cacheKey);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Stream: Cache threw an exception for data lookup [key: {Key}]", cacheKey);
        }

        if (!string.IsNullOrWhiteSpace(cacheValue))
        {
            return Redirect(cacheValue);
        }

        // Use the respective service client to fetch data from a remote api or remote service
        WebradioService service = services.GetService(serviceName);

        if (service == null)
        {
            return SearchFailure("service is not supported");
        }
        
        StreamResponse response;

        try
        {
            using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromSeconds(StreamRequestTimeoutInSeconds));
            response = await service.Client.StreamAsync(new StreamRequest { Id = id }, cancellationToken: cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogError("Stream: {Service} took too long to respond (> {Timeout} seconds)", serviceName, SearchRequestTimeoutInSeconds);
            return NotFound();
        }
        catch (RpcException exception)
        {
            logger.LogError(exception, "Stream: {Service} threw an exception", serviceName);
            return NotFound();
        }

        // Check the response from the service
        if (response.Status == null || !response.Status.Success || string.IsNullOrWhiteSpace(response.Url))
        {
            return NotFound();
        }

        SetCacheString(cacheKey, response.Url, service.Configuration.StreamExpirationInSeconds);
        return Redirect(response.Url);
    }

    [NonAction]
    private static ObjectResult SearchFailure(string error)
    {
        return new ObjectResult(new
        {
            success = false,
            error = string.IsNullOrWhiteSpace(error) ? "unknown error" : error,
        });
    }

    [NonAction]
    private static string GenerateCacheKey(string action, string service, string identifier)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{action}-{service}-{identifier.Trim().ToLower()}"));
        return BitConverter.ToString(hash);
    }

    [NonAction]
    private void SetCacheString(string key, string value, long expirationOffsetInSeconds)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var cacheOptions = new DistributedCacheEntryOptions()
                {
                    SlidingExpiration = TimeSpan.FromSeconds(expirationOffsetInSeconds),
                };

                using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromSeconds(3));
                await cache.SetStringAsync(key, value, cacheOptions, cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                logger.LogError("SetCacheString: Cache took too long to respond (> 3 seconds) [key: {Key}]", key);
            }
            catch (Exception cacheException)
            {
                logger.LogError(cacheException, "SetCacheString: Cache threw an exception");
            }
        });
    }

    [NonAction]
    private void RemoveCacheString(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await cache.RemoveAsync(key, cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                logger.LogError("RemoveCacheString: Cache took too long to respond (> 1 second)");
            }
            catch (Exception cacheException)
            {
                logger.LogError(cacheException, "RemoveCacheString: Cache threw an exception");
            }
        });
    }
}

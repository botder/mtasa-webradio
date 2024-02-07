using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.YouTube.v3.Data;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Webradio.Service;
using YouTubeService.Helpers;
using GoogleYouTubeApi = Google.Apis.YouTube.v3;
using static Webradio.Service.Webradio;
using static YouTubeService.Helpers.YouTubeDownloadHelper;

namespace YouTubeService;

public partial class YouTubeService : WebradioBase
{
    private static readonly IList<string> supportedFormats = ["338", "251", "250", "249", "18", "22", "37", "38"];

    // Regex patterns for these variants:
    // ?v={videoId}
    // &v={videoId}
    // /v/{videoId}
    // /embed/{videoId}
    // youtu.be/{videoId}
    [GeneratedRegex(@"(?:[?&]v=|(?:\/(?:v|embed)|youtu\.be)\/)([A-Za-z0-9\-_]{11})")]
    private static partial Regex YouTubeRegex();

    [GeneratedRegex(@"[A-Za-z0-9\-_]{11}")]
    private static partial Regex VideoIdRegex();

    private static readonly Regex youtubeRegex = YouTubeRegex();
    private static readonly Regex videoIdRegex = VideoIdRegex();

    private readonly GoogleYouTubeApi.YouTubeService googleYouTubeService;
    private readonly ILogger<YouTubeService> logger;

    public YouTubeService(IConfiguration configuration, ILogger<YouTubeService> logger)
    {
        googleYouTubeService = new GoogleYouTubeApi.YouTubeService(new Google.Apis.Services.BaseClientService.Initializer
        {
            ApiKey = configuration["ApiKey"],
            ApplicationName = GetType().Name,
        });

        this.logger = logger;
    }

    public override Task<Configuration> GetConfiguration(ConfigurationRequest request, ServerCallContext context)
    {
        return Task.FromResult(new Configuration
        {
            SearchExpirationInSeconds = Convert.ToInt64(TimeSpan.FromDays(7).TotalSeconds),
            StreamExpirationInSeconds = Convert.ToInt64(TimeSpan.FromMinutes(30).TotalSeconds),
        });
    }

    public override async Task<SearchResponse> Search(SearchRequest request, ServerCallContext context)
    {
        // Check if parameters fulfill our basic requirements
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return SearchFailure("query is empty");
        }

        // Try to extract a video id from the query and fetch information for that specific video
        if (ParseVideoId(request.Query, out string videoIdFromQuery))
        {
            // Use youtube-dl to get the video information
            YouTubeVideoInformation videoInformation = null;

            try
            {
                using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromSeconds(5));
                videoInformation = await GetVideoInformationAsync(videoIdFromQuery, cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                logger.LogError("Search: Video information lookup (id: {VideoId}) took too long to respond (> 5 seconds)", videoIdFromQuery);
                return SearchFailure("remote api information fetch timeout");
            }
            catch (Exception exception)
            {
                logger.LogInformation(exception, "Search: Downloading video (id: {VideoId}) information through youtube-dl has failed", videoIdFromQuery);
                return SearchFailure("remote api information fetch exception");
            }

            if (videoInformation == null)
            {
                logger.LogInformation("Search: Video (id: {VideoId}) information is empty", videoIdFromQuery);
                return SearchFailure("video not available");
            }
            else if (videoInformation.Video == null || videoInformation.Error != null)
            {
                logger.LogInformation("Stream: Downloading video (id: {VideoId}) information through youtube-dl has failed: {Error}", videoIdFromQuery, videoInformation.Error);
                return SearchFailure("video not available");
            }
            else
            {
                var response = new SearchResponse
                {
                    Status = new ResponseStatus { Success = true }
                };

                response.Items.Add(new SearchResponseItem
                {
                    Id = videoInformation.Video.Id,
                    Title = videoInformation.Video.Title,
                });

                return response;
            }
        }
        else
        {
            // Search for videos
            GoogleYouTubeApi.SearchResource.ListRequest searchListRequest = googleYouTubeService.Search.List("snippet");
            searchListRequest.Q = request.Query;
            searchListRequest.MaxResults = 50;
            searchListRequest.Order = GoogleYouTubeApi.SearchResource.ListRequest.OrderEnum.ViewCount;
            searchListRequest.SafeSearch = GoogleYouTubeApi.SearchResource.ListRequest.SafeSearchEnum.None;
            searchListRequest.Type = "video";
            searchListRequest.VideoDimension = GoogleYouTubeApi.SearchResource.ListRequest.VideoDimensionEnum.Value2d;

            SearchListResponse searchListResponse = null;

            try
            {
                using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromSeconds(5));
                searchListResponse = await searchListRequest.ExecuteAsync(cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                logger.LogError("Video search (query: {Query}) took too long to respond (> 5 seconds)", request.Query);
                return SearchFailure("remote api search timeout");
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Video search (query: {Query}) threw an exception", request.Query);
                return SearchFailure("remote api search exception");
            }

            // Check the search response
            if (searchListResponse == null)
            {
                logger.LogError("Video search (query: {Query}) with invalid result", request.Query);
                return SearchFailure("unknown search error");
            }

            var response = new SearchResponse
            {
                Status = new ResponseStatus { Success = true }
            };

            response.Items.AddRange(
                searchListResponse.Items.Select(item => new SearchResponseItem
                {
                    Id = item.Id.VideoId,
                    Title = item.Snippet.Title,
                }));

            return response;
        }
    }

    public override async Task<StreamResponse> Stream(StreamRequest request, ServerCallContext context)
    {
        // Check if parameters fulfill our basic requirements
        if (!IsVideoId(request.Id))
        {
            return StreamFailure("invalid video id");
        }

        // Use youtube-dl to get the URL for the download
        YouTubeVideoInformation videoInformation = null;

        try
        {
            using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromSeconds(5));
            videoInformation = await GetVideoInformationAsync(request.Id, cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogError("Stream: Video information lookup (id: {RequestId}) took too long to respond (> 5 seconds)", request.Id);
            return StreamFailure("remote api information fetch timeout");
        }
        catch (Exception exception)
        {
            logger.LogInformation(exception, "Stream: Downloading video (id: {RequestId}) information through youtube-dl has failed", request.Id);
            return StreamFailure("video information fetch exception");
        }

        if (videoInformation == null)
        {
            logger.LogInformation("Stream: Video (id: {RequestId}) information is empty", request.Id);
            return StreamFailure("video information is empty");
        }
        else if (videoInformation.Video == null || videoInformation.Error != null)
        {
            logger.LogInformation("Stream: Downloading video (id: {RequestId}) information through youtube-dl has failed: {Error}", request.Id, videoInformation.Error);
            return StreamFailure("video information fetch error");
        }
        else
        {
            // Filter video information by supported formats and order by format id
            IOrderedEnumerable<YouTubeVideoFormat> videoFormats =
                videoInformation.Video.Formats.Where(format => supportedFormats.Contains(format.FormatId))
                                              .OrderByDescending(format => Convert.ToInt64(format.FormatId));

            // Find a responsive downloadable url
            foreach (YouTubeVideoFormat videoFormat in videoFormats)
            {
                if (await IsURLAvailable(videoFormat.Url))
                {
                    return new StreamResponse
                    {
                        Status = new ResponseStatus { Success = true },
                        Url = videoFormat.Url,
                    };
                }
            }

            return StreamFailure("video is not playable");
        }
    }

    private static async Task<bool> IsURLAvailable(string url)
    {
        try
        {
            using HttpClient httpClient = new();
            using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromSeconds(2));
            using HttpResponseMessage response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationTokenSource.Token);
            var statusCode = (int)response.StatusCode;
            return statusCode >= 100 && statusCode < 400;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool ParseVideoId(string input, out string videoId)
    {
        Match match = youtubeRegex.Match(input);

        if (!match.Success)
        {
            videoId = null;
            return false;
        }

        videoId = match.Groups[1].Value;
        return true;
    }

    private static bool IsVideoId(string input) => videoIdRegex.IsMatch(input);

    private static SearchResponse SearchFailure(string errorMessage)
    {
        return new SearchResponse
        {
            Status = new ResponseStatus
            {
                Success = false,
                ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? "unknown error" : errorMessage,
            }
        };
    }

    private static StreamResponse StreamFailure(string errorMessage)
    {
        return new StreamResponse
        {
            Status = new ResponseStatus
            {
                Success = false,
                ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? "unknown error" : errorMessage,
            }
        };
    }
}

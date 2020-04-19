using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Webradio.Service;
using YouTubeService.Helpers;
using static Webradio.Service.Webradio;
using static YouTubeService.Helpers.YouTubeDownloadHelper;

namespace YouTubeService
{
    using GoogleYouTubeApi = Google.Apis.YouTube.v3;

    public class YouTubeService : WebradioBase
    {
        private static readonly IList<string> supportedFormats = new List<string>(){ "18", "22", "37", "38" };

        // Regex patterns for these variants:
        // ?v={videoId}
        // &v={videoId}
        // /v/{videoId}
        // /embed/{videoId}
        // youtu.be/{videoId}
        private static readonly Regex youtubeRegex = new Regex(@"(?:\?v=|\&v=|(?:\/(?:v|embed)|youtu\.be)\/)([A-Za-z0-9\-_]{11})");

        private static readonly Regex videoIdRegex = new Regex(@"[A-Za-z0-9\-_]{11}");

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
                    var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    videoInformation = await GetVideoInformationAsync(videoIdFromQuery, cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    logger.LogError($"Search: Video information lookup (id: {videoIdFromQuery}) took too long to respond (> 5 seconds)");
                    return SearchFailure("remote api information fetch timeout");
                }
                catch (Exception exception)
                {
                    logger.LogInformation($"Search: Downloading video (id: {videoIdFromQuery}) information through youtube-dl has failed: {exception}");
                    return SearchFailure("remote api information fetch exception");
                }

                if (videoInformation == null)
                {
                    logger.LogInformation($"Search: Video (id: {videoIdFromQuery}) information is empty");
                    return SearchFailure("video not available");
                }
                else if (videoInformation.Video == null || videoInformation.Error != null)
                {
                    logger.LogInformation(
                        $"Stream: Downloading video (id: {videoIdFromQuery}) information through youtube-dl has failed: {videoInformation.Error}");
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

                GoogleYouTubeApi.Data.SearchListResponse searchListResponse = null;

                try
                {
                    var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    searchListResponse = await searchListRequest.ExecuteAsync(cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    logger.LogError($"Video search (query: {request.Query}) took too long to respond (> 5 seconds)");
                    return SearchFailure("remote api search timeout");
                }
                catch (Exception exception)
                {
                    logger.LogError($"Video search (query: {request.Query}) threw an exception: {exception}");
                    return SearchFailure("remote api search exception");
                }

                // Check the search response
                if (searchListResponse == null)
                {
                    logger.LogError($"Video search (query: {request.Query}) with invalid result");
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
                var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                videoInformation = await GetVideoInformationAsync(request.Id, cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                logger.LogError($"Stream: Video information lookup (id: {request.Id}) took too long to respond (> 5 seconds)");
                return StreamFailure("remote api information fetch timeout");
            }
            catch (Exception exception)
            {
                logger.LogInformation($"Stream: Downloading video (id: {request.Id}) information through youtube-dl has failed: {exception}");
                return StreamFailure("video information fetch exception");
            }

            if (videoInformation == null)
            {
                logger.LogInformation($"Stream: Video (id: {request.Id}) information is empty");
                return StreamFailure("video information is empty");
            }
            else if (videoInformation.Video == null || videoInformation.Error != null)
            {
                logger.LogInformation(
                    $"Stream: Downloading video (id: {request.Id}) information through youtube-dl has failed: {videoInformation.Error}");
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

        private async Task<bool> IsURLAvailable(string url)
        {
            try
            {
                HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
                request.Method = "HEAD";
                request.Timeout = 2000;

                using (HttpWebResponse response = await request.GetResponseAsync() as HttpWebResponse)
                {
                    var statusCode = (int)response.StatusCode;
                    return statusCode >= 100 && statusCode < 400;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool ParseVideoId(string input, out string videoId)
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

        private bool IsVideoId(string input) => videoIdRegex.IsMatch(input);

        private SearchResponse SearchFailure(string errorMessage)
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

        private StreamResponse StreamFailure(string errorMessage)
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
}

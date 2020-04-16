using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Webradio.Service;
using static Webradio.Service.Webradio;

namespace YouTubeService
{
    using GoogleYouTubeApi = Google.Apis.YouTube.v3;

    public class YouTubeService : WebradioBase
    {
        private static readonly IList<string> SupportedFormats = new List<string>(){ "18", "22", "37", "38" };

        private readonly YoutubeExplode.YoutubeClient youTubeExplodeClient = new YoutubeExplode.YoutubeClient();
        private readonly GoogleYouTubeApi.YouTubeService googleYouTubeService;
        private readonly NYoutubeDL.YoutubeDL youtubeDL = new NYoutubeDL.YoutubeDL();
        private readonly ILogger<YouTubeService> logger;

        public YouTubeService(IConfiguration configuration, ILogger<YouTubeService> logger)
        {
            googleYouTubeService = new GoogleYouTubeApi.YouTubeService(new Google.Apis.Services.BaseClientService.Initializer()
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
            if (YoutubeExplode.YoutubeClient.TryParseVideoId(request.Query, out string videoIdFromQuery))
            {
                YoutubeExplode.Models.Video video = null;

                // Retry video information lookup three times (magic number) before giving up
                for (int i = 0; i < 3; ++i)
                {
                    try
                    {
                        video = await youTubeExplodeClient.GetVideoAsync(videoIdFromQuery);
                        
                        if (video != null)
                        {
                            break;
                        }
                    }
                    catch (Exception exception)
                    {
                        logger.LogInformation($"Search: YoutubeExplode - Video (id: {videoIdFromQuery}) not found: {exception}");
                    }
                }

                if (video == null)
                {
                    return SearchFailure("video not found");
                }

                if (string.IsNullOrWhiteSpace(video.Title))
                {
                    logger.LogInformation($"Search: YoutubeExplode - Video (id: {videoIdFromQuery}) found, but invalid: title is empty");
                    return SearchFailure("video not found (no data)");
                }

                var response = new SearchResponse
                {
                    Status = new ResponseStatus { Success = true }
                };

                response.Items.Add(new SearchResponseItem
                {
                    Id = video.Id,
                    Title = video.Title,
                });

                return response;
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
            if (!YoutubeExplode.YoutubeClient.ValidateVideoId(request.Id))
            {
                return StreamFailure("invalid video id");
            }

            // Use youtube-dl to get the URL for the download
            NYoutubeDL.Models.VideoDownloadInfo downloadInfo;

            try
            {
                downloadInfo = await youtubeDL.GetDownloadInfoAsync("https://youtube.com/watch?v=" + request.Id) as NYoutubeDL.Models.VideoDownloadInfo;

                if (downloadInfo == null)
                {
                    logger.LogInformation($"Stream: Video (id: {request.Id}) information is empty");
                    return StreamFailure("video information is empty");
                }
                else
                {
                    // Filter video information by supported formats and order by format id
                    IOrderedEnumerable<NYoutubeDL.Models.FormatDownloadInfo> formatDownloadInfos =
                        downloadInfo.Formats.Where(format => SupportedFormats.Contains(format.FormatId))
                                            .OrderByDescending(format => Convert.ToInt64(format.FormatId));

                    // Find a responsive downloadable url
                    foreach (NYoutubeDL.Models.FormatDownloadInfo formatDownloadInfo in formatDownloadInfos)
                    {
                        if (await IsURLAvailable(formatDownloadInfo.Url))
                        {
                            return new StreamResponse
                            {
                                Status = new ResponseStatus { Success = true },
                                Url = formatDownloadInfo.Url,
                            };
                        }
                    }

                    return StreamFailure("video is not playable");
                }
            }
            catch (Exception exception)
            {
                logger.LogInformation($"Stream: Downloading video (id: {request.Id}) information through youtube-dl has failed: {exception}");
                return StreamFailure("video information fetch exception");
            }
        }

        private async Task<bool> IsURLAvailable(string url)
        {
            try
            {
                HttpWebRequest request = HttpWebRequest.Create(url) as HttpWebRequest;
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

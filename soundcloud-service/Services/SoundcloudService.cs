using System;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using SoundCloud.Api;
using SoundCloud.Api.Entities;
using SoundCloud.Api.Entities.Enums;
using SoundCloud.Api.QueryBuilders;
using Webradio.Service;
using static Webradio.Service.Webradio;

namespace SoundCloudService
{
    public class SoundCloudService : WebradioBase
    {
        private readonly string clientId;
        private readonly ISoundCloudClient client;

        public SoundCloudService(IConfiguration configuration, ISoundCloudClient client)
        {
            clientId = configuration["ClientId"];
            this.client = client;
        }

        public override Task<Configuration> GetConfiguration(ConfigurationRequest request, ServerCallContext context)
        {
            return Task.FromResult(new Configuration
            {
                SearchExpirationInSeconds = Convert.ToInt64(TimeSpan.FromDays(7).TotalSeconds),
                StreamExpirationInSeconds = Convert.ToInt64(TimeSpan.FromMinutes(30).TotalSeconds),
            });
        }

        public async override Task<SearchResponse> Search(SearchRequest request, ServerCallContext context)
        {
            // Check if parameters fulfill our basic requirements
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return SearchFailure("query is empty");
            }

            // Search for tracks
            var builder = new TrackQueryBuilder
            {
                SearchString = request.Query,
                Limit = 50,
                Sharing = Sharing.Public
            };

            SoundCloudList<Track> tracks = await client.Tracks.GetAllAsync(builder);

            var response = new SearchResponse
            {
                Status = new ResponseStatus { Success = true }
            };

            response.Items.AddRange(
                tracks.Select(track => new SearchResponseItem
                {
                    Id = track.Id.ToString(),
                    Title = track.Title,
                    Duration = track.Duration / 1000,
                }));

            return response;
        }

        public override async Task<StreamResponse> Stream(StreamRequest request, ServerCallContext context)
        {
            // Check if parameters fulfill our basic requirements
            if (!long.TryParse(request.Id, out long trackId))
            {
                return StreamFailure("invalid track id");
            }

            // Get track information from the Soundcloud API
            Track track = await client.Tracks.GetAsync(trackId);

            if (track == null)
            {
                return StreamFailure("failed to resolve track from id");
            }
            else
            {
                return new StreamResponse
                {
                    Status = new ResponseStatus { Success = true },
                    Url = $"{track.StreamUrl.AbsoluteUri}?client_id={clientId}",
                };
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

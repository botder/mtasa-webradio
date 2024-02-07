using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using static YouTubeService.Helpers.ProcessHelper;

namespace YouTubeService.Helpers;

sealed class YouTubeVideo
{
    public string Id { get; set; }
    public string Title { get; set; }
    public int Duration { get; set; }
    public IReadOnlyCollection<YouTubeVideoFormat> Formats { get; set; }
}

sealed class YouTubeVideoFormat
{
    [JsonPropertyName("format_id")]
    public string FormatId { get; set; }
    public string Url { get; set; }
}

sealed class YouTubeVideoInformation
{
    public YouTubeVideo Video { get; set; }
    public string Error { get; set; }
}

static class YouTubeDownloadHelper
{
    private static readonly string[] DefaultArgumentsList =
    {
        "--no-warnings",        // Ignore warnings
        "--ignore-errors",      // Continue on download errors
        "--ignore-config",      // Do not read configuration files
        "--no-color",           // Do not emit color codes in output
        "--no-mark-watched",    // Do not mark videos watched
        "--no-call-home",       // Do not contact youtube-dl server for debugging
        "--simulate",           // Do not download the video and do not write anything to disk
        "--dump-single-json",   // Print JSON information for each command-line argument
        "--no-progress",        // Do not print progress bar
        "--no-playlist",        // Download only the video
        "--no-geo-bypass",      // Do not bypass geographic restriction
        "--rm-cache-dir",       // Delete all filesystem cache files
    };

    private static readonly string DefaultArguments = string.Join(" ", DefaultArgumentsList);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static async Task<YouTubeVideoInformation> GetVideoInformationAsync(string videoId, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new YouTubeVideoInformation { Error = "Process aborted by client" };
        }

        ProcessResult processResult = await StartProcessAsync("youtube-dl",
            string.Join(" ", DefaultArguments, $"https://www.youtube.com/watch?v={videoId}"),
            cancellationToken);

        if (!processResult.Completed
            || string.IsNullOrWhiteSpace(processResult.Output)
            || processResult.Output.Equals("null", StringComparison.InvariantCultureIgnoreCase))
        {
            if (processResult.Error != null && processResult.Error.StartsWith("error:", StringComparison.InvariantCultureIgnoreCase))
            {
                return new YouTubeVideoInformation { Error = processResult.Error.Substring(6).Replace('\n', ' ').Trim() };
            }
            else
            {
                return new YouTubeVideoInformation { Error = processResult.Error };
            }
        }

        try
        {
            var video = JsonSerializer.Deserialize<YouTubeVideo>(processResult.Output, SerializerOptions);

            if (video != null)
            {
                return new YouTubeVideoInformation { Video = video };
            }
            else
            {
                return new YouTubeVideoInformation { Error = "invalid youtube-dl output" };
            }
        }
        catch (JsonException)
        {
            return new YouTubeVideoInformation { Error = "invalid json in youtube-dl output" };
        }
    }
}

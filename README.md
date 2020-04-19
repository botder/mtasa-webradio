# Webradio
Webradio is an application programming interface (API) for usage on Multi Theft Auto servers to provide players with audio playback from different sources (for example YouTube and SoundCloud) without storing the audio files in any kind on the server and serving these directly from the source.

- [Requirements](#requirements)
- [Installation](#installation)
- [API](#api)
  - [Configuration](#configuration)
    - [Webradio](#webradio-1)
    - [YouTube](#youtube)
    - [SoundCloud](#soundcloud)
  - [Providers](#providers)
  - [Searching](#searching)
    - [Request](#request)
    - [Response](#response)
    - [Examples](#examples)
  - [Streaming](#streaming)
    - [Request](#request-1)
    - [Response](#response-1)
    - [Examples](#examples-1)

## Requirements
- TODO

## Installation
- TODO

## API

### Configuration
The following sections describe the available configuration you can or must supply to the applications. You could overwrite the respective `appsettings.json` file or, preferably, create your own `appsettings.Production.json` configuration (if deployed) or edit the existing `appsettings.Development.json` (if developing).

#### Webradio
```
{
  "ApplicationOptions": {
    "UseApikeyAuthentication": bool,
    "UseUserAgentAuthentication": bool,
    "LogUserAgent": bool,
    "ApiKeys": [
      {
        "Owner": string,
        "Key": string,
        "ServerAddress": string,
        "AllowedIPAddresses": [ string, ... ]
      },
      ...
    ]
  }
}
```
- **UseApikeyAuthentication** Enforces authentication for search endpoint
- **UseUserAgentAuthentication** Enforces authentication for stream endpoint
- **LogUserAgent** Logs stream endpoint access if **UseUserAgentAuthentication** is disabled
- **ApiKeys** Array of api keys
  - **Owner** Name of the api key owner (no restrictions on length and characters, can't be null or empty)
  - **Key** A random key for the owner (no restrictions on length and characters, can't be null or empty)
  - **ServerAddress** ServerIP:ServerPort (for example `"127.0.0.1:22003"`), required for user-agent authentication
  - **AllowedIPAddresses** Array of server IPs using the search endpoint (for example `[ "127.0.0.1" ]`)

#### YouTube
```
{
  "ApiKey": string
}
```
- **ApiKey** Your YouTube Data API key

#### SoundCloud
```
{
  "ClientId": string
}
```
- **ClientId** Your SoundCloud API client ID ([Stack Overflow: Getting a SoundCloud API client ID](https://stackoverflow.com/q/40992480))

### Providers
Service providers must be registered directly in the webradio [service manager](webradio/Service/ServiceManager.cs) constructor. There are no plans to expose an API to let services register themselves at the moment. They must be hardcoded.

### Searching

#### Request
```
/{serviceName}/search?query={queryString}
```
- **serviceName** (case-ignore) is a registered service provider (see [ServiceManager](webradio/Service/ServiceManager.cs))
- **queryString** is the input string to search for
  - YouTube service supports searching with a YouTube URL

If **UseApikeyAuthentication** is enabled, the request must include the header field **X-Api-Key** with an appropriate key from the configuration.

#### Response
- [401 Unauthorized](https://httpstatuses.com/401) if authentication failed (**UseApikeyAuthentication** is enabled)
  - Response body is empty

- [404 Not Found](https://httpstatuses.com/404) if there was an server issue or the audio was not found
  - Response body is empty

- [200 OK](https://httpstatuses.com/200) if successful (JSON field **success** can be `false`)
  - **Note:** YouTube service doesn't provide the audio duration
```
{
    "success": false,
    "error": string
}
```
```
{
    "success": true,
    "items": [
        {
            "id": string,
            "title": string,
            "duration": number
        },
        ...
    ]
}
```

#### Examples
```
/youtube/search?query=developers
/youtube/search?query=rick%20astley
/youtube/search?query=https%3A%2F%2Fyoutu.be%2FKMU0tzLwhbE
```

### Streaming

#### Request
```
/{serviceName}/stream/{identifier}
```
- **serviceName** (case-ignore) is a registered service provider (see [ServiceManager](webradio/Service/ServiceManager.cs))
- **identifier** can be anything, for example a YouTube video id `dQw4w9WgXcQ`

If **UseUserAgentAuthentication** is enabled, the request must be made with the MTA:SA client-side function [playSound](https://wiki.multitheftauto.com/wiki/PlaySound) or [playSound3D](https://wiki.multitheftauto.com/wiki/PlaySound3D).

#### Response

- [302 Found](https://httpstatuses.com/302) redirect to the audio source
- [401 Unauthorized](https://httpstatuses.com/401) if authentication failed (**UseUserAgentAuthentication** is enabled)
- [404 Not Found](https://httpstatuses.com/404) if there was an server issue or the audio was not found

#### Examples
```
/youtube/stream/dQw4w9WgXcQ
/youtube/stream/KMU0tzLwhbE
```

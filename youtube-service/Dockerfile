FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
ARG DEBIAN_FRONTEND=noninteractive
RUN apt-get update \
    && apt-get install -y --no-install-recommends cron ffmpeg software-properties-common curl jq \
    && rm -rf /var/lib/apt/lists/* /tmp/* /var/tmp/* /usr/share/doc/*
RUN ytdlp_version="$(curl --silent "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest" | jq -r .tag_name)" \
    && curl -sSL -o /usr/local/bin/youtube-dl "https://github.com/yt-dlp/yt-dlp/releases/download/$ytdlp_version/yt-dlp" \
    && chmod a+rx /usr/local/bin/youtube-dl
RUN touch /var/log/cron.log \
    && ln -sf /dev/stdout /var/log/cron.log \
    && printf 'SHELL=/bin/sh\n/usr/local/bin/youtube-dl -U' > /etc/cron.daily/youtube-dl \
    && chmod 755 /etc/cron.daily/youtube-dl
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["youtube-service/youtube-service.csproj", "youtube-service/"]
RUN dotnet restore "youtube-service/youtube-service.csproj"
COPY . .
WORKDIR "/src/youtube-service"
RUN dotnet build "youtube-service.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "youtube-service.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "youtube-service.dll"]

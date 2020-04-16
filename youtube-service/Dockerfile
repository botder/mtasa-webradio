FROM mcr.microsoft.com/dotnet/core/aspnet:3.1-buster-slim AS base
ARG DEBIAN_FRONTEND=noninteractive
RUN apt-get update \
    && apt-get install -y --no-install-recommends cron ffmpeg nano python software-properties-common wget \
    && rm -rf /var/lib/apt/lists/* /tmp/* /var/tmp/* /usr/share/doc/*
RUN wget https://yt-dl.org/downloads/latest/youtube-dl -O /usr/local/bin/youtube-dl \
	&& chmod a+rx /usr/local/bin/youtube-dl \
	&& hash -r
RUN touch /var/log/cron.log \
	&& ln -sf /dev/stdout /var/log/cron.log \
	&& echo "SHELL=/bin/sh\n/usr/local/bin/youtube-dl -U" > /etc/cron.daily/youtube-dl \
	&& chmod 755 /etc/cron.daily/youtube-dl
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/core/sdk:3.1-buster AS build
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
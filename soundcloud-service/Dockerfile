FROM mcr.microsoft.com/dotnet/core/aspnet:3.1-buster-slim AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/core/sdk:3.1-buster AS build
WORKDIR /src
COPY ["soundcloud-service/soundcloud-service.csproj", "soundcloud-service/"]
RUN dotnet restore "soundcloud-service/soundcloud-service.csproj"
COPY . .
WORKDIR "/src/soundcloud-service"
RUN dotnet build "soundcloud-service.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "soundcloud-service.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "soundcloud-service.dll"]
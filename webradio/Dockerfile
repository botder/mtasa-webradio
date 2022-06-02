FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["webradio/webradio.csproj", "webradio/"]
RUN dotnet restore "webradio/webradio.csproj"
COPY . .
WORKDIR "/src/webradio"
RUN dotnet build "webradio.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "webradio.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "webradio.dll"]
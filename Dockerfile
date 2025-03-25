FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["fnws-weather/fnws-weather.csproj", "fnws-weather/"]
RUN dotnet restore "fnws-weather/fnws-weather.csproj"
COPY . .
WORKDIR "/src/fnws-weather"
RUN dotnet build "fnws-weather.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "fnws-weather.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "fnws-weather.dll"]
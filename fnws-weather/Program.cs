using System.Dynamic;
using System.Globalization;
using System.Text.Json;
using GeoTimeZone;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IWeatherService, OpenMeteoWeatherService>();
builder.Services.AddSingleton<IWeatherCodeMapper, WeatherCodeMapper>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/weather", async (
    double lat, 
    double lon, 
    string? units, 
    IWeatherService weatherService) => 
    await weatherService.GetWeatherForecastAsync(lat, lon, units))
    .WithName("GetWeatherForecast");

app.Run();

#region Models

public record WeatherRequest(double Latitude, double Longitude, string? Units);

public record Forecast(
    string dow, 
    long expire_time_gmt, 
    long fcst_valid, 
    int max_temp, 
    int min_temp, 
    string sunrise, 
    string sunset,
    int icon_code,
    string weathercode,
    TimeZoneInfo timezone)
{
    public DayPart day => new(dow, 'D', sunrise, icon_code, weathercode, max_temp);
    public DayPart night => new(dow, 'N', sunset, icon_code, weathercode, min_temp);

    public string fcst_valid_local => TimeZoneInfo
        .ConvertTime(DateTimeOffset.FromUnixTimeSeconds(fcst_valid), timezone)
        .ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
}

public record DayPart(
    string daypart_name, 
    char day_ind, 
    string fcst_valid_local, 
    int icon_code, 
    string phrase_12char, 
    int temp)
{
    public string alt_daypart_name => daypart_name;
    public string long_daypart_name => daypart_name;
    public long fcst_valid => DateTimeOffset.Parse(fcst_valid_local).ToUnixTimeSeconds();
    public string golf_category => "boring sports";
    public int icon_code_extd => icon_code * 100;
    public string phrase_22char => phrase_12char;
    public string phrase_32char => phrase_12char;

    public string temp_phrase => day_ind switch
    {
        'D' => $"High of {temp}°",
        'N' => $"Low of {temp}°",
        _ => $"{temp}°"
    };

    public string narrative => $"{phrase_12char} with a {temp_phrase}.";
}

#endregion

#region Services

public interface IWeatherService
{
    Task<object> GetWeatherForecastAsync(double latitude, double longitude, string? units);
}

public interface IWeatherCodeMapper
{
    string GetWeatherDescription(int code);
    int WeatherCodeToPebble(int code);
    string GetWindDirection(int degrees);
}

public class WeatherCodeMapper : IWeatherCodeMapper
{
    public string GetWindDirection(int degrees)
    {
        string[] directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        return directions[(int)Math.Round(degrees / 45.0) % 8];
    }

    public string GetWeatherDescription(int code) => code switch
    {
        0 => "Clear sky",
        1 => "Mainly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 => "Fog",
        48 => "Depositing rime fog",
        51 => "Light drizzle",
        53 => "Moderate drizzle",
        55 => "Dense drizzle",
        56 => "Light freezing drizzle",
        57 => "Dense freezing drizzle",
        61 => "Slight rain",
        63 => "Moderate rain",
        65 => "Heavy rain",
        66 => "Light freezing rain",
        67 => "Heavy freezing rain",
        71 => "Slight snow fall",
        73 => "Moderate snow fall",
        75 => "Heavy snow fall",
        77 => "Snow grains",
        80 => "Slight rain showers",
        81 => "Moderate rain showers",
        82 => "Violent rain showers",
        85 => "Slight snow showers",
        86 => "Heavy snow showers",
        95 => "Thunderstorm",
        96 => "Thunderstorm with slight hail",
        99 => "Thunderstorm with heavy hail",
        _ => "Unknown weather condition"
    };

    public int WeatherCodeToPebble(int code) => code switch 
    {
        0 => 31,
        1 => 31,
        2 => 29,
        3 => 28,
        45 => 27,
        48 => 27,
        51 => 11,
        53 => 11,
        55 => 11,
        56 => 11,
        57 => 11,
        61 => 11,
        63 => 11,
        65 => 11,
        66 => 11,
        67 => 11,
        80 => 11,
        81 => 11,
        82 => 11,
        71 => 41,
        73 => 41,
        75 => 41,
        77 => 41,
        85 => 41,
        86 => 41,
        95 => 1,
        96 => 0,
        99 => 0,
        _ => 28
    };
}

public class OpenMeteoWeatherService : IWeatherService
{
    private readonly HttpClient _httpClient;
    private readonly IWeatherCodeMapper _weatherCodeMapper;
    private readonly ILogger<OpenMeteoWeatherService> _logger;
    private const string BaseUrl = "https://api.open-meteo.com/v1/forecast";

    public OpenMeteoWeatherService(
        HttpClient httpClient,
        IWeatherCodeMapper weatherCodeMapper,
        ILogger<OpenMeteoWeatherService> logger)
    {
        _httpClient = httpClient;
        _weatherCodeMapper = weatherCodeMapper;
        _logger = logger;
    }

    public async Task<object> GetWeatherForecastAsync(double latitude, double longitude, string? units)
    {
        var timezone = GetTimezone(latitude, longitude);
        var (temperatureUnit, windspeedUnit) = GetUnits(units);
        
        _logger.LogInformation("Getting weather forecast for {Lat}, {Lon} in {Timezone} with units {Units}", 
            latitude, longitude, timezone.Id, units);

        var url = BuildRequestUrl(latitude, longitude, timezone.Id, temperatureUnit, windspeedUnit);
        var responseData = await FetchWeatherDataAsync(url);
        
        var forecasts = ParseForecasts(responseData, timezone);
        var currentData = ParseCurrentConditions(responseData, forecasts, timezone, units);
        
        return BuildResponse(currentData, forecasts);
    }

    private TimeZoneInfo GetTimezone(double latitude, double longitude)
    {
        return TimeZoneInfo.FindSystemTimeZoneById(
            TimeZoneLookup.GetTimeZone(latitude, longitude).Result) ?? TimeZoneInfo.Utc;
    }

    private (string temperatureUnit, string windspeedUnit) GetUnits(string? units)
    {
        return units == "e" 
            ? ("fahrenheit", "mph") 
            : ("celsius", "kmh");
    }

    private string BuildRequestUrl(
        double latitude, 
        double longitude, 
        string timezoneId, 
        string temperatureUnit, 
        string windspeedUnit)
    {
        return $"{BaseUrl}?latitude={latitude.ToString("F2", CultureInfo.InvariantCulture)}" +
               $"&longitude={longitude.ToString("F2", CultureInfo.InvariantCulture)}" +
               $"&daily=temperature_2m_min,temperature_2m_max,apparent_temperature_min,sunrise,sunset,precipitation_probability_max,weather_code" +
               $"&current=temperature_2m,wind_speed_10m,wind_direction_10m,weather_code" +
               $"&timezone={timezoneId}" +
               $"&temperature_unit={temperatureUnit}" +
               $"&windspeed_unit={windspeedUnit}";
    }

    private async Task<JsonDocument> FetchWeatherDataAsync(string url)
    {
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var responseBody = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(responseBody);
    }

    private List<Forecast> ParseForecasts(JsonDocument doc, TimeZoneInfo timezone)
    {
        var forecasts = new List<Forecast>();
        var root = doc.RootElement;
        var dailyData = root.GetProperty("daily");
        
        var times = dailyData.GetProperty("time").EnumerateArray().ToList();
        var minTemps = dailyData.GetProperty("temperature_2m_min").EnumerateArray().ToList();
        var maxTemps = dailyData.GetProperty("temperature_2m_max").EnumerateArray().ToList();
        var sunrises = dailyData.GetProperty("sunrise").EnumerateArray().ToList();
        var sunsets = dailyData.GetProperty("sunset").EnumerateArray().ToList();
        var weatherCodes = dailyData.GetProperty("weather_code").EnumerateArray().ToList();

        for (var i = 0; i < times.Count; i++)
        {
            var date = DateTime.Parse(times[i].GetString()!);
            var dow = date.DayOfWeek.ToString();
            var sunriseTime = DateTime.Parse(sunrises[i].GetString()!);
            var sunsetTime = DateTime.Parse(sunsets[i].GetString()!);

            var sunrise = new DateTimeOffset(sunriseTime, timezone.GetUtcOffset(sunriseTime))
                .ToString("yyyy-MM-ddTHH:mm:sszzz");
            var sunset = new DateTimeOffset(sunsetTime, timezone.GetUtcOffset(sunsetTime))
                .ToString("yyyy-MM-ddTHH:mm:sszzz");

            var fcst_valid = new DateTimeOffset(date).ToUnixTimeSeconds();
            var expire_time = fcst_valid + 86400;

            var maxTemp = (int)Math.Round(maxTemps[i].GetDouble());
            var minTemp = (int)Math.Round(minTemps[i].GetDouble());

            var weatherCode = weatherCodes[i].GetInt32();
            var weatherDescription = _weatherCodeMapper.GetWeatherDescription(weatherCode);

            forecasts.Add(new Forecast(
                dow, expire_time, fcst_valid, maxTemp, minTemp,
                sunrise, sunset, _weatherCodeMapper.WeatherCodeToPebble(weatherCode), 
                weatherDescription, timezone));
        }

        return forecasts;
    }

    private object ParseCurrentConditions(
        JsonDocument doc, 
        List<Forecast> forecasts, 
        TimeZoneInfo timezone, 
        string? units)
    {
        var root = doc.RootElement;
        var currentData = root.GetProperty("current");
        var currentTime = DateTime.Parse(currentData.GetProperty("time").GetString()!);
        var expire_time_gmt = new DateTimeOffset(currentTime).ToUnixTimeSeconds() + 600;

        string weathercase = units switch {
            "m" => "metric",
            "h" => "uk_hybrid",
            "e" => "imperial",
            _ => "metric"
        };

        var metadata = new {
            expire_time_gmt,
            language = "en_US",
            latitude = Math.Round((double)forecasts[0].fcst_valid, 2),
            longitude = Math.Round((double) forecasts[0].fcst_valid, 2),
            status_code = 200,
            transaction_id = "lol!",
            units,
            version = "1",
        };

        dynamic observation = new ExpandoObject();
        var weatherCode = currentData.GetProperty("weather_code").GetInt32();
        observation.@class = "observation";
        observation.day_ind = "D";
        observation.dow = currentTime.DayOfWeek.ToString();
        observation.expire_time_gmt = expire_time_gmt;
        observation.icon_code = _weatherCodeMapper.WeatherCodeToPebble(weatherCode);
        observation.icon_extd = _weatherCodeMapper.WeatherCodeToPebble(weatherCode) * 100;
        observation.obs_time = new DateTimeOffset(currentTime, timezone.GetUtcOffset(currentTime)).ToUnixTimeSeconds();
        
        ((IDictionary<string, object>)observation)[weathercase] = new {
            feels_like = (int)currentData.GetProperty("temperature_2m").GetDouble(),
            temp = (int)currentData.GetProperty("temperature_2m").GetDouble(),
            temp_max_24hour = forecasts[0].max_temp,
            temp_min_24hour = forecasts[0].min_temp,
        };
        
        var description = _weatherCodeMapper.GetWeatherDescription(weatherCode);
        observation.phrase_12char = description;
        observation.phrase_22char = description;
        observation.phrase_32char = description;

        return new {
            data = new {
                observation,
                metadata
            },
            errors = false,
        };
    }

    private object BuildResponse(object currentConditions, List<Forecast> forecasts)
    {
        return new {
            conditions = currentConditions,
            fcstdaily7 = new {
               data = new {
                   forecasts,
               },
               errors = false
            },
            metadata = new {
               version = 2,
               transaction_id = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            }
        };
    }
}

#endregion
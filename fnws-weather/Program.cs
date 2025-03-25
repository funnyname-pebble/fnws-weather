using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/weather", () =>
    {
        DateTime date = DateTime.Parse("2025-03-25");
        
        Forecast test = new Forecast(date.DayOfWeek.ToString(), 1630000000, new DateTimeOffset(date).ToUnixTimeSeconds(), 70, 50, "2025-03-25T05:56", "2025-03-25T18:28", 1, "Sunny", "N", 5, TimeZoneInfo.Local);
        return test;
    })
    .WithName("GetWeatherForecast");

app.Run();

record Forecast(string dow, int expire_time_gmt, long fcst_valid, int max_temp, int min_temp, string sunrise, string sunset)
{
    private int icon_code;
    private string weathercode;
    private string wind_direction;
    private int wind_speed;
    private TimeZoneInfo timezone;

    public DayPart day => new DayPart(dow, 'D', sunrise, icon_code, weathercode, max_temp, wind_direction, wind_speed);
    public DayPart night => new DayPart(dow, 'N', sunset, icon_code, weathercode, min_temp, wind_direction, wind_speed);
    public string fcst_valid_local => TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(fcst_valid), timezone).ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    public Forecast(string dow, int expire_time_gmt, long fcst_valid, int max_temp, int min_temp, string sunrise, string sunset, int icon_code, string weathercode, string wind_direction, int wind_speed, TimeZoneInfo timezone)
        : this(dow, expire_time_gmt, fcst_valid, max_temp, min_temp, sunrise, sunset)
    {
        this.icon_code = icon_code;
        this.weathercode = weathercode;
        this.wind_direction = wind_direction;
        this.wind_speed = wind_speed;
        this.timezone = timezone;
    }
}

record DayPart(string daypart_name, char day_ind, string fcst_valid_local, int icon_code, string phrase_12char, int temp, string wind_direction, float wind_speed) {

    public string alt_daypart_name => daypart_name;
    public string long_daypart_name => daypart_name;
    public long fcst_valid => DateTimeOffset.Parse(fcst_valid_local).ToUnixTimeSeconds();
    public string golf_category => "boring sports";
    public int icon_code_extd => icon_code * 100;
    public string phrase_22char => phrase_12char;
    public string phrase_32char => phrase_12char;

    public string temp_phrase {
        get {
            switch (day_ind) {
                case 'D':
                    return $"High of {temp}°";
                case 'N':
                    return $"Low of {temp}°";
                default:
                    return $"{temp}°";
            }
        }
    }
    
    public string narrative => $"{phrase_12char} with a {temp_phrase}. Winds {wind_direction} at {wind_speed}.";
}
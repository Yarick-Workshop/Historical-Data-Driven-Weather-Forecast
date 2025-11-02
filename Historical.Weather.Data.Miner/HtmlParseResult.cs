namespace Historical.Weather.Data.Miner;

/// <summary>
/// Represents the result of parsing an HTML file.
/// </summary>
public class HtmlParseResult
{
    /// <summary>
    /// Gets the city name extracted from the title.
    /// </summary>
    public string? CityName { get; }

    /// <summary>
    /// Gets the date extracted from the title and validated against the filename.
    /// Format: YYYY-MM-dd (e.g., "2016-12-10").
    /// </summary>
    public string? Date { get; }
                                                                                                
    /// <summary>
    /// Gets the list of weather data rows from the archive table.
    /// </summary>
    public List<WeatherDataRow> WeatherDataRows { get; }

    /// <summary>
    /// Initializes a new instance of the HtmlParseResult class.
    /// </summary>
    /// <param name="cityName">The city name extracted from the title.</param>
    /// <param name="date">The date extracted from the title in YYYY-MM-dd format.</param>
    /// <param name="weatherDataRows">The list of weather data rows from the archive table.</param>
    public HtmlParseResult(string? cityName, string? date, List<WeatherDataRow>? weatherDataRows = null)
    {
        CityName = cityName;
        Date = date;
        WeatherDataRows = weatherDataRows ?? new List<WeatherDataRow>();
    }
}


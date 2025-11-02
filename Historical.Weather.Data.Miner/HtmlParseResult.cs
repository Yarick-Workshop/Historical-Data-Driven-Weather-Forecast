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
    /// Initializes a new instance of the HtmlParseResult class.
    /// </summary>
    /// <param name="cityName">The city name extracted from the title.</param>
    public HtmlParseResult(string? cityName)
    {
        CityName = cityName;
    }
}


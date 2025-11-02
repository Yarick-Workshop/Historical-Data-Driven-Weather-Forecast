                                                                                                    namespace Historical.Weather.Data.Miner;

/// <summary>
/// Represents a single row of weather data from the archive table.
/// </summary>
public class WeatherDataRow
{
    /// <summary>
    /// Gets the time (e.g., "00:00").
    /// </summary>
    public string Time { get; }

    /// <summary>
    /// Gets the weather characteristics (e.g., "Сплошная облачность").
    /// </summary>
    public string WeatherCharacteristics { get; }

    /// <summary>
    /// Gets the air temperature (e.g., "+5°C").
    /// </summary>
    public string Temperature { get; }

    /// <summary>
    /// Gets the wind direction (e.g., "Западный").
    /// </summary>
    public string WindDirection { get; }

    /// <summary>
    /// Gets the wind speed (e.g., "7.0").
    /// </summary>
    public string WindSpeed { get; }

    /// <summary>
    /// Gets the atmospheric pressure (e.g., "740").
    /// </summary>
    public string AtmosphericPressure { get; }

    /// <summary>
    /// Gets the air humidity percentage (e.g., "93").
    /// </summary>
    public string Humidity { get; }

    /// <summary>
    /// Initializes a new instance of the WeatherDataRow class.
    /// </summary>
    /// <param name="time">The time value.</param>
    /// <param name="weatherCharacteristics">The weather characteristics.</param>
    /// <param name="temperature">The air temperature.</param>
    /// <param name="windDirection">The wind direction.</param>
    /// <param name="windSpeed">The wind speed.</param>
    /// <param name="atmosphericPressure">The atmospheric pressure.</param>
    /// <param name="humidity">The air humidity percentage.</param>
    public WeatherDataRow(
        string time,
        string weatherCharacteristics,
        string temperature,
        string windDirection,
        string windSpeed,
        string atmosphericPressure,
        string humidity)
    {
        Time = time;
        WeatherCharacteristics = weatherCharacteristics;
        Temperature = temperature;
        WindDirection = windDirection;
        WindSpeed = windSpeed;
        AtmosphericPressure = atmosphericPressure;
        Humidity = humidity;
    }
}


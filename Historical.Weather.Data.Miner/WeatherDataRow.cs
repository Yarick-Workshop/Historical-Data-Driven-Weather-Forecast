using System;
using System.Collections.Generic;

namespace Historical.Weather.Data.Miner;

/// <summary>
/// Represents a single row of weather data from the archive table.
/// </summary>
public class WeatherDataRow
{
    /// <summary>
    /// Gets the date and time.
    /// </summary>
    public DateTime Time { get; }

    /// <summary>
    /// Gets the normalized weather characteristics.
    /// </summary>
    public List<string> WeatherCharacteristics { get; }

    /// <summary>
    /// Gets the air temperature in degrees Celsius.
    /// </summary>
    public int Temperature { get; }

    /// <summary>
    /// Gets the wind direction (e.g., "Западный").
    /// </summary>
    public string WindDirection { get; }

    /// <summary>
    /// Gets the wind speed in m/s.
    /// </summary>
    public decimal WindSpeed { get; }

    /// <summary>
    /// Gets the atmospheric pressure.
    /// </summary>
    public int AtmosphericPressure { get; }

    /// <summary>
    /// Gets the air humidity percentage.
    /// </summary>
    public int Humidity { get; }

    /// <summary>
    /// Initializes a new instance of the WeatherDataRow class.
    /// </summary>
    /// <param name="time">The date and time value.</param>
    /// <param name="weatherCharacteristics">The normalized weather characteristics.</param>
    /// <param name="temperature">The air temperature in degrees Celsius.</param>
    /// <param name="windDirection">The wind direction.</param>
    /// <param name="windSpeed">The wind speed in m/s.</param>
    /// <param name="atmosphericPressure">The atmospheric pressure.</param>
    /// <param name="humidity">The air humidity percentage.</param>
    public WeatherDataRow(
        DateTime time,
        List<string> weatherCharacteristics,
        int temperature,
        string windDirection,
        decimal windSpeed,
        int atmosphericPressure,
        int humidity)
    {
        Time = time;
        WeatherCharacteristics = weatherCharacteristics ?? throw new ArgumentNullException(nameof(weatherCharacteristics));
        Temperature = temperature;
        WindDirection = windDirection;
        WindSpeed = windSpeed;
        AtmosphericPressure = atmosphericPressure;
        Humidity = humidity;
    }
}


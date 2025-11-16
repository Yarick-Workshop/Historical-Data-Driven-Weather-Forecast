using System;
using Historical.Weather.Core;

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
    /// Gets the weather characteristics flags.
    /// </summary>
    public WeatherCharacteristics WeatherCharacteristics { get; }

    /// <summary>
    /// Gets the air temperature in degrees Celsius.
    /// </summary>
    public int Temperature { get; }

    /// <summary>
    /// Gets the wind direction azimuth angle (0..359 degrees).
    /// </summary>
    public int WindDirectionAzimuth { get; }

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
    /// <param name="weatherCharacteristics">The weather characteristics flags.</param>
    /// <param name="temperature">The air temperature in degrees Celsius.</param>
    /// <param name="windDirectionAzimuth">The wind direction azimuth angle (0..359).</param>
    /// <param name="windSpeed">The wind speed in m/s.</param>
    /// <param name="atmosphericPressure">The atmospheric pressure.</param>
    /// <param name="humidity">The air humidity percentage.</param>
    public WeatherDataRow(
        DateTime time,
        WeatherCharacteristics weatherCharacteristics,
        int temperature,
        int windDirectionAzimuth,
        decimal windSpeed,
        int atmosphericPressure,
        int humidity)
    {
        Time = time;
        WeatherCharacteristics = weatherCharacteristics;
        Temperature = temperature;
        WindDirectionAzimuth = windDirectionAzimuth;
        WindSpeed = windSpeed;
        AtmosphericPressure = atmosphericPressure;
        Humidity = humidity;
    }
}


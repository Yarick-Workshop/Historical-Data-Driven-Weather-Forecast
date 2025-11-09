using System.Collections.Generic;

namespace Historical.Weather.Core;

/// <summary>
///     Provides constant names for the core columns that appear in normalized weather CSV files.
/// </summary>
public static class WeatherCsvColumns
{
    public const string Place = "Place";
    public const string DateTime = "DateTime";
    public const string Temperature = "Temperature";
    public const string WindDirection = "WindDirection";
    public const string WindSpeed = "WindSpeed";
    public const string AtmosphericPressure = "AtmosphericPressure";
    public const string Humidity = "Humidity";

    /// <summary>
    ///     Ordered collection of all core columns before the weather characteristic flags are appended.
    /// </summary>
    public static readonly IReadOnlyList<string> CoreColumns = new[]
    {
        Place,
        DateTime,
        Temperature,
        WindDirection,
        WindSpeed,
        AtmosphericPressure,
        Humidity
    };
}



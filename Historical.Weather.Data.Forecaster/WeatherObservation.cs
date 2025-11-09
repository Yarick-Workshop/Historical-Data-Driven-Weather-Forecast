namespace Historical.Weather.Data.Forecaster;

using Historical.Weather.Core;

/// <summary>
///     Represents a single weather observation extracted from a CSV row.
/// </summary>
internal sealed record WeatherObservation(
    string Place,
    DateTime Timestamp,
    double Temperature,
    double[] FeatureVector,
    WeatherCharacteristics Characteristics);



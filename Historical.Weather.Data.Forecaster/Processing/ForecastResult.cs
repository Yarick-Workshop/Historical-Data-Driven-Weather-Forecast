namespace Historical.Weather.Data.Forecaster.Processing;

internal sealed record ForecastResult(
    string Place,
    int TotalRecords,
    int TrainingRecords,
    int ValidationRecords,
    ForecastMetrics? Metrics,
    IReadOnlyList<ForecastEvaluationPoint> ValidationSeries,
    ForecastPrediction? NextPrediction);

internal sealed record ForecastEvaluationPoint(DateTime Timestamp, double Actual, double Predicted);

internal sealed record ForecastPrediction(DateTime Timestamp, double Temperature);

internal sealed record ForecastMetrics(double MeanAbsoluteError, double RootMeanSquareError, double? MeanAbsolutePercentageError, int Samples);



using System.Globalization;
using Historical.Weather.Data.Forecaster.Processing;
using Serilog;

namespace Historical.Weather.Data.Forecaster.IO;

internal sealed class ForecastReportWriter
{
    private readonly ForecastOptions _options;

    public ForecastReportWriter(ForecastOptions options)
    {
        _options = options;
    }

    public async Task WriteAsync(string csvPath, ForecastResult result)
    {
        WriteConsoleReport(csvPath, result);

        if (string.IsNullOrWhiteSpace(_options.OutputDirectory))
        {
            return;
        }

        await WriteForecastCsvAsync(csvPath, result);
    }

    private static void WriteConsoleReport(string csvPath, ForecastResult result)
    {
        Log.Information("  Place:               {Place}", result.Place);
        Log.Information("  Total rows:          {TotalRecords}", result.TotalRecords);
        Log.Information("  Training rows:       {TrainingRecords}", result.TrainingRecords);
        Log.Information("  Validation rows:     {ValidationRecords}", result.ValidationRecords);

        if (result.Metrics is null)
        {
            Log.Warning("  Accuracy:            insufficient validation rows to compute metrics.");
        }
        else
        {
            Log.Information("  MAE:                 {MAE:F2}°C", result.Metrics.MeanAbsoluteError);
            Log.Information("  RMSE:                {RMSE:F2}°C", result.Metrics.RootMeanSquareError);

            if (result.Metrics.MeanAbsolutePercentageError is { } mape)
            {
                Log.Information("  MAPE:                {MAPE:F2}%", mape);
            }
            else
            {
                Log.Information("  MAPE:                not available (temperatures near zero).");
            }
        }

        if (result.NextPrediction is null)
        {
            Log.Warning("  Next forecast:       unavailable (insufficient history).");
        }
        else
        {
            Log.Information("  Next forecast time:  {Timestamp:yyyy-MM-dd HH:mm}", result.NextPrediction.Timestamp);
            Log.Information("  Next temperature:    {Temperature:F2}°C", result.NextPrediction.Temperature);
        }
    }

    private async Task WriteForecastCsvAsync(string csvPath, ForecastResult result)
    {
        var directory = _options.OutputDirectory!;
        Directory.CreateDirectory(directory);

        var fileName = $"{Path.GetFileNameWithoutExtension(csvPath)}.forecast.csv";
        var outputPath = Path.Combine(directory, fileName);

        await using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream);

        await writer.WriteLineAsync("DateTime,ActualTemperature,PredictedTemperature,Source");

        foreach (var point in result.ValidationSeries.OrderBy(p => p.Timestamp))
        {
            await writer.WriteLineAsync(string.Join(',',
                point.Timestamp.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                point.Actual.ToString("F2", CultureInfo.InvariantCulture),
                point.Predicted.ToString("F2", CultureInfo.InvariantCulture),
                "validation"));
        }

        if (result.NextPrediction is { } prediction)
        {
            await writer.WriteLineAsync(string.Join(',',
                prediction.Timestamp.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                string.Empty,
                prediction.Temperature.ToString("F2", CultureInfo.InvariantCulture),
                "forecast"));
        }

        Log.Information("  Forecast CSV:        {OutputPath}", outputPath);
    }
}



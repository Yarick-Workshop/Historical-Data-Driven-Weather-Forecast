using System.Globalization;
using Historical.Weather.Data.Forecaster.Processing;

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
        Console.WriteLine($"  Place:               {result.Place}");
        Console.WriteLine($"  Total rows:          {result.TotalRecords}");
        Console.WriteLine($"  Training rows:       {result.TrainingRecords}");
        Console.WriteLine($"  Validation rows:     {result.ValidationRecords}");

        if (result.Metrics is null)
        {
            Console.WriteLine("  Accuracy:            insufficient validation rows to compute metrics.");
        }
        else
        {
            Console.WriteLine($"  MAE:                 {result.Metrics.MeanAbsoluteError:F2}°C");
            Console.WriteLine($"  RMSE:                {result.Metrics.RootMeanSquareError:F2}°C");

            if (result.Metrics.MeanAbsolutePercentageError is { } mape)
            {
                Console.WriteLine($"  MAPE:                {mape:F2}%");
            }
            else
            {
                Console.WriteLine("  MAPE:                not available (temperatures near zero).");
            }
        }

        if (result.NextPrediction is null)
        {
            Console.WriteLine("  Next forecast:       unavailable (insufficient history).");
        }
        else
        {
            Console.WriteLine($"  Next forecast time:  {result.NextPrediction.Timestamp:yyyy-MM-dd HH:mm}");
            Console.WriteLine($"  Next temperature:    {result.NextPrediction.Temperature:F2}°C");
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

        Console.WriteLine($"  Forecast CSV:        {outputPath}");
    }
}



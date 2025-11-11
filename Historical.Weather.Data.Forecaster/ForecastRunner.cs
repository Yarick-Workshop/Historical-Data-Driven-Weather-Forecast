using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Historical.Weather.Data.Forecaster.IO;
using Historical.Weather.Data.Forecaster.Processing;
using HtmlLogWriter;
using Serilog;

namespace Historical.Weather.Data.Forecaster;

internal sealed class ForecastRunner
{
    private readonly ForecastOptions _options;
    private readonly WeatherCsvLoader _loader;
    private readonly ForecastReportWriter _reportWriter;
    private readonly string _logFilePath;
    private readonly Dictionary<string, List<WeatherObservation>> _observationsByFile = new(StringComparer.OrdinalIgnoreCase);

    public ForecastRunner(ForecastOptions options, string logFilePath)
    {
        _options = options;
        _loader = new WeatherCsvLoader();
        _reportWriter = new ForecastReportWriter(options);
        _logFilePath = logFilePath;
    }

    public async Task RunAsync()
    {
        var totalStopwatch = Stopwatch.StartNew();
        var csvPaths = ResolveCsvPaths(_options.InputPath).ToList();
        var summaries = new List<ForecastSummary>();

        if (csvPaths.Count == 0)
        {
            Log.Warning("No CSV files were found to process.");
            return;
        }

        Log.Information("Step 1/2: Training individual models per CSV file.");
        Log.Information("Discovered {CsvCount} CSV file(s).", csvPaths.Count);

        foreach (var csvPath in csvPaths)
        {
            var fileStopwatch = Stopwatch.StartNew();
            Log.Information("================================================");
            Log.Information("Processing: {CsvPath}", csvPath);

            var observations = (await _loader.LoadAsync(csvPath)).ToList();

            if (observations.Count == 0)
            {
                Log.Warning("  Skipped: file contains no observations.");
                continue;
            }

            _observationsByFile[csvPath] = observations;
            _observationsByFile[csvPath] = observations;

            using var processor = new LstmForecastProcessor(_options);
            var result = processor.Process(observations);
            await _reportWriter.WriteAsync(csvPath, result);

            fileStopwatch.Stop();
            Log.Information("Completed processing {CsvPath} in {ElapsedSeconds:F2} seconds.",
                csvPath,
                fileStopwatch.Elapsed.TotalSeconds);

            summaries.Add(new ForecastSummary(
                Name: Path.GetFileNameWithoutExtension(csvPath),
                Result: result,
                Duration: fileStopwatch.Elapsed));
        }

        var aggregateResult = TrainAggregateModel();
        var combinedSummaries = summaries.ToList();
        if (aggregateResult is not null)
        {
            combinedSummaries.Add(aggregateResult.Summary);
        }

        totalStopwatch.Stop();
        Log.Information("Completed forecast run in {ElapsedSeconds:F2} seconds.", totalStopwatch.Elapsed.TotalSeconds);

        if (combinedSummaries.Count > 0)
        {
            Log.Information("================================================");
            Log.Information("Summary of processed models:");
            Log.Information("Place                 | Rows    | Train   | Valid   |  MAE |  RMSE |  MAPE | Next Forecast          | Temp  | Duration (s)");
            Log.Information("---------------------+---------+---------+---------+------+-------+-------+------------------------+-------+-------------");

            foreach (var summary in combinedSummaries)
            {
                var metrics = summary.Result.Metrics;
                var mae = metrics?.MeanAbsoluteError.ToString("F2", CultureInfo.InvariantCulture) ?? "-";
                var rmse = metrics?.RootMeanSquareError.ToString("F2", CultureInfo.InvariantCulture) ?? "-";
                var mape = metrics?.MeanAbsolutePercentageError?.ToString("F2", CultureInfo.InvariantCulture) ?? "-";
                var nextTime = summary.Result.NextPrediction?.Timestamp.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "-";
                var nextTemp = summary.Result.NextPrediction?.Temperature.ToString("F2", CultureInfo.InvariantCulture) ?? "-";
                var duration = summary.Duration.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture);

                Log.Information("{Place,-21}| {Total,7} | {Train,7} | {Valid,7} | {MAE,5} | {RMSE,5} | {MAPE,5} | {NextTime,-22} | {NextTemp,5} | {Duration,11}",
                    summary.Name.Length > 21 ? summary.Name[..21] : summary.Name,
                    summary.Result.TotalRecords,
                    summary.Result.TrainingRecords,
                    summary.Result.ValidationRecords,
                    mae,
                    rmse,
                    mape,
                    nextTime,
                    nextTemp,
                    duration);
            }

            Log.Information("Abbreviations: MAE = Mean Absolute Error, RMSE = Root Mean Square Error, MAPE = Mean Absolute Percentage Error.");

            WriteHtmlSummary(combinedSummaries);
        }
    }

    private static IEnumerable<string> ResolveCsvPaths(string inputPath)
    {
        if (File.Exists(inputPath))
        {
            yield return inputPath;
            yield break;
        }

        if (!Directory.Exists(inputPath))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(inputPath, "*.csv", SearchOption.TopDirectoryOnly).Order())
        {
            yield return file;
        }
    }

    private void WriteHtmlSummary(IReadOnlyList<ForecastSummary> summaries)
    {
        if (string.IsNullOrWhiteSpace(_logFilePath))
        {
            return;
        }

        try
        {
            using var writer = new HtmlLogWriter.HtmlLogWriter(_logFilePath, "Historical Weather Forecaster");
            writer.WriteSeparator();

            var summaryRows = summaries.Select(s =>
            {
                var metrics = s.Result.Metrics;
                var mae = metrics?.MeanAbsoluteError;
                var rmse = metrics?.RootMeanSquareError;
                var mape = metrics?.MeanAbsolutePercentageError;
                return new SummaryRow
                {
                    Place = s.Name,
                    TotalRows = s.Result.TotalRecords,
                    TrainingRows = s.Result.TrainingRecords,
                    ValidationRows = s.Result.ValidationRecords,
                    MAE = mae?.ToString("F2", CultureInfo.InvariantCulture) ?? "-",
                    RMSE = rmse?.ToString("F2", CultureInfo.InvariantCulture) ?? "-",
                    MAPE = mape?.ToString("F2", CultureInfo.InvariantCulture) ?? "-",
                    NextForecastTime = s.Result.NextPrediction?.Timestamp.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "-",
                    NextTemperature = s.Result.NextPrediction?.Temperature.ToString("F2", CultureInfo.InvariantCulture) ?? "-",
                    DurationSeconds = s.Duration.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture)
                };
            }).ToList();

            writer.WriteTable(summaryRows, "Forecast Summary");

            var abbreviationRows = new[]
            {
                new AbbreviationRow("MAE", "Mean Absolute Error"),
                new AbbreviationRow("RMSE", "Root Mean Square Error"),
                new AbbreviationRow("MAPE", "Mean Absolute Percentage Error")
            };

            writer.WriteTable(abbreviationRows, "Abbreviations");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to write HTML summary table.");
        }
    }

    private AggregateTrainingResult? TrainAggregateModel()
    {
        if (_observationsByFile.Count == 0)
        {
            return null;
        }

        Log.Information("Step 2/2: Training aggregate model across all CSV files.");

        var aggregateStopwatch = Stopwatch.StartNew();
        var aggregated = _observationsByFile
            .SelectMany(pair => pair.Value.Select(obs => (File: pair.Key, Observation: obs)))
            .OrderBy(entry => entry.Observation.Timestamp)
            .Select(entry => new WeatherObservation(
                entry.File,
                entry.Observation.Timestamp,
                entry.Observation.Temperature,
                entry.Observation.FeatureVector.ToArray(),
                entry.Observation.Characteristics))
            .ToList();

        if (aggregated.Count <= _options.WindowSize)
        {
            Log.Warning("Aggregate training skipped: insufficient observations ({Count}) compared to window size ({Window}).", aggregated.Count, _options.WindowSize);
            return null;
        }

        using var processor = new LstmForecastProcessor(_options);
        var result = processor.Process(aggregated);
        aggregateStopwatch.Stop();

        Log.Information("Aggregate model trained in {ElapsedSeconds:F2} seconds.", aggregateStopwatch.Elapsed.TotalSeconds);

        if (result.Metrics is null)
        {
            Log.Warning("Aggregate metrics unavailable: insufficient validation rows.");
        }
        else
        {
            Log.Information("Aggregate metrics -> MAE: {MAE:F2}°C, RMSE: {RMSE:F2}°C, MAPE: {MAPE}",
                result.Metrics.MeanAbsoluteError,
                result.Metrics.RootMeanSquareError,
                result.Metrics.MeanAbsolutePercentageError?.ToString("F2", CultureInfo.InvariantCulture) ?? "n/a");
        }

        if (result.NextPrediction is { } prediction)
        {
            Log.Information("Aggregate next forecast at {Timestamp:yyyy-MM-dd HH:mm}, temperature {Temperature:F2}°C",
                prediction.Timestamp,
                prediction.Temperature);
        }
        else
        {
            Log.Warning("Aggregate next forecast unavailable (insufficient history).");
        }

        return new AggregateTrainingResult(
            new ForecastSummary("Aggregate", result, aggregateStopwatch.Elapsed),
            result);
    }

    private sealed record ForecastSummary(string Name, ForecastResult Result, TimeSpan Duration);

    private sealed record AggregateTrainingResult(ForecastSummary Summary, ForecastResult Result);

    private sealed record SummaryRow
    {
        public string Place { get; init; } = string.Empty;
        public int TotalRows { get; init; }
        public int TrainingRows { get; init; }
        public int ValidationRows { get; init; }
        public string MAE { get; init; } = "-";
        public string RMSE { get; init; } = "-";
        public string MAPE { get; init; } = "-";
        public string NextForecastTime { get; init; } = "-";
        public string NextTemperature { get; init; } = "-";
        public string DurationSeconds { get; init; } = "-";
    }

    private sealed record AbbreviationRow(string Code, string Meaning);
}



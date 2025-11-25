using System.Collections.Generic;
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

        Console.WriteLine($"In folder: {_options.InputPath} found {csvPaths.Count} CSV files:");
        Console.WriteLine(string.Join($",{Environment.NewLine}", csvPaths));

        if (csvPaths.Count == 0)
        {
            Log.Warning("No CSV files were found to process.");
            return;
        }

        Log.Information("Step 1/2: Training individual models per CSV file.");
        Log.Information("Discovered {CsvCount} CSV file(s).", csvPaths.Count);

        var summaries = await TrainIndividualModelsAsync(csvPaths);

        var aggregateResult = await TrainAggregateModelAsync();
        var combinedSummaries = summaries.ToList();
        if (aggregateResult is not null)
        {
            combinedSummaries.Add(aggregateResult.Summary);
        }

        totalStopwatch.Stop();
        Log.Information("Completed forecast run in {Elapsed}.", FormatDuration(totalStopwatch.Elapsed));

        if (combinedSummaries.Count > 0)
        {
            Log.Information("================================================");
            Log.Information("Summary of processed models:");
            Log.Information("Place                 | Rows    | Train   | Valid   |  MAE |  RMSE |  MAPE | Next Forecast          | Temp  | Duration");
            Log.Information("---------------------+---------+---------+---------+------+-------+-------+------------------------+-------+-------------");

            foreach (var summary in combinedSummaries)
            {
                var metrics = summary.Result.Metrics;
                var mae = metrics?.MeanAbsoluteError.ToString("F2", CultureInfo.InvariantCulture) ?? "-";
                var rmse = metrics?.RootMeanSquareError.ToString("F2", CultureInfo.InvariantCulture) ?? "-";
                var mape = metrics?.MeanAbsolutePercentageError?.ToString("F2", CultureInfo.InvariantCulture) ?? "-";
                var nextTime = summary.Result.NextPrediction?.Timestamp.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "-";
                var nextTemp = summary.Result.NextPrediction?.Temperature.ToString("F2", CultureInfo.InvariantCulture) ?? "-";
                var duration = FormatDuration(summary.Duration);

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

    private async Task<List<ForecastSummary>> TrainIndividualModelsAsync(IReadOnlyCollection<string> csvPaths)
    {
        var summaries = new List<ForecastSummary>(csvPaths.Count * 2); // LSTM and GRU for each file

        foreach (var csvPath in csvPaths)
        {
            var observations = (await _loader.LoadAsync(csvPath)).ToList();

            if (observations.Count == 0)
            {
                Log.Warning("  Skipped: file contains no observations.");
                continue;
            }

            _observationsByFile[csvPath] = observations;

            // Process both LSTM and GRU
            foreach (var networkType in new[] { NeuralNetworkType.LSTM, NeuralNetworkType.GRU })
            {
                var fileStopwatch = Stopwatch.StartNew();
                Log.Information("================================================");
                Log.Information("Processing: {CsvPath} with {NetworkType}", csvPath, networkType);

                // Determine fixed, non-time-based output directory for JSON stats
                var jsonDirectory = !string.IsNullOrWhiteSpace(_options.OutputDirectory)
                    ? _options.OutputDirectory!
                    : Path.Combine(Path.GetDirectoryName(csvPath) ?? Directory.GetCurrentDirectory(), "ForecastStats");
                Directory.CreateDirectory(jsonDirectory);
                
                // Generate JSON filename with suffix for GRU
                var baseFileName = Path.GetFileNameWithoutExtension(csvPath);
                var jsonFileName = networkType == NeuralNetworkType.GRU 
                    ? $"{baseFileName}_gru.json"
                    : $"{baseFileName}.json";
                var jsonOutputPath = Path.Combine(jsonDirectory, jsonFileName);

                // Skip this CSV if corresponding JSON already exists
                if (File.Exists(jsonOutputPath))
                {
                    Log.Information("Stats JSON already exists for {CsvPath} with {NetworkType} at {JsonPath}. Skipping.", csvPath, networkType, jsonOutputPath);
                    continue;
                }

                using var processor = new NeuralNetworkForecastProcessor(_options, networkType);
                var result = processor.Process(observations);
                await _reportWriter.WriteAsync(csvPath, result);

                // Collect training stats and write JSON report
                var stats = processor.GetAndResetTrainingStats();
                try
                {
                    var jsonPayload = new
                    {
                        File = Path.GetFileName(csvPath),
                        NetworkType = networkType.ToString(),
                        Place = result.Place,
                        Totals = new
                        {
                            TotalRecords = result.TotalRecords,
                            TrainingRecords = result.TrainingRecords,
                            ValidationRecords = result.ValidationRecords
                        },
                        Metrics = result.Metrics is null
                            ? null
                            : new
                            {
                                MAE = result.Metrics.MeanAbsoluteError,
                                RMSE = result.Metrics.RootMeanSquareError,
                                MAPE = result.Metrics.MeanAbsolutePercentageError,
                                Samples = result.Metrics.Samples
                            },
                        NextPrediction = result.NextPrediction is null
                            ? null
                            : new
                            {
                                Timestamp = result.NextPrediction.Timestamp,
                                Temperature = result.NextPrediction.Temperature
                            },
                        Training = stats is null
                            ? null
                            : new
                            {
                                TotalTime = stats.TotalTime,
                                TotalSteps = stats.TotalSteps,
                                FinalAverageLoss = stats.FinalAverageLoss,
                                Epochs = stats.Epochs.Select(e => new
                                {
                                    e.Epoch,
                                    e.Batches,
                                    e.AverageLoss,
                                    e.EpochElapsed
                                }).ToList()
                            },
                        Processing = new
                        {
                            Duration = FormatDuration(fileStopwatch.Elapsed)
                        }
                    };

                    await using var jsonStream = File.Open(jsonOutputPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await System.Text.Json.JsonSerializer.SerializeAsync(jsonStream, jsonPayload, new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    Log.Information("Wrote training stats JSON to {JsonPath}", jsonOutputPath);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to write training stats JSON for {CsvPath} with {NetworkType}", csvPath, networkType);
                }

                fileStopwatch.Stop();
                Log.Information("Completed processing {CsvPath} with {NetworkType} in {Elapsed}.",
                    csvPath,
                    networkType,
                    FormatDuration(fileStopwatch.Elapsed));

                summaries.Add(new ForecastSummary(
                    Name: $"{Path.GetFileNameWithoutExtension(csvPath)} ({networkType})",
                    Result: result,
                    Duration: fileStopwatch.Elapsed));
            }
        }

        return summaries;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var totalHours = (int)duration.TotalHours;
        return $"{totalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
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
                    Duration = FormatDuration(s.Duration)
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

    private async Task<AggregateTrainingResult?> TrainAggregateModelAsync()
    {
        Log.Information("Step 2/2: Training aggregate model across all CSV files.");

        // Re-read all CSV files from the input path
        var csvPaths = ResolveCsvPaths(_options.InputPath).ToList();
        if (csvPaths.Count == 0)
        {
            Log.Warning("No CSV files found for aggregate training.");
            return null;
        }

        // Determine output directory for JSON stats (use first CSV file's directory as reference)
        var firstCsvPath = csvPaths.First();
        var jsonDirectory = !string.IsNullOrWhiteSpace(_options.OutputDirectory)
            ? _options.OutputDirectory!
            : Path.Combine(Path.GetDirectoryName(firstCsvPath) ?? Directory.GetCurrentDirectory(), "ForecastStats");
        Directory.CreateDirectory(jsonDirectory);

        // Load all observations from all CSV files
        var aggregated = new List<WeatherObservation>();
        foreach (var csvPath in csvPaths)
        {
            var observations = await _loader.LoadAsync(csvPath);
            foreach (var obs in observations)
            {
                aggregated.Add(new WeatherObservation(
                    csvPath,
                    obs.Timestamp,
                    obs.Temperature,
                    obs.FeatureVector.ToArray(),
                    obs.Characteristics));
            }
        }

        // Order by timestamp
        aggregated = aggregated
            .OrderBy(obs => obs.Timestamp)
            .ToList();

        if (aggregated.Count <= _options.WindowSize)
        {
            Log.Warning("Aggregate training skipped: insufficient observations ({Count}) compared to window size ({Window}).", aggregated.Count, _options.WindowSize);
            return null;
        }

        // Process both LSTM and GRU for aggregate model
        ForecastResult? bestResult = null;
        ForecastSummary? bestSummary = null;
        var aggregateStopwatch = Stopwatch.StartNew();

        foreach (var networkType in new[] { NeuralNetworkType.LSTM, NeuralNetworkType.GRU })
        {
            var networkStopwatch = Stopwatch.StartNew();
            Log.Information("Training aggregate {NetworkType} model...", networkType);

            var jsonFileName = networkType == NeuralNetworkType.GRU 
                ? "Все файлы_gru.json"
                : "Все файлы.json";
            var jsonOutputPath = Path.Combine(jsonDirectory, jsonFileName);

            // Check if the aggregate JSON file already exists
            if (File.Exists(jsonOutputPath))
            {
                Log.Information("Aggregate {NetworkType} stats JSON already exists at {JsonPath}. Skipping.", networkType, jsonOutputPath);
                continue;
            }

            using var processor = new NeuralNetworkForecastProcessor(_options, networkType);
            var result = processor.Process(aggregated);
            networkStopwatch.Stop();

            Log.Information("Aggregate {NetworkType} model trained in {Elapsed}.", networkType, FormatDuration(networkStopwatch.Elapsed));

            if (result.Metrics is null)
            {
                Log.Warning("Aggregate {NetworkType} metrics unavailable: insufficient validation rows.", networkType);
            }
            else
            {
                Log.Information("Aggregate {NetworkType} metrics -> MAE: {MAE:F2}°C, RMSE: {RMSE:F2}°C, MAPE: {MAPE}",
                    networkType,
                    result.Metrics.MeanAbsoluteError,
                    result.Metrics.RootMeanSquareError,
                    result.Metrics.MeanAbsolutePercentageError?.ToString("F2", CultureInfo.InvariantCulture) ?? "n/a");
            }

            if (result.NextPrediction is { } prediction)
            {
                Log.Information("Aggregate {NetworkType} next forecast at {Timestamp:yyyy-MM-dd HH:mm}, temperature {Temperature:F2}°C",
                    networkType,
                    prediction.Timestamp,
                    prediction.Temperature);
            }
            else
            {
                Log.Warning("Aggregate {NetworkType} next forecast unavailable (insufficient history).", networkType);
            }

            // Collect training stats and write JSON report
            var stats = processor.GetAndResetTrainingStats();
            try
            {
                var jsonPayload = new
                {
                    File = jsonFileName,
                    NetworkType = networkType.ToString(),
                    Place = result.Place,
                    Totals = new
                    {
                        TotalRecords = result.TotalRecords,
                        TrainingRecords = result.TrainingRecords,
                        ValidationRecords = result.ValidationRecords
                    },
                    Metrics = result.Metrics is null
                        ? null
                        : new
                        {
                            MAE = result.Metrics.MeanAbsoluteError,
                            RMSE = result.Metrics.RootMeanSquareError,
                            MAPE = result.Metrics.MeanAbsolutePercentageError,
                            Samples = result.Metrics.Samples
                        },
                    NextPrediction = result.NextPrediction is null
                        ? null
                        : new
                        {
                            Timestamp = result.NextPrediction.Timestamp,
                            Temperature = result.NextPrediction.Temperature
                        },
                    Training = stats is null
                        ? null
                        : new
                        {
                            TotalTime = stats.TotalTime,
                            TotalSteps = stats.TotalSteps,
                            FinalAverageLoss = stats.FinalAverageLoss,
                            Epochs = stats.Epochs.Select(e => new
                            {
                                e.Epoch,
                                e.Batches,
                                e.AverageLoss,
                                e.EpochElapsed
                            }).ToList()
                        },
                    Processing = new
                    {
                        Duration = FormatDuration(networkStopwatch.Elapsed)
                    }
                };

                await using var jsonStream = File.Open(jsonOutputPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await System.Text.Json.JsonSerializer.SerializeAsync(jsonStream, jsonPayload, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
                Log.Information("Wrote aggregate {NetworkType} training stats JSON to {JsonPath}", networkType, jsonOutputPath);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to write aggregate {NetworkType} training stats JSON", networkType);
            }

            // Keep track of the first result (LSTM) for backward compatibility
            if (bestResult is null)
            {
                bestResult = result;
                bestSummary = new ForecastSummary($"Aggregate ({networkType})", result, networkStopwatch.Elapsed);
            }
        }

        aggregateStopwatch.Stop();

        if (bestResult is null || bestSummary is null)
        {
            return null;
        }

        return new AggregateTrainingResult(bestSummary, bestResult);
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
        public string Duration { get; init; } = "-";
    }

    private sealed record AbbreviationRow(string Code, string Meaning);
}



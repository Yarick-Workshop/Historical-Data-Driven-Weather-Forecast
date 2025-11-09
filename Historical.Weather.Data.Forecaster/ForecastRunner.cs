using Historical.Weather.Data.Forecaster.IO;
using Historical.Weather.Data.Forecaster.Processing;
using Serilog;

namespace Historical.Weather.Data.Forecaster;

internal sealed class ForecastRunner
{
    private readonly ForecastOptions _options;
    private readonly WeatherCsvLoader _loader;
    private readonly ForecastReportWriter _reportWriter;

    public ForecastRunner(ForecastOptions options)
    {
        _options = options;
        _loader = new WeatherCsvLoader();
        _reportWriter = new ForecastReportWriter(options);
    }

    public async Task RunAsync()
    {
        var csvPaths = ResolveCsvPaths(_options.InputPath).ToList();

        if (csvPaths.Count == 0)
        {
            Log.Warning("No CSV files were found to process.");
            return;
        }

        Log.Information("Discovered {CsvCount} CSV file(s).", csvPaths.Count);

        foreach (var csvPath in csvPaths)
        {
            Log.Information("================================================");
            Log.Information("Processing: {CsvPath}", csvPath);

            var observations = (await _loader.LoadAsync(csvPath)).ToList();

            if (observations.Count == 0)
            {
                Log.Warning("  Skipped: file contains no observations.");
                continue;
            }

            LogDayProgress(observations);

            using var processor = new LstmForecastProcessor(_options);
            var result = processor.Process(observations);
            await _reportWriter.WriteAsync(csvPath, result);
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

        foreach (var file in Directory.EnumerateFiles(inputPath, "*.csv", SearchOption.TopDirectoryOnly).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            yield return file;
        }
    }

    private static void LogDayProgress(IReadOnlyList<WeatherObservation> observations)
    {
        if (observations.Count == 0)
        {
            return;
        }

        var ordered = observations
            .OrderBy(o => o.Timestamp)
            .ToList();

        var distinctDays = 0;
        DateTime? currentDay = null;
        foreach (var observation in ordered)
        {
            var date = observation.Timestamp.Date;
            if (currentDay != date)
            {
                currentDay = date;
                distinctDays++;

                if (distinctDays % 10 == 0)
                {
                    Log.Information("  Progress: processed {DayCount} day(s); current day {Date}", distinctDays, date);
                }
            }
        }

        Log.Information("  Total distinct days in file: {TotalDays}", distinctDays);
    }
}



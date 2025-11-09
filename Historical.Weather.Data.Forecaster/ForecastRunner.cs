using Historical.Weather.Data.Forecaster.IO;
using Historical.Weather.Data.Forecaster.Processing;

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
            Console.WriteLine("No CSV files were found to process.");
            return;
        }

        Console.WriteLine($"Discovered {csvPaths.Count} CSV file(s).");

        foreach (var csvPath in csvPaths)
        {
            Console.WriteLine();
            Console.WriteLine("================================================");
            Console.WriteLine($"Processing: {csvPath}");

            var observations = await _loader.LoadAsync(csvPath);

            if (observations.Count == 0)
            {
                Console.WriteLine("  Skipped: file contains no observations.");
                continue;
            }

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
}



using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using System.IO;
using System.Diagnostics;
using Historical.Weather.Data.Miner;
using HtmlLogWriter;

// Generate DateTime-based log file path
var logDateTime = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
var logDirectory = $"HtmlLog_{logDateTime}";
var logFilePath = Path.Combine(logDirectory, $"result{logDateTime}.html");

// Initialize Serilog for console and HTML logging
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Debug) // Console: all levels (Debug and above)
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(evt => evt.Level >= LogEventLevel.Information) // HTML: Information and above (Information, Warning, Error, Fatal)
        .WriteTo.HtmlLog(logFilePath, "Historical Weather Data Miner"))
    .CreateLogger();

try
{
    Log.Debug("Start");

    // Build configuration
    var projectDir = Path.Combine(Directory.GetCurrentDirectory(), "Historical.Weather.Data.Miner");
    var basePath = Directory.Exists(projectDir) && File.Exists(Path.Combine(projectDir, "appsettings.json")) 
        ? projectDir 
        : Directory.GetCurrentDirectory();
    var builder = new ConfigurationBuilder()
        .SetBasePath(basePath)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

    var configuration = builder.Build();

    // Get the Historical Weather Files Root path
    var rootPath = configuration["HistoricalWeatherFilesRoot"];
    
    if (string.IsNullOrWhiteSpace(rootPath))
    {
        Log.Error("HistoricalWeatherFilesRoot is not configured in appsettings.json");
        return;
    }

    Log.Debug("Reading files from: {RootPath}", rootPath);

    // Check if directory exists
    if (!Directory.Exists(rootPath))
    {
        Log.Error("Directory does not exist: {RootPath}", rootPath);
        return;
    }

    // Scan all files in the directory
    var files = Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories);

    // Initialize counters and timing
    var totalStopwatch = Stopwatch.StartNew();
    int parsingSuccessfulCount = 0;
    int parsingUnsuccessfulCount = 0;
    long totalFileProcessingTime = 0;

    // Initialize HTML parser
    var htmlParser = new RealWeatherHtmlParser();

    // Collect all parse results with their file paths
    var rawParseResultsWithPaths = new List<(string FilePath, HtmlParseResult Result)>();

    // Prepare expected observation times (00:00 to 21:00 with 3-hour step)
    var expectedObservationTimes = Enumerable.Range(0, 8)
        .Select(i => TimeSpan.FromHours(i * 3))
        .ToList();

    int missingTimeEntriesCount = 0;

    // Parse each HTML file
    foreach (var file in files)
    {
        Log.Debug("File found: {FilePath}", file);
        
        var fileStopwatch = Stopwatch.StartNew();
        
        try
        {
            // Parse the HTML file (all parsing logic is inside ParseFile)
            var result = htmlParser.ParseFile(file);

            rawParseResultsWithPaths.Add((file, result));
            
            parsingSuccessfulCount++;
            Log.Debug("Successfully parsed HTML file: {FilePath}", file);
        }
        catch (Exception ex)
        {
            parsingUnsuccessfulCount++;
            Log.Error(ex, "Failed to parse HTML file: {FilePath}", file);
        }
        finally
        {
            fileStopwatch.Stop();
            totalFileProcessingTime += fileStopwatch.ElapsedMilliseconds;
        }
    }

    // Perform observation time normalization after parsing all files
    var normalizedParseResultsWithPaths = new List<(string FilePath, HtmlParseResult Result)>();
    int normalizationSuccessfulCount = 0;
    int normalizationUnsuccessfulCount = 0;

    foreach (var (filePath, parseResult) in rawParseResultsWithPaths)
    {
        try
        {
            var normalizedResult = NormalizeObservationTimesOrThrow(filePath, parseResult, expectedObservationTimes, ref missingTimeEntriesCount);
            normalizedParseResultsWithPaths.Add((filePath, normalizedResult));
            normalizationSuccessfulCount++;
        }
        catch (Exception normalizationException)
        {
            normalizationUnsuccessfulCount++;
            Log.Error(normalizationException, "Failed to normalize HTML file: {FilePath}", filePath);
        }
    }

    totalStopwatch.Stop();

    // Log statistics
    var totalTime = totalStopwatch.Elapsed.TotalSeconds;
    var averageTime = files.Length > 0 ? (totalFileProcessingTime / (double)files.Length) / 1000.0 : 0;

    Log.Debug("Finish");
    Log.Information("Processing Statistics:");
    Log.Information("  Total files processed: {TotalFiles}", files.Length);
    Log.Information("  Parsing successful: {ParsingSuccessfulCount}", parsingSuccessfulCount);
    Log.Information("  Parsing unsuccessful: {ParsingUnsuccessfulCount}", parsingUnsuccessfulCount);
    Log.Information("  Normalization successful: {NormalizationSuccessfulCount}", normalizationSuccessfulCount);
    Log.Information("  Normalization unsuccessful: {NormalizationUnsuccessfulCount}", normalizationUnsuccessfulCount);
    Log.Information("  Total processing time: {TotalTime:F2} seconds", totalTime);
    Log.Information("  Average time per file: {AverageTime:F3} seconds", averageTime);
    Log.Information("  Files missing expected observation times: {MissingTimeEntriesCount}", missingTimeEntriesCount);

    // Write tables to HTML
    using (var htmlWriter = new HtmlLogWriter.HtmlLogWriter(logFilePath, "Historical Weather Data Miner"))
    {
        WriteRowCountDistributionTable(htmlWriter, normalizedParseResultsWithPaths);
        
        // Create distribution diagram from the row counts
        var rowCounts = normalizedParseResultsWithPaths.Select(r => (double)r.Result.WeatherDataRows.Count).ToList();
        if (rowCounts.Count > 0)
        {
            htmlWriter.WriteDistributionDiagram(rowCounts, "Distribution of Row List Counts");
        }
        
        WriteTimesDistributionTable(htmlWriter, normalizedParseResultsWithPaths);
    }
}
catch (Exception ex)
{
    Log.Error(ex, "An error occurred");
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>
/// Generates and writes the row count distribution table to HTML.
/// </summary>
/// <param name="writer">The HTML writer instance.</param>
/// <param name="parseResultsWithPaths">List of parse results with their file paths.</param>
static void WriteRowCountDistributionTable(HtmlLogWriter.HtmlLogWriter writer, List<(string FilePath, HtmlParseResult Result)> parseResultsWithPaths)
{
    var tableData = parseResultsWithPaths
        .GroupBy(r => r.Result.WeatherDataRows.Count)
        .Select(g => new
        {
            RealWeatherDataRowsCount = g.Key,
            ParsedFilesCount = g.Count()
        })
        .OrderBy(x => x.RealWeatherDataRowsCount)
        .ToList();
    
    writer.WriteTable(tableData, "Row List Count Distribution");
}

/// <summary>
/// Generates and writes the times distribution table to HTML.
/// </summary>
/// <param name="writer">The HTML writer instance.</param>
/// <param name="parseResultsWithPaths">List of parse results with their file paths.</param>
static void WriteTimesDistributionTable(HtmlLogWriter.HtmlLogWriter writer, List<(string FilePath, HtmlParseResult Result)> parseResultsWithPaths)
{
    var tableData = parseResultsWithPaths
        .Select(r =>
        {
            var fileTimes = r.Result.WeatherDataRows
                .Select(row => row.Time.TimeOfDay)
                .OrderBy(t => t)
                .ToList();
            var timesKey = string.Join(",", fileTimes.Select(t => t.ToString(@"hh\:mm")));
            return new { TimesKey = timesKey, Result = r };
        })
        .GroupBy(x => x.TimesKey)
        .Select(g =>
        {
            var results = g.Select(x => x.Result).ToList();
            var dates = results
                .Where(r => !string.IsNullOrEmpty(r.Result.Date))
                .Select(r => DateTime.ParseExact(r.Result.Date!, "yyyy-MM-dd", null))
                .ToList();
            
            // Get the times list (all should be the same since we grouped by the key)
            var timesList = results.First().Result.WeatherDataRows
                .Select(row => row.Time.TimeOfDay)
                .OrderBy(t => t)
                .ToList();
            
            // Get rows count (all files in the group should have the same number of rows)
            var rowsCount = results.First().Result.WeatherDataRows.Count;
            
            // Get parsed files count
            var parsedFilesCount = results.Count;
            
            // Get example URIs (1 if only 1 file, 2 if more)
            var exampleUris = results
                .Take(results.Count == 1 ? 1 : 2)
                .Select(r => r.FilePath)
                .ToList();
            
            // Convert file paths to HTML links
            var exampleLinks = exampleUris.Select(filePath =>
            {
                var fileName = Path.GetFileName(filePath);
                // Convert absolute file path to file:// URI
                var fileUri = Path.IsPathRooted(filePath) 
                    ? new Uri(filePath).AbsoluteUri 
                    : new Uri(Path.GetFullPath(filePath)).AbsoluteUri;
                return $"<a href=\"{fileUri}\" target=\"_blank\">{fileName}</a>";
            }).ToList();
            
            return new
            {
                Times = string.Join(", ", timesList.Select(t => t.ToString(@"hh\:mm"))),
                RowsCount = rowsCount,
                ParsedFilesCount = parsedFilesCount,
                MinimumDate = dates.Any() ? dates.Min().ToString("yyyy-MM-dd") : (string?)null,
                MaximumDate = dates.Any() ? dates.Max().ToString("yyyy-MM-dd") : (string?)null,
                ExampleUris = string.Join(", ", exampleLinks)
            };
        })
        .OrderBy(x => x.MinimumDate)
        .ToList();
    
    writer.WriteTable(tableData, "Times Distribution");
}

static HtmlParseResult NormalizeObservationTimesOrThrow(
    string filePath,
    HtmlParseResult parseResult,
    IReadOnlyCollection<TimeSpan> expectedObservationTimes,
    ref int missingTimeEntriesCount)
{
    var rowsByTime = parseResult.WeatherDataRows
        .GroupBy(row => row.Time.TimeOfDay)
        .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Time).ToList());

    var normalizedRows = new List<WeatherDataRow>(expectedObservationTimes.Count);
    var missingTimes = new List<TimeSpan>();

    foreach (var expectedTime in expectedObservationTimes)
    {
        if (!rowsByTime.TryGetValue(expectedTime, out var rowsForTime) || rowsForTime.Count == 0)
        {
            missingTimes.Add(expectedTime);
            continue;
        }

        if (rowsForTime.Count > 1)
        {
            var duplicatesFormatted = string.Join(", ", rowsForTime.Select(r => r.Time.ToString("yyyy-MM-dd HH:mm")));
            Log.Warning("Multiple observations found for {ObservationTime} in file {FilePath}. Using the first occurrence. Entries: {Entries}",
                expectedTime.ToString(@"hh\:mm"), filePath, duplicatesFormatted);
        }

        normalizedRows.Add(rowsForTime[0]);
    }

    if (missingTimes.Count > 0)
    {
        missingTimeEntriesCount++;
        var missingTimesFormatted = string.Join(", ", missingTimes.Select(ts => ts.ToString(@"hh\:mm")));
        throw new InvalidOperationException($"Missing expected observation times ({missingTimesFormatted}) in file '{filePath}'.");
    }

    var extraObservationTimes = rowsByTime.Keys
        .Where(time => !expectedObservationTimes.Contains(time))
        .OrderBy(time => time)
        .Select(time => time.ToString(@"hh\:mm"))
        .ToList();

    if (extraObservationTimes.Count > 0)
    {
        Log.Debug("Ignoring extra observation times {ExtraObservationTimes} in file {FilePath}",
            string.Join(", ", extraObservationTimes), filePath);
    }

    normalizedRows.Sort((left, right) => left.Time.CompareTo(right.Time));

    if (normalizedRows.Count != expectedObservationTimes.Count)
    {
        throw new InvalidOperationException(
            $"Unexpected row count after normalization. Expected {expectedObservationTimes.Count}, got {normalizedRows.Count} in file '{filePath}'.");
    }

    return new HtmlParseResult(parseResult.CityName, parseResult.Date, new List<WeatherDataRow>(normalizedRows));
}

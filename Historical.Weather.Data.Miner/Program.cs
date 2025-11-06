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
    .WriteTo.Console()
    .WriteTo.HtmlLog(logFilePath, "Historical Weather Data Miner")
        .Filter.ByIncludingOnly(evt => 
            evt.Level == LogEventLevel.Information || 
            evt.Level == LogEventLevel.Error || 
            evt.Level == LogEventLevel.Fatal)//TODO, refactor to make configurable
    .MinimumLevel.Debug()
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
    int successfulCount = 0;
    int unsuccessfulCount = 0;
    long totalFileProcessingTime = 0;

    // Initialize HTML parser
    var htmlParser = new RealWeatherHtmlParser();

    // Collect all parse results with their file paths
    var parseResultsWithPaths = new List<(string FilePath, HtmlParseResult Result)>();

    // Parse each HTML file
    foreach (var file in files)
    {
        Log.Debug("File found: {FilePath}", file);
        
        var fileStopwatch = Stopwatch.StartNew();
        
        try
        {
            // Parse the HTML file (all parsing logic is inside ParseFile)
            var result = htmlParser.ParseFile(file);
            parseResultsWithPaths.Add((file, result));
            
            successfulCount++;
            Log.Debug("Successfully parsed HTML file: {FilePath}", file);
        }
        catch (Exception ex)
        {
            unsuccessfulCount++;
            Log.Error(ex, "Failed to parse HTML file: {FilePath}", file);
        }
        finally
        {
            fileStopwatch.Stop();
            totalFileProcessingTime += fileStopwatch.ElapsedMilliseconds;
        }
    }

    totalStopwatch.Stop();

    // Log statistics
    var totalTime = totalStopwatch.Elapsed.TotalSeconds;
    var averageTime = files.Length > 0 ? (totalFileProcessingTime / (double)files.Length) / 1000.0 : 0;

    Log.Debug("Finish");
    Log.Information("Processing Statistics:");
    Log.Information("  Total files processed: {TotalFiles}", files.Length);
    Log.Information("  Successful: {SuccessfulCount}", successfulCount);
    Log.Information("  Unsuccessful: {UnsuccessfulCount}", unsuccessfulCount);
    Log.Information("  Total processing time: {TotalTime:F2} seconds", totalTime);
    Log.Information("  Average time per file: {AverageTime:F3} seconds", averageTime);

    // Write tables to HTML
    using (var htmlWriter = new HtmlLogWriter.HtmlLogWriter(logFilePath, "Historical Weather Data Miner"))
    {
        WriteRowCountDistributionTable(htmlWriter, parseResultsWithPaths);
        
        // Create distribution diagram from the row counts
        var rowCounts = parseResultsWithPaths.Select(r => (double)r.Result.WeatherDataRows.Count).ToList();
        if (rowCounts.Count > 0)
        {
            htmlWriter.WriteDistributionDiagram(rowCounts, "Distribution of Row List Counts");
        }
        
        WriteTimesDistributionTable(htmlWriter, parseResultsWithPaths);
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
            
            // Get example URIs (1 if only 1 file, 2 if more)
            var exampleUris = results
                .Take(results.Count == 1 ? 1 : 2)
                .Select(r => r.FilePath)
                .ToList();
            
            return new
            {
                Times = string.Join(", ", timesList.Select(t => t.ToString(@"hh\:mm"))),
                MinimumDate = dates.Any() ? dates.Min().ToString("yyyy-MM-dd") : (string?)null,
                MaximumDate = dates.Any() ? dates.Max().ToString("yyyy-MM-dd") : (string?)null,
                ExampleUris = string.Join("; ", exampleUris)
            };
        })
        .OrderBy(x => x.Times)
        .ToList();
    
    writer.WriteTable(tableData, "Times Distribution");
}

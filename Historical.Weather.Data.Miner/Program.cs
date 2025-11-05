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
            evt.Level == LogEventLevel.Fatal)
    .MinimumLevel.Debug()
    .CreateLogger();

try
{
    Log.Debug("Start");

    // Build configuration
    var builder = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
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

    // Collect all parse results
    var parseResults = new List<HtmlParseResult>();

    // Parse each HTML file
    foreach (var file in files)
    {
        Log.Debug("File found: {FilePath}", file);
        
        var fileStopwatch = Stopwatch.StartNew();
        
        try
        {
            // Parse the HTML file (all parsing logic is inside ParseFile)
            var result = htmlParser.ParseFile(file);
            parseResults.Add(result);
            
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

    // Group by RowListCount and calculate count for each group
    var groupedResults = parseResults
        .GroupBy(r => r.WeatherDataRows.Count)
        .Select(g => new
        {
            RowListCount = g.Key,
            Count = g.Count()
        })
        .OrderBy(x => x.RowListCount)
        .ToList();

    // Write the anonymous type list to the table
    using (var htmlWriter = new HtmlLogWriter(logFilePath, "Historical Weather Data Miner"))
    {
        htmlWriter.WriteTable(groupedResults, "Row List Count Distribution");
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

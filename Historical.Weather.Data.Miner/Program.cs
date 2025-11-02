using Microsoft.Extensions.Configuration;
using Serilog;
using System.IO;
using HtmlAgilityPack;
using System.Diagnostics;

// Initialize Serilog for console logging
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
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

    // Parse each HTML file
    foreach (var file in files)
    {
        Log.Debug("File found: {FilePath}", file);
        
        var fileStopwatch = Stopwatch.StartNew();
        
        try
        {
            // Load and parse the HTML file
            var doc = new HtmlDocument();
            doc.OptionCheckSyntax = true;
            doc.Load(file);
            
            // Check for parsing errors
            if (doc.ParseErrors != null && doc.ParseErrors.Count() > 0)
            {
                var errors = string.Join("; ", doc.ParseErrors.Select(e => $"{e.Code}: {e.Reason} (Line {e.Line}, Column {e.LinePosition})"));
                throw new InvalidOperationException($"Malformed HTML in file '{file}': {errors}");
            }
            
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
}
catch (Exception ex)
{
    Log.Error(ex, "An error occurred");
}
finally
{
    Log.CloseAndFlush();
}


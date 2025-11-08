using Historical.Weather.Data.Miner;
using HtmlLogWriter;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Globalization;

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
    Log.Information("Start");

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

    // Initialize HTML parser
    var htmlParser = new RealWeatherHtmlParser();

    // Collect all parse results with their file paths
    var rawParseResultsWithPaths = ParseFiles(
        files,
        htmlParser,
        out var parsingSuccessfulCount,
        out var parsingUnsuccessfulCount,
        out var totalFileProcessingTime);

    var rawParseResultsByPlace = OrganizeParseResultsByPlaceAndDate(rawParseResultsWithPaths);
    var flattenedRawParseResults = FlattenParseResults(rawParseResultsByPlace).ToList();

    // Prepare expected observation times (00:00 to 21:00 with 3-hour step)
    var expectedObservationTimes = Enumerable.Range(0, 8)
        .Select(i => TimeSpan.FromHours(i * 3))
        .ToList();
    
    totalStopwatch.Stop();

    // Log parsing statistics
    var totalTime = totalStopwatch.Elapsed.TotalSeconds;
    var averageTime = files.Length > 0 ? (totalFileProcessingTime / (double)files.Length) / 1000.0 : 0;

    Log.Information("Parsing Statistics:");
    Log.Information("  Total files processed: {TotalFiles}", files.Length);
    Log.Information("  Parsing successful: {ParsingSuccessfulCount}", parsingSuccessfulCount);
    Log.Information("  Parsing unsuccessful: {ParsingUnsuccessfulCount}", parsingUnsuccessfulCount);
    Log.Information("  Total parsing time: {TotalTime:F2} seconds", totalTime);
    Log.Information("  Average time per file: {AverageTime:F3} seconds", averageTime);

    LogAllKnownWeatherCharacteristics();

    var normalizedParseResultsByPlace = NormalizeParseResults(
        rawParseResultsByPlace,
        expectedObservationTimes,
        out var missingTimeEntriesCount,
        out var normalizationSuccessfulCount,
        out var normalizationUnsuccessfulCount);

    Log.Information("Normalization Statistics:");
    Log.Information("  Normalization successful: {NormalizationSuccessfulCount}", normalizationSuccessfulCount);
    Log.Information("  Normalization unsuccessful: {NormalizationUnsuccessfulCount}", normalizationUnsuccessfulCount);
    Log.Information("  Files missing expected observation times: {MissingTimeEntriesCount}", missingTimeEntriesCount);

    Log.Information("Finish");

    WeatherDataCsvWriter.WriteNormalizedResultsByPlace(
        normalizedParseResultsByPlace,
        logDirectory);

    // Write tables to HTML
    using (var htmlWriter = new HtmlLogWriter.HtmlLogWriter(logFilePath, "Historical Weather Data Miner"))
    {
        WriteRowCountDistributionTable(htmlWriter, flattenedRawParseResults);

        // Create distribution diagram from the row counts
        var rowCounts = flattenedRawParseResults.Select(r => (double)r.Result.WeatherDataRows.Count).ToList();
        if (rowCounts.Count > 0)
        {
            htmlWriter.WriteDistributionDiagram(rowCounts, "Distribution of Row List Counts");
        }
        WriteTimesDistributionTable(htmlWriter, flattenedRawParseResults);
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
/// <param name="parseResults">Collection of parsed file information entries.</param>
static void WriteRowCountDistributionTable(HtmlLogWriter.HtmlLogWriter writer, IEnumerable<ParsedFileInfo> parseResults)
{
    var tableData = parseResults
        .GroupBy(info => info.Result.WeatherDataRows.Count)
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
/// <param name="parseResults">Collection of parsed file information entries.</param>
static void WriteTimesDistributionTable(HtmlLogWriter.HtmlLogWriter writer, IEnumerable<ParsedFileInfo> parseResults)
{
    var tableData = parseResults
        .Select(info =>
        {
            var fileTimes = info.Result.WeatherDataRows
                .Select(row => row.Time.TimeOfDay)
                .OrderBy(t => t)
                .ToList();
            var timesKey = string.Join(",", fileTimes.Select(t => t.ToString(@"hh\:mm")));
            return new { TimesKey = timesKey, Info = info, FileTimes = fileTimes };
        })
        .GroupBy(x => x.TimesKey)
        .Select(g =>
        {
            var infos = g.Select(x => x.Info).ToList();
            var timesList = g.First().FileTimes;

            var rowsCount = infos.First().Result.WeatherDataRows.Count;
            var parsedFilesCount = infos.Count;
            var dates = infos.Select(info => info.Date).ToList();

            var exampleUris = infos
                .Take(parsedFilesCount == 1 ? 1 : 2)
                .Select(info => info.FilePath)
                .ToList();
            
            var exampleLinks = exampleUris.Select(filePath =>
            {
                var fileName = Path.GetFileName(filePath);
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

static List<(string FilePath, HtmlParseResult Result)> ParseFiles(
    string[] files,
    RealWeatherHtmlParser htmlParser,
    out int parsingSuccessfulCount,
    out int parsingUnsuccessfulCount,
    out long totalFileProcessingTime)
{
    var rawParseResultsWithPaths = new List<(string FilePath, HtmlParseResult Result)>();
    parsingSuccessfulCount = 0;
    parsingUnsuccessfulCount = 0;
    totalFileProcessingTime = 0;

    foreach (var file in files)
    {
        Log.Debug("File found: {FilePath}", file);

        var fileStopwatch = Stopwatch.StartNew();

        try
        {
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

    return rawParseResultsWithPaths;
}

static Dictionary<string, SortedDictionary<DateTime, (string FilePath, HtmlParseResult Result)>> OrganizeParseResultsByPlaceAndDate(
    IEnumerable<(string FilePath, HtmlParseResult Result)> parseResults)
{
    var structuredResults = new Dictionary<string, SortedDictionary<DateTime, (string FilePath, HtmlParseResult Result)>>(StringComparer.OrdinalIgnoreCase);

    foreach (var (filePath, result) in parseResults)
    {
        var place = string.IsNullOrWhiteSpace(result.CityName) ? "Unknown" : result.CityName!.Trim();

        if (string.IsNullOrWhiteSpace(result.Date) ||
            !DateTime.TryParseExact(result.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            Log.Warning("Skipping file {FilePath} due to missing or invalid date value '{DateValue}'.", filePath, result.Date);
            continue;
        }

        if (!structuredResults.TryGetValue(place, out var resultsByDate))
        {
            resultsByDate = new SortedDictionary<DateTime, (string FilePath, HtmlParseResult Result)>();
            structuredResults[place] = resultsByDate;
        }

        if (resultsByDate.ContainsKey(parsedDate))
        {
            Log.Warning("Duplicate entry detected for {Place} on {Date}. Overwriting previous data with file {FilePath}.", place, parsedDate.ToString("yyyy-MM-dd"), filePath);
        }

        resultsByDate[parsedDate] = (filePath, result);
    }

    return structuredResults;
}

static IEnumerable<ParsedFileInfo> FlattenParseResults(
    Dictionary<string, SortedDictionary<DateTime, (string FilePath, HtmlParseResult Result)>> parseResultsByPlace)
{
    foreach (var (place, resultsByDate) in parseResultsByPlace)
    {
        foreach (var (date, entry) in resultsByDate)
        {
            yield return new ParsedFileInfo(place, date, entry.FilePath, entry.Result);
        }
    }
}

static Dictionary<string, SortedDictionary<DateTime, (string FilePath, HtmlParseResult Result)>> NormalizeParseResults(
    Dictionary<string, SortedDictionary<DateTime, (string FilePath, HtmlParseResult Result)>> rawParseResultsByPlace,
    IReadOnlyCollection<TimeSpan> expectedObservationTimes,
    out int missingTimeEntriesCount,
    out int normalizationSuccessfulCount,
    out int normalizationUnsuccessfulCount)
{
    var normalizedParseResultsByPlace = new Dictionary<string, SortedDictionary<DateTime, (string FilePath, HtmlParseResult Result)>>(StringComparer.OrdinalIgnoreCase);
    missingTimeEntriesCount = 0;
    normalizationSuccessfulCount = 0;
    normalizationUnsuccessfulCount = 0;

    foreach (var (place, dateEntries) in rawParseResultsByPlace)
    {
        var normalizedDateEntries = new SortedDictionary<DateTime, (string FilePath, HtmlParseResult Result)>();
        normalizedParseResultsByPlace[place] = normalizedDateEntries;

        var orderedDates = dateEntries.Keys.OrderBy(date => date).ToList();

        for (var index = 0; index < orderedDates.Count; index++)
        {
            var date = orderedDates[index];
            var currentEntry = dateEntries[date];

            HtmlParseResult? previousDayResult = null;
            if (index > 0)
            {
                var previousDate = orderedDates[index - 1];
                if (normalizedDateEntries.TryGetValue(previousDate, out var normalizedPreviousEntry))
    {
                    previousDayResult = normalizedPreviousEntry.Result;
                }
                else
                {
                    previousDayResult = dateEntries[previousDate].Result;
                }
            }

            HtmlParseResult? nextDayResult = null;
            if (index < orderedDates.Count - 1)
            {
                var nextDate = orderedDates[index + 1];
                nextDayResult = dateEntries[nextDate].Result;
            }

            HtmlParseResult? normalizedResult = null;

            try
            {
                normalizedResult = NormalizeObservationTimesOrThrow(currentEntry.FilePath, currentEntry.Result, expectedObservationTimes, ref missingTimeEntriesCount);
            }
            catch (Exception normalizationException)
            {
                if (TryInterpolateMissingObservationTimes(currentEntry.FilePath, currentEntry.Result, expectedObservationTimes, previousDayResult, nextDayResult, out var interpolatedResult))
                {
                    normalizedResult = interpolatedResult;
                    Log.Information("Normalized file {FilePath} using interpolation for missing observations.", currentEntry.FilePath);
                }
                else
                {
                    normalizationUnsuccessfulCount++;
                    Log.Error(normalizationException, "Unable to normalize HTML file {FilePath} even after interpolation attempt.", currentEntry.FilePath);
                    continue;
                }
            }

            normalizedDateEntries[date] = (currentEntry.FilePath, normalizedResult);
            normalizationSuccessfulCount++;
        }
    }

    return normalizedParseResultsByPlace;
}


static void LogAllKnownWeatherCharacteristics()
{
    var knownCharacteristics = WeatherCharacteristicConverter.GetAllKnownCharacteristics();

    if (knownCharacteristics.Count == 0)
    {
        Log.Warning("No known weather characteristics are registered in the converter.");
        return;
    }

    Log.Information("Known weather characteristics (ordered): {WeatherCharacteristics}", string.Join(", ", knownCharacteristics));
    Log.Information("Total known weather characteristics: {WeatherCharacteristicsCount}", knownCharacteristics.Count);
}

static bool TryInterpolateMissingObservationTimes(
    string filePath,
    HtmlParseResult parseResult,
    IReadOnlyCollection<TimeSpan> expectedObservationTimes,
    HtmlParseResult? previousDayResult,
    HtmlParseResult? nextDayResult,
    out HtmlParseResult interpolatedResult)
{
    interpolatedResult = default!;

    if (parseResult.WeatherDataRows == null || parseResult.WeatherDataRows.Count == 0)
    {
        return false;
    }

    var rowsGroupedByTime = parseResult.WeatherDataRows
        .GroupBy(row => row.Time.TimeOfDay)
        .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Time).First());

    if (rowsGroupedByTime.Count == 0)
    {
        return false;
    }

    var candidateRows = new List<WeatherDataRow>(rowsGroupedByTime.Count
        + (previousDayResult?.WeatherDataRows?.Count ?? 0)
        + (nextDayResult?.WeatherDataRows?.Count ?? 0));

    candidateRows.AddRange(rowsGroupedByTime.Values);

    if (previousDayResult?.WeatherDataRows != null)
    {
        candidateRows.AddRange(previousDayResult.WeatherDataRows);
    }

    if (nextDayResult?.WeatherDataRows != null)
    {
        candidateRows.AddRange(nextDayResult.WeatherDataRows);
    }

    candidateRows = candidateRows
        .Where(row => row != null)
        .OrderBy(row => row.Time)
        .ToList();

    if (candidateRows.Count < 2)
    {
        return false;
    }

    var firstExistingRow = rowsGroupedByTime.First().Value;
    var baseDate = DetermineBaseDate(parseResult, firstExistingRow);
    var sortedExpectedTimes = expectedObservationTimes.OrderBy(time => time).ToList();

    var normalizedRows = new List<WeatherDataRow>(sortedExpectedTimes.Count);

    foreach (var expectedTime in sortedExpectedTimes)
    {
        if (rowsGroupedByTime.TryGetValue(expectedTime, out var existingRow))
        {
            normalizedRows.Add(existingRow);
            continue;
        }

        if (!TryCreateInterpolatedRow(expectedTime, baseDate, candidateRows, out var interpolatedRow))
        {
            Log.Debug("Interpolation not possible for observation time {ObservationTime} in file {FilePath}", expectedTime.ToString(@"hh\:mm"), filePath);
            return false;
        }

        normalizedRows.Add(interpolatedRow);
    }

    normalizedRows.Sort((left, right) => left.Time.CompareTo(right.Time));
    interpolatedResult = new HtmlParseResult(parseResult.CityName, parseResult.Date, normalizedRows);
    return true;
}

static DateTime DetermineBaseDate(HtmlParseResult parseResult, WeatherDataRow referenceRow)
{
    if (!string.IsNullOrWhiteSpace(parseResult.Date) &&
        DateTime.TryParseExact(parseResult.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
    {
        return parsedDate;
    }

    return referenceRow.Time.Date;
}

static bool TryCreateInterpolatedRow(
    TimeSpan targetTime,
    DateTime baseDate,
    IReadOnlyList<WeatherDataRow> orderedCandidateRows,
    out WeatherDataRow interpolatedRow)
{
    interpolatedRow = default!;

    var targetDateTime = baseDate.Add(targetTime);

    var previousRow = orderedCandidateRows.LastOrDefault(row => row.Time < targetDateTime);
    var nextRow = orderedCandidateRows.FirstOrDefault(row => row.Time > targetDateTime);

    if (previousRow == null || nextRow == null)
    {
        return false;
}

    var totalMinutes = (nextRow.Time - previousRow.Time).TotalMinutes;
    if (totalMinutes <= 0)
    {
        return false;
    }

    var elapsedMinutes = (targetDateTime - previousRow.Time).TotalMinutes;
    if (elapsedMinutes < 0 || elapsedMinutes > totalMinutes)
    {
        return false;
    }

    var ratio = elapsedMinutes / totalMinutes;

    var temperature = InterpolateInt(previousRow.Temperature, nextRow.Temperature, ratio);
    var windSpeed = InterpolateDecimal(previousRow.WindSpeed, nextRow.WindSpeed, ratio);
    var atmosphericPressure = InterpolateInt(previousRow.AtmosphericPressure, nextRow.AtmosphericPressure, ratio);
    var humidity = InterpolateInt(previousRow.Humidity, nextRow.Humidity, ratio);

    var windDirection = ratio <= 0.5 ? previousRow.WindDirection : nextRow.WindDirection;
    var characteristics = previousRow.WeatherCharacteristics | nextRow.WeatherCharacteristics;

    interpolatedRow = new WeatherDataRow(
        targetDateTime,
        characteristics,
        temperature,
        windDirection,
        windSpeed,
        atmosphericPressure,
        humidity);

    return true;
}

static int InterpolateInt(int previousValue, int nextValue, double ratio)
{
    var interpolatedValue = previousValue + (nextValue - previousValue) * ratio;
    return (int)Math.Round(interpolatedValue, MidpointRounding.AwayFromZero);
}

static decimal InterpolateDecimal(decimal previousValue, decimal nextValue, double ratio)
{
    var ratioDecimal = (decimal)ratio;
    var interpolatedValue = previousValue + (nextValue - previousValue) * ratioDecimal;
    return decimal.Round(interpolatedValue, 2, MidpointRounding.AwayFromZero);
}

file sealed record ParsedFileInfo(string Place, DateTime Date, string FilePath, HtmlParseResult Result);

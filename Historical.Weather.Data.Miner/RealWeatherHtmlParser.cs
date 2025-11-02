using HtmlAgilityPack;
using System.Text.RegularExpressions;
using Serilog;
using System.Globalization;
using System.IO;

namespace Historical.Weather.Data.Miner;

public class RealWeatherHtmlParser
{
    /// <summary>
    /// Compiled regex pattern to extract city name from title.
    /// Pattern matches "Архив погоды в [CityName]" where CityName is in Cyrillic characters.
    /// Handles city names that may contain hyphens (e.g., Ивано-Франковск) or consist of a single word.
    /// </summary>
    private static readonly Regex CityNameRegex = new Regex(
        @"Архив\s+погоды\s+в\s+(.*?)\.\s+Погода",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Compiled regex pattern to extract date from title.
    /// Pattern matches "за [Day] [Month] [Year] года" where Month is in Russian.
    /// Example: "за 10 декабрь 2016 года"
    /// </summary>
    private static readonly Regex DateRegex = new Regex(
        @"за\s+(\d+)\s+(январь|февраль|март|апрель|май|июнь|июль|август|сентябрь|октябрь|ноябрь|декабрь)\s+(\d{4})\s+года",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Russian month names mapped to month numbers (1-12).
    /// </summary>
    private static readonly Dictionary<string, int> RussianMonths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        { "январь", 1 },
        { "февраль", 2 },
        { "март", 3 },
        { "апрель", 4 },
        { "май", 5 },
        { "июнь", 6 },
        { "июль", 7 },
        { "август", 8 },
        { "сентябрь", 9 },
        { "октябрь", 10 },
        { "ноябрь", 11 },
        { "декабрь", 12 }
    };

    /// <summary>
    /// Loads and parses an HTML file, checking for syntax errors.
    /// Extracts the city name from the title element (title handling is encapsulated internally).
    /// </summary>
    /// <param name="filePath">The path to the HTML file to parse.</param>
    /// <returns>An HtmlParseResult containing the extracted city name.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the HTML file is malformed or title is missing.</exception>
    public HtmlParseResult ParseFile(string filePath)
    {
        // Load and parse the HTML file
        var doc = new HtmlDocument();
        doc.OptionCheckSyntax = true;
        doc.Load(filePath);
        
        // Check for parsing errors
        if (doc.ParseErrors != null && doc.ParseErrors.Count() > 0)
        {
            var errors = string.Join("; ", doc.ParseErrors.Select(e => $"{e.Code}: {e.Reason} (Line {e.Line}, Column {e.LinePosition})"));
            throw new InvalidOperationException($"Malformed HTML in file '{filePath}': {errors}");
        }

        // Extract city name from title using XPath and compiled regex
        var cityName = ExtractSingleMatchByXPathAndRegex(
            doc,
            "/html/head/title",
            CityNameRegex,
            filePath);

        Log.Debug("  Extracted city: {CityName}", cityName);

        // Extract date from title - get title text and apply date regex
        var titleElement = doc.DocumentNode.SelectSingleNode("/html/head/title");
        if (titleElement == null)
        {
            throw new InvalidOperationException($"Title element not found in file '{filePath}'");
        }
        
        var titleText = titleElement.InnerText?.Trim();
        if (string.IsNullOrWhiteSpace(titleText))
        {
            throw new InvalidOperationException($"Title element has empty or null text in file '{filePath}'");
        }

        // Parse date to YYYY-MM-dd format
        var parsedDate = ParseRussianDateToYYYYMMDD(titleText, filePath);

        // Extract date from filename
        var dateFromFilename = ExtractDateFromFilename(filePath);

        // Self-test: compare dates
        if (parsedDate != dateFromFilename)
        {
            throw new InvalidOperationException(
                $"Date mismatch in file '{filePath}': Date from title '{parsedDate}' does not match date from filename '{dateFromFilename}'");
        }

        Log.Debug("  Extracted date: {Date} (validated against filename)", parsedDate);

        return new HtmlParseResult(cityName, parsedDate);
    }

    /// <summary>
    /// Parses a Russian date string (e.g., "10 декабрь 2016") to YYYY-MM-dd format.
    /// </summary>
    /// <param name="dateString">The date string in format "day month_name year" (e.g., "10 декабрь 2016").</param>
    /// <param name="filePath">The file path for error reporting.</param>
    /// <returns>Date in YYYY-MM-dd format (e.g., "2016-12-10").</returns>
    private string ParseRussianDateToYYYYMMDD(string dateString, string filePath)
    {
        var match = DateRegex.Match(dateString);
        if (!match.Success || match.Groups.Count < 4)
        {
            throw new InvalidOperationException(
                $"Failed to parse date from string '{dateString}' in file '{filePath}'");
        }

        var day = match.Groups[1].Value;
        var monthName = match.Groups[2].Value;
        var year = match.Groups[3].Value;

        if (!RussianMonths.TryGetValue(monthName, out var month))
        {
            throw new InvalidOperationException(
                $"Unknown Russian month name '{monthName}' in file '{filePath}'");
        }

        if (!int.TryParse(day, out var dayInt) || dayInt < 1 || dayInt > 31)
        {
            throw new InvalidOperationException(
                $"Invalid day value '{day}' in file '{filePath}'");
        }

        if (!int.TryParse(year, out var yearInt))
        {
            throw new InvalidOperationException(
                $"Invalid year value '{year}' in file '{filePath}'");
        }

        try
        {
            var date = new DateTime(yearInt, month, dayInt);
            return date.ToString("yyyy-MM-dd");
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new InvalidOperationException(
                $"Invalid date '{day}-{month}-{year}' in file '{filePath}'", ex);
        }
    }

    /// <summary>
    /// Extracts date in YYYY-MM-dd format from filename.
    /// Expected filename format: "YYYY-M-D.html" or "YYYY-MM-DD.html" or "path/to/YYYY-M-D.html"
    /// Handles both single and double digit months and days.
    /// </summary>
    /// <param name="filePath">The full file path.</param>
    /// <returns>Date in YYYY-MM-dd format (e.g., "2016-12-10").</returns>
    private string ExtractDateFromFilename(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        // Match YYYY-M-D or YYYY-MM-DD format (handles single and double digits for month and day)
        var dateRegex = new Regex(@"(\d{4})-(\d{1,2})-(\d{1,2})", RegexOptions.Compiled);
        
        var match = dateRegex.Match(fileName);
        if (!match.Success || match.Groups.Count < 4)
        {
            throw new InvalidOperationException(
                $"Could not extract date from filename '{fileName}' in file path '{filePath}'. Expected format: YYYY-M-D.html or YYYY-MM-DD.html");
        }

        var year = match.Groups[1].Value;
        var month = match.Groups[2].Value;
        var day = match.Groups[3].Value;

        // Parse to ensure valid date and normalize to YYYY-MM-dd format
        var dateStr = $"{year}-{month.PadLeft(2, '0')}-{day.PadLeft(2, '0')}";
        
        // Validate the extracted date format
        if (!DateTime.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            throw new InvalidOperationException(
                $"Invalid date format '{dateStr}' extracted from filename '{fileName}' in file path '{filePath}'");
        }

        return parsedDate.ToString("yyyy-MM-dd");
    }

    private string ExtractSingleMatchByXPathAndRegex(
        HtmlDocument doc,
        string xpath,
        Regex compiledRegex,
        string filePath)
    {
        // Check that exactly one element matches the XPath
        var elements = doc.DocumentNode.SelectNodes(xpath);
        
        if (elements == null || elements.Count == 0)
        {
            throw new InvalidOperationException($"Element not found by XPath '{xpath}' in file '{filePath}'");
        }

        if (elements.Count > 1)
        {
            throw new InvalidOperationException(
                $"Multiple elements ({elements.Count}) found by XPath '{xpath}' in file '{filePath}'. Expected exactly one element.");
        }

        // Get the single element
        var element = elements[0];

        // Extract and validate element text
        var text = element.InnerText?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"Element found by XPath '{xpath}' has empty or null text in file '{filePath}'");
        }

        // Apply compiled regex pattern and validate matches
        var matches = compiledRegex.Matches(text);
        
        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"No regex match found for pattern '{compiledRegex}' in element text '{text}' (XPath: '{xpath}') in file '{filePath}'");
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"Multiple regex matches found ({matches.Count}) for pattern '{compiledRegex}' in element text '{text}' (XPath: '{xpath}') in file '{filePath}'. Expected exactly one match.");
        }

        // Extract the first capture group value
        var match = matches[0];
        if (match.Groups.Count < 2)
        {
            throw new InvalidOperationException(
                $"Regex pattern '{compiledRegex}' does not contain a capture group in file '{filePath}'");
        }

        var capturedValue = match.Groups[1].Value;
        if (string.IsNullOrWhiteSpace(capturedValue))
        {
            throw new InvalidOperationException(
                $"Regex capture group returned empty value for pattern '{compiledRegex}' in file '{filePath}'");
        }

        return capturedValue;
    }
}


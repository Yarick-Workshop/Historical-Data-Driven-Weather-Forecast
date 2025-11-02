using HtmlAgilityPack;
using System.Text.RegularExpressions;
using Serilog;

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

        return new HtmlParseResult(cityName);
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


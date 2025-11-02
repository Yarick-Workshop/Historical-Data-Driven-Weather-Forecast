using HtmlAgilityPack;

namespace Historical.Weather.Data.Miner;

public class RealWeatherHtmlParser
{
    /// <summary>
    /// Loads and parses an HTML file, checking for syntax errors.
    /// </summary>
    /// <param name="filePath">The path to the HTML file to parse.</param>
    /// <returns>An HtmlDocument representing the parsed HTML.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the HTML file is malformed.</exception>
    public HtmlDocument ParseFile(string filePath)
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

        return doc;
    }
}


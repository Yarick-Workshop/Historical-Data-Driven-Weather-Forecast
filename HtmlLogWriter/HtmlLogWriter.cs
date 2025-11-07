using System.Reflection;
using System.Text;
using ScottPlot;

namespace HtmlLogWriter;

/// <summary>
/// A writer that can write HTML tables to the same file as HtmlLogSink.
/// Supports writing tables from flat classes using reflection.
/// </summary>
public class HtmlLogWriter : IDisposable
{
    private readonly HtmlLogFileManager.FileHandler _fileHandler;
    private readonly string _filePath;
    private readonly string _title;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the HtmlLogWriter class.
    /// </summary>
    /// <param name="filePath">The path to the HTML log file (must be the same as HtmlLogSink).</param>
    /// <param name="title">The title of the HTML document (must match HtmlLogSink).</param>
    public HtmlLogWriter(string filePath, string title = "Log")
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _title = title ?? throw new ArgumentNullException(nameof(title));

        _fileHandler = HtmlLogFileManager.GetOrCreateHandler(filePath, title);
    }

    /// <summary>
    /// Writes a table from a collection of flat class objects.
    /// Uses property/field names as column headers and values as row data.
    /// </summary>
    /// <typeparam name="T">The type of objects in the collection.</typeparam>
    /// <param name="items">The collection of objects to write as a table.</param>
    /// <param name="tableTitle">Optional title for the table. If not provided, uses the type name.</param>
    public void WriteTable<T>(IEnumerable<T> items, string? tableTitle = null)
    {
        if (_isDisposed)
            return;

        if (items == null)
            throw new ArgumentNullException(nameof(items));

        var itemsList = items.ToList();
        if (itemsList.Count == 0)
            return;

        var type = typeof(T);
        
        // Get all readable properties and fields
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && !IsIndexedProperty(p))
            .ToList();

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .ToList();

        if (properties.Count == 0 && fields.Count == 0)
        {
            throw new InvalidOperationException($"Type {type.Name} has no public readable properties or fields.");
        }

        // Close the logs container before writing tables (if not already closed)
        _fileHandler.CloseLogsContainer();

        // Write table container
        var title = tableTitle ?? type.Name;
        _fileHandler.WriteLine($@"        <div class=""table-container"">
            <div class=""table-title"">{EscapeHtml(title)}</div>
            <table>");

        // Write table header
        _fileHandler.WriteLine("                <thead>");
        _fileHandler.Write("                    <tr>");
        
        foreach (var prop in properties)
        {
            var displayName = SplitPascalCase(prop.Name);
            _fileHandler.Write($"<th>{EscapeHtml(displayName)}</th>");
        }
        
        foreach (var field in fields)
        {
            var displayName = SplitPascalCase(field.Name);
            _fileHandler.Write($"<th>{EscapeHtml(displayName)}</th>");
        }
        
        _fileHandler.WriteLine("</tr>");
        _fileHandler.WriteLine("                </thead>");

        // Write table body
        _fileHandler.WriteLine("                <tbody>");
        
        foreach (var item in itemsList)
        {
            _fileHandler.Write("                    <tr>");
            
            foreach (var prop in properties)
            {
                var value = GetPropertyValue(item, prop);
                var formattedValue = FormatValue(value);
                var cellContent = ContainsHtml(formattedValue) ? formattedValue : EscapeHtml(formattedValue);
                _fileHandler.Write($"<td>{cellContent}</td>");
            }
            
            foreach (var field in fields)
            {
                var value = field.GetValue(item);
                var formattedValue = FormatValue(value);
                var cellContent = ContainsHtml(formattedValue) ? formattedValue : EscapeHtml(formattedValue);
                _fileHandler.Write($"<td>{cellContent}</td>");
            }
            
            _fileHandler.WriteLine("</tr>");
        }
        
        _fileHandler.WriteLine("                </tbody>");
        _fileHandler.WriteLine("            </table>");
        _fileHandler.WriteLine("        </div>");
    }

    /// <summary>
    /// Writes a separator before the next table or log entry.
    /// </summary>
    public void WriteSeparator()
    {
        if (_isDisposed)
            return;

        _fileHandler.WriteLine(@"        <div class=""separator""><hr></div>");
    }

    /// <summary>
    /// Writes an image with a caption/subscription to the HTML file.
    /// </summary>
    /// <param name="imagePath">Path to the image file.</param>
    /// <param name="caption">Caption/subscription text for the image.</param>
    public void WriteImage(string imagePath, string? caption = null)
    {
        if (_isDisposed)
            return;

        if (string.IsNullOrEmpty(imagePath))
            throw new ArgumentNullException(nameof(imagePath));

        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"Image file not found: {imagePath}");

        // Close the logs container before writing images
        _fileHandler.CloseLogsContainer();

        // Read image and convert to base64
        var imageBytes = File.ReadAllBytes(imagePath);
        var base64String = Convert.ToBase64String(imageBytes);
        var imageExtension = Path.GetExtension(imagePath).ToLowerInvariant().TrimStart('.');
        var mimeType = imageExtension switch
        {
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "gif" => "image/gif",
            "svg" => "image/svg+xml",
            "webp" => "image/webp",
            _ => "image/png"
        };
        var imgSrc = $"data:{mimeType};base64,{base64String}";

        _fileHandler.WriteLine($@"        <div class=""image-container"">
            <img src=""{imgSrc}"" alt=""{EscapeHtml(caption ?? "Image")}"" />
            {(string.IsNullOrEmpty(caption) ? "" : $@"<div class=""image-caption"">{EscapeHtml(caption)}</div>")}
        </div>");
    }

    /// <summary>
    /// Creates and writes a distribution diagram (histogram) using ScottPlot.
    /// </summary>
    /// <param name="data">The data values to plot in the distribution.</param>
    /// <param name="caption">Caption/subscription text for the diagram.</param>
    /// <param name="width">Width of the plot in pixels. Default is 800.</param>
    /// <param name="height">Height of the plot in pixels. Default is 400.</param>
    /// <param name="bins">Number of bins for the histogram. Default is 30.</param>
    public void WriteDistributionDiagram(
        IEnumerable<double> data,
        string? caption = null,
        int width = 800,
        int height = 400,
        int bins = 30)
    {
        if (_isDisposed)
            return;

        if (data == null)
            throw new ArgumentNullException(nameof(data));

        var dataArray = data.ToArray();
        if (dataArray.Length == 0)
            return;

        // Close the logs container before writing diagrams
        _fileHandler.CloseLogsContainer();

        // Create the plot
        var plt = new Plot();
        
        // Calculate histogram bins
        var min = dataArray.Min();
        var max = dataArray.Max();
        var binWidth = (max - min) / bins;
        
        var binEdges = new double[bins + 1];
        var binCounts = new double[bins];
        
        for (int i = 0; i <= bins; i++)
        {
            binEdges[i] = min + i * binWidth;
        }
        
        foreach (var value in dataArray)
        {
            var binIndex = (int)Math.Min((value - min) / binWidth, bins - 1);
            binCounts[binIndex]++;
        }
        
        // Create positions for bars (centers of bins)
        var positions = new double[bins];
        var values = new double[bins];
        for (int i = 0; i < bins; i++)
        {
            positions[i] = (binEdges[i] + binEdges[i + 1]) / 2;
            values[i] = binCounts[i];
        }
        
        // Add bars to create histogram
        var bars = plt.Add.Bars(positions, values);
        bars.Color = Colors.Blue;

        // Style the plot
        plt.Title(caption ?? "Distribution");
        plt.YLabel("Frequency");
        plt.XLabel("Value");

        // Save plot to temporary file then read as bytes
        var tempFile = Path.Combine(Path.GetTempPath(), $"plot_{Guid.NewGuid()}.png");
        try
        {
            plt.SavePng(tempFile, width, height);
            var imageBytes = File.ReadAllBytes(tempFile);
            var base64String = Convert.ToBase64String(imageBytes);
            var imgSrc = $"data:image/png;base64,{base64String}";

            _fileHandler.WriteLine($@"        <div class=""image-container"">
            <img src=""{imgSrc}"" alt=""{EscapeHtml(caption ?? "Distribution Diagram")}"" />
            {(string.IsNullOrEmpty(caption) ? "" : $@"<div class=""image-caption"">{EscapeHtml(caption)}</div>")}
        </div>");
        }
        finally
        {
            // Clean up temporary file
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }

    private static bool IsIndexedProperty(PropertyInfo property)
    {
        return property.GetIndexParameters().Length > 0;
    }

    private static object? GetPropertyValue(object obj, PropertyInfo property)
    {
        try
        {
            return property.GetValue(obj);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatValue(object? value)
    {
        if (value == null)
            return string.Empty;

        if (value is DateTime dt)
            return dt.ToString("yyyy-MM-dd HH:mm:ss.fff");

        if (value is DateTimeOffset dto)
            return dto.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");

        if (value is IFormattable formattable && !(value is string))
            return formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture);

        return value.ToString() ?? string.Empty;
    }

    private static string SplitPascalCase(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Insert space before uppercase letters (except the first one)
        // Uses regex to match: lowercase or digit followed by uppercase
        return System.Text.RegularExpressions.Regex.Replace(
            text,
            @"(\p{Ll}|\d)(\p{Lu})",
            "$1 $2");
    }

    private static bool ContainsHtml(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        // Check if the string contains HTML tags (e.g., <a>, <div>, etc.)
        // Look for patterns like <tag> or <tag/> or </tag>
        return System.Text.RegularExpressions.Regex.IsMatch(text, @"<[^>]+>");
    }

    private static string EscapeHtml(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");
    }

    /// <summary>
    /// Ensures the footer is written and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (!_isDisposed)
        {
            _fileHandler.WriteFooter();
            HtmlLogFileManager.RemoveHandler(_filePath);
            _isDisposed = true;
        }
    }
}

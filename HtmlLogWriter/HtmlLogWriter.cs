using System.Reflection;
using System.Text;

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
                _fileHandler.Write($"<td>{EscapeHtml(FormatValue(value))}</td>");
            }
            
            foreach (var field in fields)
            {
                var value = field.GetValue(item);
                _fileHandler.Write($"<td>{EscapeHtml(FormatValue(value))}</td>");
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

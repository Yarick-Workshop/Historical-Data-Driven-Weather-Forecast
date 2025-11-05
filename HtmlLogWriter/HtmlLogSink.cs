using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using System.Text;

namespace HtmlLogWriter;

/// <summary>
/// A Serilog sink that writes log events to HTML files.
/// </summary>
public class HtmlLogSink : ILogEventSink, IDisposable
{
    private readonly HtmlLogFileManager.FileHandler _fileHandler;
    private readonly string _filePath;
    private readonly string _title;
    private readonly ITextFormatter _formatter;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the HtmlLogSink class.
    /// </summary>
    /// <param name="filePath">The path to the HTML log file.</param>
    /// <param name="title">The title of the HTML document. Defaults to "Log" if not specified.</param>
    /// <param name="formatter">Optional formatter for log messages. If not provided, uses a default formatter.</param>
    public HtmlLogSink(string filePath, string title = "Log", ITextFormatter? formatter = null)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _title = title ?? throw new ArgumentNullException(nameof(title));
        _formatter = formatter ?? new MessageTemplateTextFormatter("{Message:lj}{NewLine}{Exception}");

        _fileHandler = HtmlLogFileManager.GetOrCreateHandler(filePath, title);
    }

    /// <summary>
    /// Emits a log event to the HTML file.
    /// </summary>
    /// <param name="logEvent">The log event to emit.</param>
    public void Emit(LogEvent logEvent)
    {
        if (_isDisposed)
            return;

        var timestamp = logEvent.Timestamp.DateTime;
        var level = logEvent.Level;
        var levelClass = GetLevelClass(level);
        var levelDisplay = level.ToString().ToUpperInvariant();

        // Format the message
        var messageBuilder = new StringBuilder();
        using (var writer = new StringWriter(messageBuilder))
        {
            _formatter.Format(logEvent, writer);
        }
        var message = messageBuilder.ToString().TrimEnd();

        _fileHandler.WriteLine($@"        <div class=""log-entry {levelClass}"">
            <div class=""timestamp"">{timestamp:yyyy-MM-dd HH:mm:ss.fff}</div>
            <div class=""level {levelClass}"">{levelDisplay}</div>
            <div class=""message"">{EscapeHtml(message)}</div>
        </div>");
    }

    private string GetLevelClass(LogEventLevel level)
    {
        return level switch
        {
            LogEventLevel.Verbose or LogEventLevel.Debug => "debug",
            LogEventLevel.Information => "info",
            LogEventLevel.Warning => "warning",
            LogEventLevel.Error or LogEventLevel.Fatal => "error",
            _ => "info"
        };
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
    /// Releases all resources used by the HtmlLogSink.
    /// </summary>
    public void Dispose()
    {
        if (!_isDisposed)
        {
            // Note: We don't remove the handler here as HtmlLogWriter might still be using it
            // The handler will be cleaned up when the application exits
            _isDisposed = true;
        }
    }
}

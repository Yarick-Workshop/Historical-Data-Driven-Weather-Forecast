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
    private readonly StreamWriter _writer;
    private readonly string _filePath;
    private readonly string _title;
    private readonly ITextFormatter _formatter;
    private readonly object _lockObject = new object();
    private bool _isDisposed;
    private bool _isHeaderWritten;

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

        // Create directory if it doesn't exist
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _writer = new StreamWriter(filePath, append: false, Encoding.UTF8);
        WriteHeader();
    }

    /// <summary>
    /// Emits a log event to the HTML file.
    /// </summary>
    /// <param name="logEvent">The log event to emit.</param>
    public void Emit(LogEvent logEvent)
    {
        if (_isDisposed)
            return;

        lock (_lockObject)
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

            _writer.WriteLine($@"        <div class=""log-entry {levelClass}"">
            <div class=""timestamp"">{timestamp:yyyy-MM-dd HH:mm:ss.fff}</div>
            <div class=""level {levelClass}"">{levelDisplay}</div>
            <div class=""message"">{EscapeHtml(message)}</div>
        </div>");
            _writer.Flush();
        }
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

    private void WriteHeader()
    {
        if (_isHeaderWritten)
            return;

        _writer.WriteLine(@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>" + EscapeHtml(_title) + @"</title>
    <style>
        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            margin: 0;
            padding: 20px;
            background-color: #f5f5f5;
        }
        .container {
            max-width: 1200px;
            margin: 0 auto;
            background-color: white;
            border-radius: 8px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
            overflow: hidden;
        }
        h1 {
            background-color: #2c3e50;
            color: white;
            margin: 0;
            padding: 20px;
            font-size: 24px;
        }
        .metadata {
            padding: 15px 20px;
            background-color: #ecf0f1;
            border-bottom: 1px solid #bdc3c7;
            font-size: 14px;
            color: #34495e;
        }
        .logs-container {
            display: flex;
            flex-direction: column;
        }
        .logs-header {
            display: grid;
            grid-template-columns: 180px 100px 1fr;
            background-color: #34495e;
            color: white;
            padding: 12px;
            font-weight: 600;
            position: sticky;
            top: 0;
            z-index: 10;
        }
        .logs-header > div {
            padding: 0 12px;
        }
        .log-entry {
            display: grid;
            grid-template-columns: 180px 100px 1fr;
            padding: 10px 12px;
            border-bottom: 1px solid #ecf0f1;
            transition: background-color 0.2s;
        }
        .log-entry:hover {
            background-color: #f8f9fa;
        }
        .log-entry.debug .level {
            background-color: #3498db;
            color: white;
            font-weight: bold;
            padding: 4px 8px;
            border-radius: 4px;
            font-size: 11px;
            display: inline-block;
        }
        .log-entry.info .level {
            background-color: #2ecc71;
            color: white;
            font-weight: bold;
            padding: 4px 8px;
            border-radius: 4px;
            font-size: 11px;
            display: inline-block;
        }
        .log-entry.warning .level {
            background-color: #f39c12;
            color: white;
            font-weight: bold;
            padding: 4px 8px;
            border-radius: 4px;
            font-size: 11px;
            display: inline-block;
        }
        .log-entry.error .level {
            background-color: #e74c3c;
            color: white;
            font-weight: bold;
            padding: 4px 8px;
            border-radius: 4px;
            font-size: 11px;
            display: inline-block;
        }
        .log-entry .timestamp {
            font-family: 'Courier New', monospace;
            font-size: 12px;
            color: #7f8c8d;
            white-space: nowrap;
        }
        .log-entry .message {
            word-wrap: break-word;
            overflow-wrap: break-word;
        }
        .separator hr {
            border: none;
            border-top: 1px dashed #bdc3c7;
            margin: 5px 0;
        }
        .footer {
            padding: 15px 20px;
            background-color: #ecf0f1;
            text-align: center;
            color: #7f8c8d;
            font-size: 12px;
        }
    </style>
</head>
<body>
    <div class=""container"">
        <h1>" + EscapeHtml(_title) + @"</h1>
        <div class=""metadata"">
            Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + @"<br>
            File: " + EscapeHtml(_filePath) + @"
        </div>
        <div class=""logs-container"">
            <div class=""logs-header"">
                <div>Timestamp</div>
                <div>Level</div>
                <div>Message</div>
            </div>");

        _isHeaderWritten = true;
        _writer.Flush();
    }

    private void WriteFooter()
    {
        _writer.WriteLine(@"        </div>
        <div class=""footer"">
            End of log file
        </div>
    </div>
</body>
</html>");
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
            lock (_lockObject)
            {
                if (!_isDisposed)
                {
                    WriteFooter();
                    _writer?.Dispose();
                    _isDisposed = true;
                }
            }
        }
    }
}


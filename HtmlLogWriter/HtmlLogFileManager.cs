using System.Collections.Concurrent;
using System.Text;

namespace HtmlLogWriter;

/// <summary>
/// Manages shared file access for HtmlLogSink and HtmlLogWriter to write to the same file.
/// </summary>
internal static class HtmlLogFileManager
{
    private static readonly ConcurrentDictionary<string, FileHandler> _handlers = new();
    private static readonly object _registrationLock = new object();

    /// <summary>
    /// Gets or creates a file handler for the specified file path.
    /// </summary>
    public static FileHandler GetOrCreateHandler(string filePath, string title)
    {
        return _handlers.GetOrAdd(filePath, path =>
        {
            lock (_registrationLock)
            {
                // Double-check after acquiring lock
                if (_handlers.TryGetValue(path, out var existing))
                    return existing;

                var handler = new FileHandler(path, title);
                return handler;
            }
        });
    }

    /// <summary>
    /// Removes and disposes a file handler when no longer needed.
    /// </summary>
    public static void RemoveHandler(string filePath)
    {
        if (_handlers.TryRemove(filePath, out var handler))
        {
            handler.Dispose();
        }
    }

    /// <summary>
    /// Handles file operations for a specific log file.
    /// </summary>
    internal class FileHandler : IDisposable
    {
        private readonly StreamWriter _writer;
        private readonly string _filePath;
        private readonly string _title;
        private readonly object _lockObject = new object();
        private bool _isDisposed;
        private bool _isHeaderWritten;
        private bool _isFooterWritten;
        private bool _isLogsContainerClosed;

        public FileHandler(string filePath, string title)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _title = title ?? throw new ArgumentNullException(nameof(title));

            // Create directory if it doesn't exist
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _writer = new StreamWriter(filePath, append: false, Encoding.UTF8);
            WriteHeader();
        }

        public void WriteLine(string line)
        {
            if (_isDisposed || _isFooterWritten)
                return;

            lock (_lockObject)
            {
                if (_isDisposed || _isFooterWritten)
                    return;

                _writer.WriteLine(line);
                _writer.Flush();
            }
        }

        public void Write(string text)
        {
            if (_isDisposed || _isFooterWritten)
                return;

            lock (_lockObject)
            {
                if (_isDisposed || _isFooterWritten)
                    return;

                _writer.Write(text);
                _writer.Flush();
            }
        }

        private void WriteHeader()
        {
            if (_isHeaderWritten)
                return;

            lock (_lockObject)
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
        .table-container {
            margin: 20px;
            padding: 20px;
            background-color: white;
            border-radius: 8px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
        }
        .table-title {
            font-size: 18px;
            font-weight: 600;
            color: #2c3e50;
            margin-bottom: 15px;
            padding-bottom: 10px;
            border-bottom: 2px solid #34495e;
        }
        table {
            width: 100%;
            border-collapse: collapse;
            margin-top: 10px;
        }
        th {
            background-color: #34495e;
            color: white;
            padding: 12px;
            text-align: left;
            font-weight: 600;
            border: 1px solid #2c3e50;
        }
        td {
            padding: 10px 12px;
            border: 1px solid #ecf0f1;
        }
        tr:nth-child(even) {
            background-color: #f8f9fa;
        }
        tr:hover {
            background-color: #e8f4f8;
        }
        .image-container {
            margin: 20px;
            padding: 20px;
            background-color: white;
            border-radius: 8px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
            text-align: center;
        }
        .image-container img {
            max-width: 100%;
            height: auto;
            border: 1px solid #ecf0f1;
            border-radius: 4px;
            display: block;
            margin: 0 auto;
        }
        .image-caption {
            margin-top: 10px;
            font-size: 14px;
            color: #34495e;
            font-style: italic;
            text-align: center;
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
        }

        public void CloseLogsContainer()
        {
            if (_isLogsContainerClosed || _isDisposed || _isFooterWritten)
                return;

            lock (_lockObject)
            {
                if (_isLogsContainerClosed || _isDisposed || _isFooterWritten)
                    return;

                _writer.WriteLine("        </div>");
                _writer.Flush();
                _isLogsContainerClosed = true;
            }
        }

        public void WriteFooter()
        {
            if (_isFooterWritten)
                return;

            lock (_lockObject)
            {
                if (_isFooterWritten)
                    return;

                // Close logs container if not already closed
                if (!_isLogsContainerClosed)
                {
                    _writer.WriteLine("        </div>");
                    _isLogsContainerClosed = true;
                }

                _writer.WriteLine(@"        <div class=""footer"">
            End of log file
        </div>
    </div>
</body>
</html>");
                _writer.Flush();
                _isFooterWritten = true;
            }
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
}

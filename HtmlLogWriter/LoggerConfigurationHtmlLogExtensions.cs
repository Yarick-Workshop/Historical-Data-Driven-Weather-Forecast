using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace HtmlLogWriter;

/// <summary>
/// Extension methods for configuring Serilog HTML log sink.
/// </summary>
public static class LoggerConfigurationHtmlLogExtensions
{
    /// <summary>
    /// Writes log events to an HTML file.
    /// </summary>
    /// <param name="loggerSinkConfiguration">The logger sink configuration.</param>
    /// <param name="filePath">The path to the HTML log file.</param>
    /// <param name="title">The title of the HTML document. Defaults to "Log" if not specified.</param>
    /// <param name="restrictedToMinimumLevel">The minimum level for events passed through the sink. Defaults to <see cref="LevelAlias.Minimum"/>.</param>
    /// <param name="levelSwitch">A switch allowing the pass-through minimum level to be changed at runtime.</param>
    /// <returns>Configuration object allowing method chaining.</returns>
    public static LoggerConfiguration HtmlLog(
        this LoggerSinkConfiguration loggerSinkConfiguration,
        string filePath,
        string title = "Log",
        LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
        LoggingLevelSwitch? levelSwitch = null)
    {
        if (loggerSinkConfiguration == null)
            throw new ArgumentNullException(nameof(loggerSinkConfiguration));

        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

        return loggerSinkConfiguration.Sink(
            new HtmlLogSink(filePath, title),
            restrictedToMinimumLevel,
            levelSwitch);
    }
}


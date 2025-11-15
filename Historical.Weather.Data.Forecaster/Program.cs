using Historical.Weather.Data.Forecaster;
using HtmlLogWriter;
using Serilog;
using Serilog.Events;

var logDateTime = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
var logDirectory = $"ForecastLog_{logDateTime}";
var logFilePath = Path.Combine(logDirectory, $"forecast_{logDateTime}.html");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Async(a => a.Console(
        restrictedToMinimumLevel: LogEventLevel.Debug,
        buffered: false))
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(evt => (evt.Level >= LogEventLevel.Information) && (evt.Level != LogEventLevel.Error))
        .WriteTo.HtmlLog(logFilePath, "Historical Weather Forecaster"))
    .CreateLogger();

try
{
    var parseResult = ForecastOptionsParser.TryParse(args, out var options, out var errors);

    if (!parseResult)
    {
        Log.Error("Failed to parse arguments:");
        foreach (var error in errors)
        {
            Log.Error("  - {Error}", error);
        }

        ForecastOptionsParser.WriteUsage();
        return 1;
    }

    Log.Information("Forecast run started at {DateTime}", DateTime.Now);
    Log.Information("Input path: {Input}", options.InputPath);
    Log.Information("Window: {WindowSize} samples over {WindowHours} hours", options.WindowSize, options.WindowDuration.TotalHours);
    Log.Information("Training ratio: {TrainRatio}", options.TrainingFraction);
    Log.Information("Epochs: {Epochs}, Batch size: {BatchSize}, Hidden size: {HiddenSize}, Learning rate: {LearningRate}",
        options.TrainingEpochs, options.BatchSize, options.HiddenSize, options.LearningRate);

    var runner = new ForecastRunner(options, logFilePath);
    await runner.RunAsync();

    Log.Information("Forecast run finished successfully.");
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "An unhandled error occurred while running the forecaster.");
    return 2;
}
finally
{
    Log.CloseAndFlush();
}

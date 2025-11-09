using Historical.Weather.Data.Forecaster;

var parseResult = ForecastOptionsParser.TryParse(args, out var options, out var errors);

if (!parseResult)
{
    Console.Error.WriteLine("Failed to parse arguments:");
    foreach (var error in errors)
    {
        Console.Error.WriteLine($"  - {error}");
    }

    ForecastOptionsParser.WriteUsage();
    return 1;
}

try
{
    var runner = new ForecastRunner(options);
    await runner.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("An unhandled error occurred while running the forecaster:");
    Console.Error.WriteLine(ex);
    return 2;
}

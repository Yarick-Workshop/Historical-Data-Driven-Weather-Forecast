using System.Globalization;

namespace Historical.Weather.Data.Forecaster;

internal static class ForecastOptionsParser
{
    private const string DefaultWindowSize = "8";
    private const string DefaultWindowHours = "36";
    private const string DefaultTrainingFraction = "0.8";
    private const string DefaultEpochs = "20";
    private const string DefaultLearningRate = "0.001";
    private const string DefaultBatchSize = "64";
    private const string DefaultHiddenSize = "32";

    public static bool TryParse(string[] args, out ForecastOptions options, out List<string> errors)
    {
        options = null!;
        errors = new List<string>();

        var tokens = new Queue<string>(args);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (tokens.Count > 0)
        {
            var token = tokens.Dequeue();

            if (IsHelpToken(token))
            {
                options = null!;
                errors = new List<string>();
                return false;
            }

            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                errors.Add($"Unexpected token '{token}'. Options must start with '--'.");
                continue;
            }

            if (IsFlag(token))
            {
                flags.Add(token);
                continue;
            }

            if (tokens.Count == 0)
            {
                errors.Add($"Missing value for option '{token}'.");
                continue;
            }

            var value = tokens.Dequeue();
            values[token] = value;
        }

        var windowSize = ParseInt(values, "--window-size", DefaultWindowSize, minValue: 1, errors);
        var windowHours = ParseDouble(values, "--window-hours", DefaultWindowHours, minValue: 0.01, maxValue: null, errors);
        var trainingFraction = ParseDouble(values, "--train-ratio", DefaultTrainingFraction, minValue: 0.1, maxValue: 0.95, errors);
        var epochs = ParseInt(values, "--epochs", DefaultEpochs, minValue: 1, errors);
        var batchSize = ParseInt(values, "--batch-size", DefaultBatchSize, minValue: 1, errors);
        var hiddenSize = ParseInt(values, "--hidden-size", DefaultHiddenSize, minValue: 4, errors);
        var learningRate = ParseDouble(values, "--learning-rate", DefaultLearningRate, minValue: 0.000001, maxValue: 1.0, errors);
        var inputPath = ResolveInputPath(values.GetValueOrDefault("--input"));
        var outputPath = ResolveOutput(values.GetValueOrDefault("--output"));

        var allowFallback = !flags.Contains("--strict-window");

        if (inputPath == null)
        {
            errors.Add("Input directory with CSV files was not found. Provide --input <path> or ensure the default miner output exists.");
        }

        if (windowSize is null
            || windowHours is null
            || trainingFraction is null
            || epochs is null
            || batchSize is null
            || hiddenSize is null
            || learningRate is null
            || errors.Count > 0)
        {
            options = null!;
            return false;
        }

        options = new ForecastOptions
        {
            InputPath = inputPath!,
            WindowSize = windowSize.Value,
            WindowDuration = TimeSpan.FromHours(windowHours.Value),
            TrainingFraction = trainingFraction.Value,
            TrainingEpochs = epochs.Value,
            LearningRate = learningRate.Value,
            BatchSize = batchSize.Value,
            HiddenSize = hiddenSize.Value,
            AllowFallbackOutsideWindow = allowFallback,
            OutputDirectory = outputPath
        };

        return true;
    }

    public static void WriteUsage()
    {
        Console.WriteLine("Usage: dotnet run --project Historical.Weather.Data.Forecaster -- [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --input <path>        Path to a CSV file or directory with CSV files. Defaults to miner output directory if found.");
        Console.WriteLine($"  --window-size <int>   Number of previous observations to use (default: {DefaultWindowSize}).");
        Console.WriteLine($"  --window-hours <num>  Size of the sliding time window in hours (default: {DefaultWindowHours}).");
        Console.WriteLine($"  --train-ratio <num>   Fraction of rows used for training (default: {DefaultTrainingFraction}).");
        Console.WriteLine($"  --epochs <int>        Training epochs for the neural model (default: {DefaultEpochs}).");
        Console.WriteLine($"  --learning-rate <num> Learning rate for the neural model (default: {DefaultLearningRate}).");
        Console.WriteLine($"  --batch-size <int>    Mini-batch size for the neural model (default: {DefaultBatchSize}).");
        Console.WriteLine($"  --hidden-size <int>   Hidden units inside the LSTM cell (default: {DefaultHiddenSize}).");
        Console.WriteLine("  --output <path>       Optional directory for forecast output CSV files.");
        Console.WriteLine("  --strict-window       Disable fallback to outside-window observations when gaps exist.");
        Console.WriteLine("  --help                Show this message.");
    }

    private static bool IsHelpToken(string token)
    {
        return token.Equals("--help", StringComparison.OrdinalIgnoreCase)
               || token.Equals("-h", StringComparison.OrdinalIgnoreCase)
               || token.Equals("/?");
    }

    private static bool IsFlag(string token)
    {
        return token.Equals("--strict-window", StringComparison.OrdinalIgnoreCase);
    }

    private static int? ParseInt(
        IReadOnlyDictionary<string, string> values,
        string key,
        string defaultValue,
        int? minValue,
        ICollection<string> errors)
    {
        var value = values.GetValueOrDefault(key) ?? defaultValue;

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            errors.Add($"Invalid integer value '{value}' for {key}.");
            return null;
        }

        if (minValue is not null && parsed < minValue.Value)
        {
            errors.Add($"{key} must be >= {minValue}.");
            return null;
        }

        return parsed;
    }

    private static double? ParseDouble(
        IReadOnlyDictionary<string, string> values,
        string key,
        string defaultValue,
        double? minValue,
        double? maxValue,
        ICollection<string> errors)
    {
        var value = values.GetValueOrDefault(key) ?? defaultValue;

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            errors.Add($"Invalid number '{value}' for {key}.");
            return null;
        }

        if (minValue is not null && parsed < minValue.Value)
        {
            errors.Add($"{key} must be >= {minValue}.");
            return null;
        }

        if (maxValue is not null && parsed > maxValue.Value)
        {
            errors.Add($"{key} must be <= {maxValue}.");
            return null;
        }

        return parsed;
    }

    private static string? ResolveInputPath(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var full = Path.GetFullPath(requested);
            if (Directory.Exists(full) || File.Exists(full))
            {
                return full;
            }

            return null;
        }

        var projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var solutionDir = Directory.GetParent(projectDir)?.FullName;
        if (solutionDir == null)
        {
            return null;
        }

        var defaultDir = Path.Combine(solutionDir, "Historical.Weather.Data.Miner", "output");
        return Directory.Exists(defaultDir) ? defaultDir : null;
    }

    private static string? ResolveOutput(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return null;
        }

        var full = Path.GetFullPath(requested);
        Directory.CreateDirectory(full);
        return full;
    }
}



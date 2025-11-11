using System.Globalization;

namespace Historical.Weather.Data.Forecaster;

internal static class ForecastOptionsParser
{
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

        var windowSize = ParseInt(values, "--window-size", errors, minValue: 1);
        var windowHours = ParseDouble(values, "--window-hours", errors, minValue: 0.01, maxValue: null);
        var trainingFraction = ParseDouble(values, "--train-ratio", errors, minValue: 0.1, maxValue: 0.95);
        var epochs = ParseInt(values, "--epochs", errors, minValue: 1);
        var batchSize = ParseInt(values, "--batch-size", errors, minValue: 1);
        var hiddenSize = ParseInt(values, "--hidden-size", errors, minValue: 4);
        var learningRate = ParseDouble(values, "--learning-rate", errors, minValue: 0.000001, maxValue: 1.0);
        var inputPath = ResolveInputPath(values.GetValueOrDefault("--input"), errors);
        var outputPath = ResolveOutput(values.GetValueOrDefault("--output"), errors);

        var allowFallback = !flags.Contains("--strict-window");

        if (inputPath == null)
        {
            errors.Add("Input directory with CSV files was not found. Provide --input <path> or ensure the default miner output exists.");
        }

        if (errors.Count > 0
            || windowSize is null
            || windowHours is null
            || trainingFraction is null
            || epochs is null
            || batchSize is null
            || hiddenSize is null
            || learningRate is null)
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
        Console.WriteLine("  --window-size <int>   Number of previous observations to use (required).");
        Console.WriteLine("  --window-hours <num>  Size of the sliding time window in hours (required).");
        Console.WriteLine("  --train-ratio <num>   Fraction of rows used for training (required).");
        Console.WriteLine("  --epochs <int>        Training epochs for the neural model (required).");
        Console.WriteLine("  --learning-rate <num> Learning rate for the neural model (required).");
        Console.WriteLine("  --batch-size <int>    Mini-batch size for the neural model (required).");
        Console.WriteLine("  --hidden-size <int>   Hidden units inside the LSTM cell (required).");
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
        ICollection<string> errors,
        int? minValue = null,
        int? maxValue = null)
    {
        if (!values.TryGetValue(key, out var value))
        {
            errors.Add($"Missing required option {key}.");
            return null;
        }

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

        if (maxValue is not null && parsed > maxValue.Value)
        {
            errors.Add($"{key} must be <= {maxValue}.");
            return null;
        }

        return parsed;
    }

    private static double? ParseDouble(
        IReadOnlyDictionary<string, string> values,
        string key,
        ICollection<string> errors,
        double? minValue = null,
        double? maxValue = null)
    {
        if (!values.TryGetValue(key, out var value))
        {
            errors.Add($"Missing required option {key}.");
            return null;
        }

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

    private static string? ResolveInputPath(string? requested, ICollection<string> errors)
    {
        
        var full = Path.GetFullPath(requested);
        if (Directory.Exists(full) || File.Exists(full))
        {
            return full;
        }

        errors.Add($"Input path '{requested}' does not exist.");
        return null;
    }

    private static string? ResolveOutput(string? requested, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return null;
        }

        var full = Path.GetFullPath(requested);
        try
        {
            Directory.CreateDirectory(full);
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to create output directory '{requested}': {ex.Message}");
            return null;
        }

        return full;
    }
}



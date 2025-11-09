namespace Historical.Weather.Data.Forecaster;

/// <summary>
///     Strongly typed configuration used by the forecaster.
/// </summary>
public sealed record ForecastOptions
{
    /// <summary>
    ///     Gets the path to a CSV file or a directory that contains CSV files to process.
    /// </summary>
    public required string InputPath { get; init; }

    /// <summary>
    ///     Gets the maximum number of historical observations to use for a forecast.
    /// </summary>
    public required int WindowSize { get; init; }

    /// <summary>
    ///     Gets the duration of the time window that observations must fall into to be considered for forecasting.
    /// </summary>
    public required TimeSpan WindowDuration { get; init; }

    /// <summary>
    ///     Gets the fraction of the chronological dataset that is used for training. The remainder is used for validation.
    /// </summary>
    public required double TrainingFraction { get; init; }

    /// <summary>
    ///     Gets the number of training epochs used by neural network models.
    /// </summary>
    public required int TrainingEpochs { get; init; }

    /// <summary>
    ///     Gets the learning rate used by neural network models.
    /// </summary>
    public required double LearningRate { get; init; }

    /// <summary>
    ///     Gets the mini-batch size used by neural network models.
    /// </summary>
    public required int BatchSize { get; init; }

    /// <summary>
    ///     Gets the number of hidden units in the neural network.
    /// </summary>
    public required int HiddenSize { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the forecaster can fall back to using the last records even if they fall outside the time window.
    /// </summary>
    public required bool AllowFallbackOutsideWindow { get; init; }

    /// <summary>
    ///     Gets an optional path where per-file forecast CSVs should be written. When <c>null</c>, no files are written.
    /// </summary>
    public string? OutputDirectory { get; init; }
}



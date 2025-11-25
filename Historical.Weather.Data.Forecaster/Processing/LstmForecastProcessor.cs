namespace Historical.Weather.Data.Forecaster.Processing;

/// <summary>
///     Backward-compatible wrapper for LSTM forecast processing.
///     This class delegates to <see cref="NeuralNetworkForecastProcessor"/> with <see cref="NeuralNetworkType.LSTM"/>.
/// </summary>
[Obsolete("Use NeuralNetworkForecastProcessor with NeuralNetworkType.LSTM instead.")]
internal sealed class LstmForecastProcessor : IDisposable
{
    private readonly NeuralNetworkForecastProcessor _processor;

    public LstmForecastProcessor(ForecastOptions options)
    {
        _processor = new NeuralNetworkForecastProcessor(options, NeuralNetworkType.LSTM);
    }

    public ForecastResult Process(IReadOnlyList<WeatherObservation> rawObservations)
    {
        return _processor.Process(rawObservations);
    }

    public TrainingStats? GetAndResetTrainingStats()
    {
        return _processor.GetAndResetTrainingStats();
    }

    public void Dispose()
    {
        _processor.Dispose();
    }
}


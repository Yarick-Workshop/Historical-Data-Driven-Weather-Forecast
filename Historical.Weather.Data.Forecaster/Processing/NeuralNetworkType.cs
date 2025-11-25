namespace Historical.Weather.Data.Forecaster.Processing;

/// <summary>
///     Represents the type of neural network architecture to use for forecasting.
/// </summary>
public enum NeuralNetworkType
{
    /// <summary>
    ///     Long Short-Term Memory (LSTM) network.
    /// </summary>
    LSTM,

    /// <summary>
    ///     Gated Recurrent Unit (GRU) network.
    /// </summary>
    GRU
}


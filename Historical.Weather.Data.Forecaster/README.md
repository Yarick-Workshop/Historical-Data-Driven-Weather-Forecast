## Historical.Weather.Data.Forecaster

This console application reads the normalized CSV files emitted by `Historical.Weather.Data.Miner`, evaluates a simple temperature forecaster, and produces the next predicted observation for every file.

### Command-Line Arguments

```
dotnet run --project Historical.Weather.Data.Forecaster -- [options]
```

- `--input <path>` – Path to a CSV file or directory with CSV files. When omitted, the tool looks for `Historical.Weather.Data.Miner/output`.
- `--window-size <int>` – Number of previous observations used for each forecast (default: `8`).
- `--window-hours <number>` – Size of the sliding time window in hours that observations must fall into (default: `36`).
- `--train-ratio <number>` – Fraction of rows used for training before evaluating the forecaster (default: `0.8`).
- `--epochs <int>` – Training epochs for the neural model (default: `20`).
- `--learning-rate <number>` – Learning rate for the neural model (default: `0.001`).
- `--batch-size <int>` – Mini-batch size for the neural model (default: `64`).
- `--hidden-size <int>` – Hidden units inside the LSTM cell (default: `32`).
- `--output <path>` – Optional directory where validation/forecast CSVs are written. If not supplied, results are printed only to the console.
- `--strict-window` – Disable the fallback that uses the last available observations when the time window is empty.
- `--help` – Print usage information.

### Behaviour

1. For each input CSV the tool sorts rows chronologically and splits them into training and validation partitions.
2. The tool trains an LSTM neural network (TorchSharp) over sliding time windows of the historical series.
3. Accuracy metrics (MAE, RMSE, and, where possible, MAPE) are reported for the validation partition.
4. The next temperature value is predicted for the typical cadence inferred from the historical data.
5. When `--output` is provided an additional `<name>.forecast.csv` file containing the validation predictions and the next forecast is emitted.



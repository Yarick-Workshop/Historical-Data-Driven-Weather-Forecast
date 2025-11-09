using System.Diagnostics.CodeAnalysis;
using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;

namespace Historical.Weather.Data.Forecaster.Processing;

internal sealed class LstmForecastProcessor : IDisposable
{
    private readonly ForecastOptions _options;
    private readonly Device _device;
    private readonly LstmRegressionModel _model;

    public LstmForecastProcessor(ForecastOptions options)
    {
        _options = options;
        _device = CPU;
        _model = new LstmRegressionModel(
            inputSize: 1,
            hiddenSize: options.HiddenSize,
            numLayers: 1);
    }

    public ForecastResult Process(IReadOnlyList<WeatherObservation> rawObservations)
    {
        var ordered = rawObservations.OrderBy(o => o.Timestamp).ToList();
        var splitIndex = DetermineTrainingIndex(ordered.Count, _options.WindowSize, _options.TrainingFraction);

        var trainingRows = ordered.Take(splitIndex).ToList();
        var validationRows = ordered.Skip(splitIndex).ToList();

        if (ordered.Count <= _options.WindowSize)
        {
            return CreateEmptyResult(ordered, trainingRows.Count, validationRows.Count, "insufficient total observations for LSTM.");
        }

        if (trainingRows.Count <= _options.WindowSize)
        {
            return CreateEmptyResult(ordered, trainingRows.Count, validationRows.Count, "insufficient training rows for LSTM (need more history than the window size).");
        }

        var statistics = ComputeStatistics(trainingRows);
        var trainingSequences = BuildSequences(trainingRows, statistics);

        if (trainingSequences.Count == 0)
        {
            return CreateEmptyResult(ordered, trainingRows.Count, validationRows.Count, "unable to build training sequences within the configured window.");
        }

        TrainModel(trainingSequences, statistics);

        var evaluationSeries = new List<ForecastEvaluationPoint>();
        var absoluteErrors = new List<double>();
        var squaredErrors = new List<double>();
        var percentageErrors = new List<double>();
        var history = new List<WeatherObservation>(trainingRows);

        foreach (var target in validationRows)
        {
            if (!TrySelectContext(history, target.Timestamp, out var context))
            {
                history.Add(target);
                continue;
            }

            var prediction = Predict(context, statistics);
            evaluationSeries.Add(new ForecastEvaluationPoint(target.Timestamp, target.Temperature, prediction));

            var error = target.Temperature - prediction;
            absoluteErrors.Add(Math.Abs(error));
            squaredErrors.Add(error * error);

            if (Math.Abs(target.Temperature) > double.Epsilon)
            {
                percentageErrors.Add(Math.Abs(error / target.Temperature));
            }

            history.Add(target);
        }

        ForecastMetrics? metrics = null;
        if (absoluteErrors.Count > 0)
        {
            metrics = new ForecastMetrics(
                MeanAbsoluteError: absoluteErrors.Average(),
                RootMeanSquareError: Math.Sqrt(squaredErrors.Average()),
                MeanAbsolutePercentageError: percentageErrors.Count > 0 ? percentageErrors.Average() * 100d : null,
                Samples: absoluteErrors.Count);
        }

        var nextPrediction = TryForecastNext(ordered, statistics);

        return new ForecastResult(
            Place: ordered[0].Place,
            TotalRecords: ordered.Count,
            TrainingRecords: trainingRows.Count,
            ValidationRecords: validationRows.Count,
            Metrics: metrics,
            ValidationSeries: evaluationSeries,
            NextPrediction: nextPrediction);
    }

    private ForecastResult CreateEmptyResult(
        IReadOnlyList<WeatherObservation> ordered,
        int trainingCount,
        int validationCount,
        string reason)
    {
        Console.WriteLine($"  Warning: {reason}");

        return new ForecastResult(
            Place: ordered.Count > 0 ? ordered[0].Place : string.Empty,
            TotalRecords: ordered.Count,
            TrainingRecords: trainingCount,
            ValidationRecords: validationCount,
            Metrics: null,
            ValidationSeries: Array.Empty<ForecastEvaluationPoint>(),
            NextPrediction: null);
    }

    private void TrainModel(List<SequenceSample> trainingSequences, NormalizationStatistics stats)
    {
        var inputs = torch.zeros(new long[] { trainingSequences.Count, _options.WindowSize, 1 }, ScalarType.Float32);
        var targets = torch.zeros(new long[] { trainingSequences.Count, 1 }, ScalarType.Float32);

        for (var i = 0; i < trainingSequences.Count; i++)
        {
            var sample = trainingSequences[i];
            for (var t = 0; t < sample.Sequence.Length; t++)
            {
                inputs[i, t, 0] = (float)sample.Sequence[t];
            }

            targets[i, 0] = (float)sample.Target;
        }

        using var inputsDevice = inputs.to(_device);
        using var targetsDevice = targets.to(_device);

        using var optimizer = torch.optim.Adam(_model.Parameters(), _options.LearningRate);
        using var lossFunction = nn.MSELoss();
        var random = new Random(42);
        var indices = Enumerable.Range(0, trainingSequences.Count).ToArray();

        for (var epoch = 1; epoch <= _options.TrainingEpochs; epoch++)
        {
            Shuffle(indices, random);
            var totalLoss = 0.0;
            var batchCount = 0;

            for (var batchStart = 0; batchStart < indices.Length; batchStart += _options.BatchSize)
            {
                var batchIndices = indices
                    .Skip(batchStart)
                    .Take(_options.BatchSize)
                    .Select(i => (long)i)
                    .ToArray();

                using var indexTensor = torch.tensor(batchIndices, dtype: ScalarType.Int64, device: _device);
                using var batchInputs = inputsDevice.index_select(0, indexTensor);
                using var batchTargets = targetsDevice.index_select(0, indexTensor);

                optimizer.zero_grad();

                using var output = _model.Forward(batchInputs);
                using var loss = lossFunction.forward(output, batchTargets);

                loss.backward();
                optimizer.step();

                totalLoss += loss.ToDouble();
                batchCount++;
            }

            if (batchCount > 0 && (epoch == 1 || epoch == _options.TrainingEpochs || epoch % 10 == 0))
            {
                var avgLoss = totalLoss / batchCount;
                Console.WriteLine($"  [LSTM] Epoch {epoch}/{_options.TrainingEpochs}, Loss={avgLoss:F4}");
            }
        }
    }

    private ForecastPrediction? TryForecastNext(IReadOnlyList<WeatherObservation> history, NormalizationStatistics stats)
    {
        if (history.Count <= _options.WindowSize)
        {
            return null;
        }

        var lastObservation = history[^1];
        var nextTimestamp = lastObservation.Timestamp + EstimateTypicalInterval(history);

        if (!TrySelectContext(history, nextTimestamp, out var context))
        {
            return null;
        }

        var prediction = Predict(context, stats);
        return new ForecastPrediction(nextTimestamp, prediction);
    }

    private double Predict(IReadOnlyList<WeatherObservation> context, NormalizationStatistics stats)
    {
        var sequence = NormalizeSequence(context, stats);

        using var input = torch.zeros(new long[] { 1, _options.WindowSize, 1 }, ScalarType.Float32, device: _device);
        for (var t = 0; t < sequence.Length; t++)
        {
            input[0, t, 0] = (float)sequence[t];
        }

        using var noGrad = no_grad();
        using var output = _model.Forward(input);
        var normalized = output.cpu()[0].ToDouble();
        return Denormalize(normalized, stats);
    }

    private List<SequenceSample> BuildSequences(IReadOnlyList<WeatherObservation> rows, NormalizationStatistics stats)
    {
        var sequences = new List<SequenceSample>();

        for (var idx = _options.WindowSize; idx < rows.Count; idx++)
        {
            var target = rows[idx];
            if (!TrySelectContext(rows.Take(idx).ToList(), target.Timestamp, out var context))
            {
                continue;
            }

            if (context.Count != _options.WindowSize)
            {
                continue;
            }

            var normalizedSequence = NormalizeSequence(context, stats);
            var normalizedTarget = Normalize(target.Temperature, stats);

            sequences.Add(new SequenceSample(normalizedSequence, normalizedTarget));
        }

        return sequences;
    }

    private bool TrySelectContext(IReadOnlyList<WeatherObservation> history, DateTime targetTimestamp, [NotNullWhen(true)] out List<WeatherObservation>? context)
    {
        if (history.Count < _options.WindowSize)
        {
            context = null;
            return false;
        }

        var filtered = history
            .Where(obs => obs.Timestamp < targetTimestamp)
            .OrderByDescending(obs => obs.Timestamp)
            .ToList();

        if (filtered.Count < _options.WindowSize)
        {
            context = null;
            return false;
        }

        var candidate = filtered.Take(_options.WindowSize).OrderBy(obs => obs.Timestamp).ToList();

        if (candidate[^1].Timestamp - candidate[0].Timestamp <= _options.WindowDuration || _options.AllowFallbackOutsideWindow)
        {
            context = candidate;
            return true;
        }

        context = null;
        return false;
    }

    private static NormalizationStatistics ComputeStatistics(IReadOnlyList<WeatherObservation> trainingRows)
    {
        var temperatures = trainingRows.Select(o => o.Temperature).ToArray();
        var mean = temperatures.Average();
        var variance = temperatures.Select(t => Math.Pow(t - mean, 2)).Average();
        var std = Math.Sqrt(Math.Max(variance, 1e-6));

        return new NormalizationStatistics(mean, std);
    }

    private double[] NormalizeSequence(IReadOnlyList<WeatherObservation> context, NormalizationStatistics stats)
    {
        var sequence = new double[context.Count];
        for (var i = 0; i < context.Count; i++)
        {
            sequence[i] = Normalize(context[i].Temperature, stats);
        }

        return sequence;
    }

    private static double Normalize(double value, NormalizationStatistics stats)
    {
        return (value - stats.Mean) / stats.StandardDeviation;
    }

    private static double Denormalize(double value, NormalizationStatistics stats)
    {
        return value * stats.StandardDeviation + stats.Mean;
    }

    private static TimeSpan EstimateTypicalInterval(IReadOnlyList<WeatherObservation> history)
    {
        if (history.Count < 2)
        {
            return TimeSpan.FromHours(3);
        }

        var deltas = new List<TimeSpan>(history.Count - 1);
        for (var i = 1; i < history.Count; i++)
        {
            var delta = history[i].Timestamp - history[i - 1].Timestamp;
            if (delta > TimeSpan.Zero)
            {
                deltas.Add(delta);
            }
        }

        if (deltas.Count == 0)
        {
            return TimeSpan.FromHours(3);
        }

        deltas.Sort();
        return deltas[deltas.Count / 2];
    }

    private static int DetermineTrainingIndex(int totalCount, int windowSize, double trainingFraction)
    {
        if (totalCount <= windowSize)
        {
            return Math.Max(1, totalCount - 1);
        }

        var index = (int)Math.Round(totalCount * trainingFraction, MidpointRounding.AwayFromZero);
        index = Math.Clamp(index, windowSize, totalCount - 1);
        return index;
    }

    private static void Shuffle<T>(IList<T> list, Random random)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public void Dispose()
    {
        _model.Dispose();
    }

    private sealed class LstmRegressionModel : IDisposable
    {
        private readonly LSTM _lstm;
        private readonly Linear _linear;

        public LstmRegressionModel(int inputSize, int hiddenSize, int numLayers)
        {
            _lstm = nn.LSTM(inputSize, hiddenSize, numLayers: numLayers, batchFirst: true);
            _linear = nn.Linear(hiddenSize, 1);
        }

        public Tensor Forward(Tensor input)
        {
            var (output, _, _) = _lstm.forward(input);
            var last = output.slice(1, output.shape[1] - 1, output.shape[1], 1);
            last = last.squeeze(1);
            return _linear.forward(last);
        }

        public IEnumerable<Parameter> Parameters()
        {
            foreach (var parameter in _lstm.parameters())
            {
                yield return parameter;
            }

            foreach (var parameter in _linear.parameters())
            {
                yield return parameter;
            }
        }

        public void Dispose()
        {
            _lstm.Dispose();
            _linear.Dispose();
        }
    }

    private sealed record SequenceSample(double[] Sequence, double Target);

    private sealed record NormalizationStatistics(double Mean, double StandardDeviation);
}



using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Serilog;
using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;

namespace Historical.Weather.Data.Forecaster.Processing;

internal sealed class LstmForecastProcessor : IDisposable
{
    private const int BaseFeatureCount = 6;

    private readonly ForecastOptions _options;
    private readonly Device _device;
    private LstmRegressionModel? _model;

    public LstmForecastProcessor(ForecastOptions options)
    {
        _options = options;
        _device = CPU;
    }

    public ForecastResult Process(IReadOnlyList<WeatherObservation> rawObservations)
    {
        var totalStopwatch = Stopwatch.StartNew();

        var ordered = rawObservations
            .OrderBy(o => o.Timestamp)
            .ToList();

        if (ordered.Count == 0)
        {
            return CreateEmptyResult(ordered, 0, 0, "no observations found.");
        }

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
        EnsureModel(statistics.FeatureCount);

        var sequenceStopwatch = Stopwatch.StartNew();
        var trainingSequences = BuildSequences(trainingRows, statistics);
        sequenceStopwatch.Stop();
        Log.Debug("  [LSTM] Built {SequenceCount} training sequence(s) in {ElapsedSeconds:F2} seconds.",
            trainingSequences.Count,
            sequenceStopwatch.Elapsed.TotalSeconds);
        if (trainingSequences.Count == 0)
        {
            return CreateEmptyResult(ordered, trainingRows.Count, validationRows.Count, "unable to build training sequences within the configured window.");
        }

        var trainingStopwatch = Stopwatch.StartNew();
        TrainModel(trainingSequences, statistics);
        trainingStopwatch.Stop();
        Log.Information("  [LSTM] Training completed in {ElapsedSeconds:F2} seconds.", trainingStopwatch.Elapsed.TotalSeconds);

        var evaluationStopwatch = Stopwatch.StartNew();
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
        evaluationStopwatch.Stop();
        Log.Information("  [LSTM] Evaluation (validation + forecasting) completed in {ElapsedSeconds:F2} seconds.",
            evaluationStopwatch.Elapsed.TotalSeconds);

        totalStopwatch.Stop();
        Log.Information("  [LSTM] Dataset processing time: {ElapsedSeconds:F2} seconds.", totalStopwatch.Elapsed.TotalSeconds);

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
        Log.Warning("  {Reason}", reason);

        return new ForecastResult(
            Place: ordered.Count > 0 ? ordered[0].Place : string.Empty,
            TotalRecords: ordered.Count,
            TrainingRecords: trainingCount,
            ValidationRecords: validationCount,
            Metrics: null,
            ValidationSeries: Array.Empty<ForecastEvaluationPoint>(),
            NextPrediction: null);
    }

    private void EnsureModel(int featureCount)
    {
        _model?.Dispose();
        _model = new LstmRegressionModel(featureCount, _options.HiddenSize, 1);
    }

    private void TrainModel(List<SequenceSample> trainingSequences, NormalizationStatistics stats)
    {
        if (_model is null)
        {
            throw new InvalidOperationException("Model has not been initialized.");
        }

        var featureCount = stats.FeatureCount;
        var sequenceLength = trainingSequences[0].Sequence.Length;

        var inputs = torch.zeros(new long[] { trainingSequences.Count, sequenceLength, featureCount }, ScalarType.Float32);
        var targets = torch.zeros(new long[] { trainingSequences.Count, 1 }, ScalarType.Float32);

        for (var i = 0; i < trainingSequences.Count; i++)
        {
            var sample = trainingSequences[i];
            for (var t = 0; t < sample.Sequence.Length; t++)
            {
                var timestep = sample.Sequence[t];
                for (var f = 0; f < featureCount; f++)
                {
                    inputs[i, t, f] = (float)timestep[f];
                }
            }

            targets[i, 0] = (float)sample.Target;
        }

        using var inputsDevice = inputs.to(_device);
        using var targetsDevice = targets.to(_device);

        using var optimizer = torch.optim.Adam(_model.Parameters(), _options.LearningRate);
        using var lossFunction = nn.MSELoss();
        var random = new Random(42);
        var indices = Enumerable.Range(0, trainingSequences.Count).ToArray();

        Log.Information("  [LSTM] Training {SequenceCount} sequence(s) with {FeatureCount} feature(s) each, batch size {BatchSize}, epochs {Epochs}.",
            trainingSequences.Count, featureCount, _options.BatchSize, _options.TrainingEpochs);

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

            if (batchCount > 0)
            {
                var avgLoss = totalLoss / batchCount;
                Log.Information("  [LSTM] Epoch {Epoch}/{TotalEpochs}, Batches={BatchCount}, AvgLoss={AverageLoss:F4}",
                    epoch, _options.TrainingEpochs, batchCount, avgLoss);
            }
            else
            {
                Log.Warning("  [LSTM] Epoch {Epoch}/{TotalEpochs}, no batches processed.", epoch, _options.TrainingEpochs);
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
        if (_model is null)
        {
            throw new InvalidOperationException("Model has not been initialized.");
        }

        var normalizedSequence = NormalizeSequence(context, stats);
        var featureCount = stats.FeatureCount;

        using var input = torch.zeros(new long[] { 1, normalizedSequence.Length, featureCount }, ScalarType.Float32, device: _device);
        for (var t = 0; t < normalizedSequence.Length; t++)
        {
            var timestep = normalizedSequence[t];
            for (var f = 0; f < featureCount; f++)
            {
                input[0, t, f] = (float)timestep[f];
            }
        }

        using var noGrad = no_grad();
        using var output = _model.Forward(input);
        var normalized = output.cpu()[0].ToDouble();
        return DenormalizeTarget(normalized, stats);
    }

    private List<SequenceSample> BuildSequences(IReadOnlyList<WeatherObservation> rows, NormalizationStatistics stats)
    {
        var sequences = new List<SequenceSample>();
        var history = new List<WeatherObservation>();
        var distinctDayCount = 0;
        DateTime? lastLoggedDay = null;

        foreach (var observation in rows)
        {
            var day = observation.Timestamp.Date;
            if (lastLoggedDay != day)
            {
                lastLoggedDay = day;
                distinctDayCount++;

                if (distinctDayCount % 100 == 0)
                {
                    Log.Debug("  [LSTM] Training progress: processed {DayCount} day(s); current day {Date}",
                        distinctDayCount, day);
                }
            }

            if (!TrySelectContext(history, observation.Timestamp, out var context))
            {
                history.Add(observation);
                continue;
            }

            if (context.Count == 0)
            {
                history.Add(observation);
                continue;
            }

            var normalizedSequence = NormalizeSequence(context, stats);
            var normalizedTarget = NormalizeTarget(observation.Temperature, stats);

            sequences.Add(new SequenceSample(normalizedSequence, normalizedTarget));
            history.Add(observation);
        }

        Log.Information("  [LSTM] Training set covers {TotalDays} distinct day(s).", distinctDayCount);

        return sequences;
    }

    private bool TrySelectContext(
        IReadOnlyList<WeatherObservation> history,
        DateTime targetTimestamp,
        [NotNullWhen(true)] out List<WeatherObservation>? context)
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

        var candidate = filtered
            .Take(_options.WindowSize)
            .OrderBy(obs => obs.Timestamp)
            .ToList();

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
        var featureCount = trainingRows[0].FeatureVector.Length;
        var featureSums = new double[featureCount];
        var featureSquares = new double[featureCount];
        double targetSum = 0d;
        double targetSquares = 0d;

        foreach (var row in trainingRows)
        {
            var features = row.FeatureVector;
            for (var i = 0; i < featureCount; i++)
            {
                var value = features[i];
                if (i < BaseFeatureCount)
                {
                    featureSums[i] += value;
                    featureSquares[i] += value * value;
                }
            }

            targetSum += row.Temperature;
            targetSquares += row.Temperature * row.Temperature;
        }

        var sampleCount = trainingRows.Count;
        var featureMeans = new double[featureCount];
        var featureStd = new double[featureCount];

        for (var i = 0; i < featureCount; i++)
        {
            if (i < BaseFeatureCount)
            {
                var mean = featureSums[i] / sampleCount;
                var variance = featureSquares[i] / sampleCount - mean * mean;
                featureMeans[i] = mean;
                featureStd[i] = Math.Sqrt(Math.Max(variance, 1e-6));
            }
            else
            {
                featureMeans[i] = 0d;
                featureStd[i] = 1d;
            }
        }

        var targetMean = targetSum / sampleCount;
        var targetVariance = targetSquares / sampleCount - targetMean * targetMean;
        var targetStd = Math.Sqrt(Math.Max(targetVariance, 1e-6));

        return new NormalizationStatistics(featureMeans, featureStd, targetMean, targetStd, BaseFeatureCount);
    }

    private double[][] NormalizeSequence(IReadOnlyList<WeatherObservation> context, NormalizationStatistics stats)
    {
        var result = new double[context.Count][];
        for (var i = 0; i < context.Count; i++)
        {
            result[i] = NormalizeFeatureVector(context[i].FeatureVector, stats);
        }

        return result;
    }

    private double[] NormalizeFeatureVector(double[] features, NormalizationStatistics stats)
    {
        var normalized = new double[stats.FeatureCount];
        for (var i = 0; i < stats.FeatureCount; i++)
        {
            if (i < stats.BaseFeatureCount)
            {
                normalized[i] = (features[i] - stats.FeatureMeans[i]) / stats.FeatureStandardDeviations[i];
            }
            else
            {
                normalized[i] = features[i];
            }
        }

        return normalized;
    }

    private double NormalizeTarget(double value, NormalizationStatistics stats)
    {
        return (value - stats.TargetMean) / stats.TargetStandardDeviation;
    }

    private double DenormalizeTarget(double value, NormalizationStatistics stats)
    {
        return value * stats.TargetStandardDeviation + stats.TargetMean;
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
        _model?.Dispose();
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

    private sealed record SequenceSample(double[][] Sequence, double Target);

    private sealed record NormalizationStatistics(
        double[] FeatureMeans,
        double[] FeatureStandardDeviations,
        double TargetMean,
        double TargetStandardDeviation,
        int BaseFeatureCount)
    {
        public int FeatureCount => FeatureMeans.Length;
    }
}


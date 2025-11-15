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
        
        // Configure TorchSharp to use specified or all available CPU cores
        var availableCores = Environment.ProcessorCount;
        var numThreads = options.CpuCores ?? availableCores;
        
        // Validate the number of cores
        if (numThreads <= 0)
        {
            throw new ArgumentException($"Number of CPU cores must be greater than zero, but was {numThreads}.", nameof(options));
        }
        
        if (numThreads > availableCores)
        {
            throw new ArgumentException($"Number of CPU cores ({numThreads}) cannot exceed the number of available cores ({availableCores}).", nameof(options));
        }
        
        torch.set_num_threads(numThreads);
        torch.set_num_interop_threads(Math.Max(1, numThreads / 2)); // Inter-op parallelism
        
        Log.Information("  [LSTM] Configured to use {ThreadCount} CPU thread(s) for training (out of {AvailableCores} available).", numThreads, availableCores);
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
        Log.Debug("  [LSTM] Built {SequenceCount} training sequence(s) in {Elapsed}.",
            trainingSequences.Count,
            FormatDuration(sequenceStopwatch.Elapsed));
        if (trainingSequences.Count == 0)
        {
            return CreateEmptyResult(ordered, trainingRows.Count, validationRows.Count, "unable to build training sequences within the configured window.");
        }

        var trainingStopwatch = Stopwatch.StartNew();
        TrainModel(trainingSequences, statistics);
        trainingStopwatch.Stop();
        Log.Information("  [LSTM] Training completed in {Elapsed}.", FormatDuration(trainingStopwatch.Elapsed));

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
        Log.Information("  [LSTM] Evaluation (validation + forecasting) completed in {Elapsed}.",
            FormatDuration(evaluationStopwatch.Elapsed));

        totalStopwatch.Stop();
        Log.Information("  [LSTM] Dataset processing time: {Elapsed}.", FormatDuration(totalStopwatch.Elapsed));

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
        var totalBatchesPerEpoch = (int)Math.Ceiling((double)indices.Length / _options.BatchSize);
        var totalSteps = totalBatchesPerEpoch * _options.TrainingEpochs;

        Log.Information("  [LSTM] Training {SequenceCount} sequence(s) with {FeatureCount} feature(s) each, batch size {BatchSize}, epochs {Epochs}.",
            trainingSequences.Count, featureCount, _options.BatchSize, _options.TrainingEpochs);
        Log.Information("  [LSTM] Total batches per epoch: {BatchesPerEpoch}, Total training steps: {TotalSteps}",
            totalBatchesPerEpoch, totalSteps);

        var trainingStopwatch = Stopwatch.StartNew();
        var stepCounter = 0;
        var cumulativeLoss = 0.0;
        var cumulativeStepCount = 0;
        var previousEpochAvgLoss = double.NaN;
        var recentLosses = new List<double>(100); // Track last 100 losses for trend analysis
        var epochStartLoss = double.NaN;
        
        for (var epoch = 1; epoch <= _options.TrainingEpochs; epoch++)
        {
            var epochStopwatch = Stopwatch.StartNew();
            Shuffle(indices, random);
            var totalLoss = 0.0;
            var batchCount = 0;
            epochStartLoss = double.NaN;

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
                
                // Compute gradient statistics before optimizer step (only every 100th step to avoid overhead)
                double? gradientNorm = null;
                double? maxGradient = null;
                double? minGradient = null;
                if ((stepCounter + 1) % 100 == 0)
                {
                    try
                    {
                        var gradNorms = new List<double>();
                        var allGradValues = new List<double>();
                        foreach (var param in _model.Parameters())
                        {
                            var grad = param.grad();
                            if (grad != null && grad.requires_grad())
                            {
                                using (var gradFlat = grad.flatten())
                                {
                                    var norm = gradFlat.norm().ToDouble();
                                    gradNorms.Add(norm);
                                    
                                    using (var gradCpu = gradFlat.cpu())
                                    {
                                        var gradData = gradCpu.data<float>();
                                        if (gradData != null && gradData.Length > 0)
                                        {
                                            foreach (var val in gradData)
                                            {
                                                allGradValues.Add((double)val);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        if (gradNorms.Count > 0)
                        {
                            gradientNorm = gradNorms.Sum(); // Total gradient norm across all parameters
                        }
                        if (allGradValues.Count > 0)
                        {
                            maxGradient = allGradValues.Max();
                            minGradient = allGradValues.Min();
                        }
                    }
                    catch
                    {
                        // If gradient computation fails, continue without it
                    }
                }

                optimizer.step();

                var currentLoss = loss.ToDouble();
                if (double.IsNaN(epochStartLoss))
                {
                    epochStartLoss = currentLoss;
                }
                totalLoss += currentLoss;
                cumulativeLoss += currentLoss;
                batchCount++;
                stepCounter++;
                cumulativeStepCount++;
                
                // Track recent losses for trend analysis
                recentLosses.Add(currentLoss);
                if (recentLosses.Count > 100)
                {
                    recentLosses.RemoveAt(0);
                }

                if (stepCounter % 100 == 0)
                {
                    var avgLossSoFar = cumulativeLoss / cumulativeStepCount;
                    var epochAvgLoss = totalLoss / batchCount;
                    var elapsed = trainingStopwatch.Elapsed;
                    var stepsPerSecond = elapsed.TotalSeconds > 0 
                        ? stepCounter / elapsed.TotalSeconds 
                        : 0.0;
                    var estimatedTimeRemaining = totalSteps > stepCounter && stepsPerSecond > 0
                        ? TimeSpan.FromSeconds((totalSteps - stepCounter) / stepsPerSecond)
                        : TimeSpan.Zero;
                    var progressPercent = (double)stepCounter / totalSteps * 100.0;
                    
                    // Compute loss trend
                    var lossTrend = recentLosses.Count >= 50 
                        ? (recentLosses.TakeLast(25).Average() - recentLosses.Take(25).Average()) / recentLosses.Take(25).Average() * 100.0
                        : double.NaN;
                    
                    // Compute epoch loss change
                    var epochLossChange = !double.IsNaN(previousEpochAvgLoss) && previousEpochAvgLoss > 0
                        ? (epochAvgLoss - previousEpochAvgLoss) / previousEpochAvgLoss * 100.0
                        : double.NaN;
                    
                    // Compute epoch start to current change
                    var epochProgressLossChange = !double.IsNaN(epochStartLoss) && epochStartLoss > 0
                        ? (currentLoss - epochStartLoss) / epochStartLoss * 100.0
                        : double.NaN;

                    Log.Information("  [LSTM] ========== Training Progress Report (Step {Step}/{TotalSteps}) ==========", stepCounter, totalSteps);
                    Log.Information("  [LSTM] Progress: {Progress:F2}% | Epoch {Epoch}/{TotalEpochs} | Batch {Batch}/{TotalBatches} | Sequence Length: {SeqLength} | Features: {Features}",
                        progressPercent, epoch, _options.TrainingEpochs, batchCount, totalBatchesPerEpoch, sequenceLength, featureCount);
                    Log.Information("  [LSTM] Loss Metrics:");
                    Log.Information("  [LSTM]   Current Batch Loss:     {CurrentLoss:F8}", currentLoss);
                    Log.Information("  [LSTM]   Epoch Average Loss:     {EpochAvgLoss:F8} (over {BatchCount} batches)", epochAvgLoss, batchCount);
                    Log.Information("  [LSTM]   Overall Average Loss:   {AvgLoss:F8} (over {TotalSteps} steps)", avgLossSoFar, cumulativeStepCount);
                    if (!double.IsNaN(epochLossChange))
                    {
                        Log.Information("  [LSTM]   Epoch Loss Change:      {Change:F2}% vs previous epoch", epochLossChange);
                    }
                    if (!double.IsNaN(epochProgressLossChange))
                    {
                        Log.Information("  [LSTM]   Epoch Progress Change:  {Change:F2}% vs epoch start", epochProgressLossChange);
                    }
                    if (!double.IsNaN(lossTrend))
                    {
                        Log.Information("  [LSTM]   Recent Loss Trend:      {Trend:F2}% (last 25 vs first 25 of recent 50)", lossTrend);
                    }
                    Log.Information("  [LSTM] Training Configuration:");
                    Log.Information("  [LSTM]   Learning Rate:          {LearningRate:E4}", _options.LearningRate);
                    Log.Information("  [LSTM]   Batch Size:              {BatchSize} | Actual Batch: {ActualBatchSize}", _options.BatchSize, batchIndices.Length);
                    Log.Information("  [LSTM]   Hidden Size:             {HiddenSize}", _options.HiddenSize);
                    if (gradientNorm.HasValue)
                    {
                        Log.Information("  [LSTM]   Gradient Norm:           {GradNorm:F6}", gradientNorm.Value);
                    }
                    if (maxGradient.HasValue && minGradient.HasValue)
                    {
                        Log.Information("  [LSTM]   Gradient Range:          [{MinGrad:F6}, {MaxGrad:F6}]", minGradient.Value, maxGradient.Value);
                    }
                    Log.Information("  [LSTM] Performance Metrics:");
                    var batchesPerSecond = elapsed.TotalSeconds > 0 
                        ? cumulativeStepCount / elapsed.TotalSeconds 
                        : 0.0;
                    Log.Information("  [LSTM]   Training Speed:          {StepsPerSec:F2} steps/sec ({BatchesPerSec:F2} batches/sec)",
                        stepsPerSecond, batchesPerSecond);
                    Log.Information("  [LSTM]   Elapsed Time:            {Elapsed}", FormatDuration(elapsed));
                    Log.Information("  [LSTM]   Estimated Time Remaining: {ETA}", FormatDuration(estimatedTimeRemaining));
                    var epochElapsed = epochStopwatch.Elapsed;
                    var epochETA = batchCount > 0 && epochElapsed.TotalSeconds > 0
                        ? TimeSpan.FromSeconds((totalBatchesPerEpoch - batchCount) * (epochElapsed.TotalSeconds / batchCount))
                        : TimeSpan.Zero;
                    Log.Information("  [LSTM]   Epoch Elapsed:           {EpochElapsed} | Epoch ETA: {EpochETA}",
                        FormatDuration(epochElapsed), FormatDuration(epochETA));
                    Log.Information("  [LSTM] =================================================================");
                }
            }

            epochStopwatch.Stop();
            if (batchCount > 0)
            {
                var avgLoss = totalLoss / batchCount;
                previousEpochAvgLoss = avgLoss;
                Log.Debug("  [LSTM] Epoch {Epoch}/{TotalEpochs} completed in {Elapsed} - Batches: {BatchCount}, AvgLoss: {AverageLoss:F6}",
                    epoch, _options.TrainingEpochs, FormatDuration(epochStopwatch.Elapsed), batchCount, avgLoss);
            }
            else
            {
                Log.Warning("  [LSTM] Epoch {Epoch}/{TotalEpochs}, no batches processed.", epoch, _options.TrainingEpochs);
            }
        }
        
        trainingStopwatch.Stop();
        var finalAvgLoss = cumulativeLoss / cumulativeStepCount;
        Log.Information("  [LSTM] Training Summary - Total Steps: {TotalSteps} | Final Average Loss: {FinalAvgLoss:F6} | Total Time: {TotalTime}",
            cumulativeStepCount, finalAvgLoss, FormatDuration(trainingStopwatch.Elapsed));
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

    private static string FormatDuration(TimeSpan duration)
    {
        var totalHours = (int)duration.TotalHours;
        return $"{totalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
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


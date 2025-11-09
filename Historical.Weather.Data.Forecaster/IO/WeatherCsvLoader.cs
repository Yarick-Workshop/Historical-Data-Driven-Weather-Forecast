using System.Globalization;
using CsvHelper;
using Historical.Weather.Core;
using Serilog;

namespace Historical.Weather.Data.Forecaster.IO;

internal sealed class WeatherCsvLoader
{
    private static readonly IReadOnlyList<string> CharacteristicColumns = WeatherCharacteristicConverter
        .GetAllKnownCharacteristics();

    public async Task<IReadOnlyList<WeatherObservation>> LoadAsync(string csvPath)
    {
        if (!File.Exists(csvPath))
        {
            throw new FileNotFoundException("CSV file was not found.", csvPath);
        }

        await using var stream = File.OpenRead(csvPath);
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        var records = new List<WeatherObservation>();
        if (!await csv.ReadAsync())
        {
            return Array.Empty<WeatherObservation>();
        }

        csv.ReadHeader();
        var header = csv.HeaderRecord ?? Array.Empty<string>();
        var headerLookup = header
            .Select((value, index) => (value, index))
            .GroupBy(pair => pair.value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.First().index,
                StringComparer.OrdinalIgnoreCase);

        var characteristicIndexes = CharacteristicColumns
            .Select(name => headerLookup.TryGetValue(name, out var index) ? index : -1)
            .ToArray();

        while (await csv.ReadAsync())
        {
            try
            {
                var place = csv.GetField(WeatherCsvColumns.Place);
                if (string.IsNullOrWhiteSpace(place))
                {
                    continue;
                }

                var timestampValue = csv.GetField(WeatherCsvColumns.DateTime);
                if (!DateTime.TryParse(timestampValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var timestamp))
                {
                    continue;
                }

                if (!double.TryParse(csv.GetField(WeatherCsvColumns.Temperature), NumberStyles.Float, CultureInfo.InvariantCulture, out var temperature))
                {
                    continue;
                }

                var windSpeed = ParseDouble(csv.GetField(WeatherCsvColumns.WindSpeed));
                var (windDirSin, windDirCos) = EncodeWindDirection(csv.GetField(WeatherCsvColumns.WindDirection));
                var pressure = ParseDouble(csv.GetField(WeatherCsvColumns.AtmosphericPressure));
                var humidity = ParseDouble(csv.GetField(WeatherCsvColumns.Humidity));

                var features = new double[6 + characteristicIndexes.Length];
                var offset = 0;
                features[offset++] = temperature;
                features[offset++] = windSpeed;
                features[offset++] = windDirSin;
                features[offset++] = windDirCos;
                features[offset++] = pressure;
                features[offset++] = humidity;

                var presentCharacteristics = new List<string>();
                for (var i = 0; i < characteristicIndexes.Length; i++)
                {
                    var index = characteristicIndexes[i];
                    var value = index >= 0 ? csv.GetField(index) : string.Empty;
                    var isPresent = IsPositive(value);
                    features[offset + i] = isPresent ? 1.0 : 0.0;
                    if (isPresent)
                    {
                        presentCharacteristics.Add(CharacteristicColumns[i]);
                    }
                }

                var characteristics = WeatherCharacteristicConverter.FromStrings(presentCharacteristics, csv.Parser?.RawRow.ToString());
                records.Add(new WeatherObservation(place, timestamp, temperature, features, characteristics));
            }
            catch (Exception ex)
            {
                Log.Warning("  Unable to parse row {RowNumber}: {Message}", csv.Parser?.RawRow ?? -1, ex.Message);
            }
        }

        return records
            .OrderBy(r => r.Timestamp)
            .ToList();
    }

    private static double ParseDouble(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0d;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.GetCultureInfo("ru-RU"), out result))
        {
            return result;
        }

        return 0d;
    }

    private static bool IsPositive(string? value)
    {
        return value is "1" or "True" or "true";
    }

    private static (double Sin, double Cos) EncodeWindDirection(string? direction)
    {
        if (string.IsNullOrWhiteSpace(direction))
        {
            return (0d, 0d);
        }

        var normalized = direction.Trim().ToLowerInvariant();
        var hasNorth = normalized.Contains("север");
        var hasSouth = normalized.Contains("юг");
        var hasEast = normalized.Contains("вост");
        var hasWest = normalized.Contains("зап");

        double angle;
        if (hasNorth && hasEast)
        {
            angle = 45d;
        }
        else if (hasSouth && hasEast)
        {
            angle = 135d;
        }
        else if (hasSouth && hasWest)
        {
            angle = 225d;
        }
        else if (hasNorth && hasWest)
        {
            angle = 315d;
        }
        else if (hasNorth)
        {
            angle = 0d;
        }
        else if (hasEast)
        {
            angle = 90d;
        }
        else if (hasSouth)
        {
            angle = 180d;
        }
        else if (hasWest)
        {
            angle = 270d;
        }
        else
        {
            return (0d, 0d);
        }

        var radians = angle * Math.PI / 180d;
        return (Math.Sin(radians), Math.Cos(radians));
    }
}

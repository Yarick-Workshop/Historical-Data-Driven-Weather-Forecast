using System.Globalization;
using CsvHelper;
using Historical.Weather.Core;

namespace Historical.Weather.Data.Forecaster.IO;

internal sealed class WeatherCsvLoader
{
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

        while (await csv.ReadAsync())
        {
            try
            {
                var place = csv.GetField(WeatherCsvColumns.Place);

                if (string.IsNullOrWhiteSpace(place))
                {
                    continue;
                }
                var timestamp = csv.GetField(WeatherCsvColumns.DateTime);
                var temperature = csv.GetField(WeatherCsvColumns.Temperature);

                if (!DateTime.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var time))
                {
                    continue;
                }

                if (!double.TryParse(temperature, NumberStyles.Number, CultureInfo.InvariantCulture, out var tempValue))
                {
                    continue;
                }

                records.Add(new WeatherObservation(place, time, tempValue));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: unable to parse row {csv.Parser.RawRow}: {ex.Message}");
            }
        }

        return records
            .OrderBy(r => r.Timestamp)
            .ToList();
    }
}



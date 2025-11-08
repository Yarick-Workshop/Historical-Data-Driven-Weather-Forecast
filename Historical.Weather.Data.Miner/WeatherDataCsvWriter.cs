using CsvHelper;
using Serilog;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Historical.Weather.Data.Miner;

public static class WeatherDataCsvWriter
{
    public static void WriteNormalizedResultsByPlace(
        Dictionary<string, SortedDictionary<DateTime, (string FilePath, HtmlParseResult Result)>> normalizedResultsByPlace,
        string outputDirectory)
    {
        if (normalizedResultsByPlace.Count == 0)
        {
            Log.Warning("No normalized results available to write to CSV.");
            return;
        }

        Directory.CreateDirectory(outputDirectory);

        foreach (var (place, resultsByDate) in normalizedResultsByPlace)
        {
            if (resultsByDate.Count == 0)
            {
                Log.Debug("Skipping CSV generation for {Place} because it has no normalized rows.", place);
                continue;
            }

            var configurations = PrepareCsvConfigurations(resultsByDate);
            var records = PrepareCsvRecords(place, resultsByDate, configurations);

            var safeFileName = GetSafeFileName($"{place}.csv");
            var csvPath = Path.Combine(outputDirectory, safeFileName);

            using var writer = new StreamWriter(csvPath, false);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

            foreach (var header in configurations.Headers)
            {
                csv.WriteField(header);
            }
            csv.NextRecord();

            foreach (var record in records)
            {
                foreach (var value in record)
                {
                    csv.WriteField(value);
                }
                csv.NextRecord();
            }

            Log.Information("Wrote normalized CSV for {Place} to {CsvPath}", place, csvPath);
        }
    }

    private static CsvHeaderConfiguration PrepareCsvConfigurations(
        SortedDictionary<DateTime, (string FilePath, HtmlParseResult Result)> resultsByDate)
    {
        var baseHeaders = new List<string>
        {
            "Place",
            "DateTime",
            "Temperature",
            "WindDirection",
            "WindSpeed",
            "AtmosphericPressure",
            "Humidity"
        };

        var characteristicHeaders = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, entry) in resultsByDate)
        {
            foreach (var row in entry.Result.WeatherDataRows)
            {
                var characteristics = WeatherCharacteristicConverter.ToStrings(row.WeatherCharacteristics);
                foreach (var characteristic in characteristics)
                {
                    characteristicHeaders.Add(characteristic);
                }
            }
        }

        baseHeaders.AddRange(characteristicHeaders);

        return new CsvHeaderConfiguration
        {
            Headers = baseHeaders,
            CharacteristicHeaders = characteristicHeaders.ToList()
        };
    }

    private static List<List<string>> PrepareCsvRecords(
        string place,
        SortedDictionary<DateTime, (string FilePath, HtmlParseResult Result)> resultsByDate,
        CsvHeaderConfiguration configuration)
    {
        var records = new List<List<string>>();
        var characteristicHeaders = configuration.CharacteristicHeaders;

        foreach (var (_, entry) in resultsByDate)
        {
            var orderedRows = entry.Result.WeatherDataRows
                .OrderBy(row => row.Time)
                .ToList();

            foreach (var row in orderedRows)
            {
                var record = new List<string>
                {
                    place,
                    row.Time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    row.Temperature.ToString(CultureInfo.InvariantCulture),
                    row.WindDirection,
                    row.WindSpeed.ToString(CultureInfo.InvariantCulture),
                    row.AtmosphericPressure.ToString(CultureInfo.InvariantCulture),
                    row.Humidity.ToString(CultureInfo.InvariantCulture)
                };

                var characteristicsSet = new HashSet<string>(WeatherCharacteristicConverter.ToStrings(row.WeatherCharacteristics), StringComparer.OrdinalIgnoreCase);

                foreach (var characteristicHeader in characteristicHeaders)
                {
                    record.Add(characteristicsSet.Contains(characteristicHeader) ? "1" : string.Empty);
                }

                records.Add(record);
            }
        }

        return records;
    }

    private static string GetSafeFileName(string fileName)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidChar, '_');
        }

        return fileName;
    }

    private sealed class CsvHeaderConfiguration
    {
        public required List<string> Headers { get; init; }
        public required List<string> CharacteristicHeaders { get; init; }
    }
}


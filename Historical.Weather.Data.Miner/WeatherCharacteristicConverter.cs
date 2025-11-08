using System;
using System.Collections.Generic;
using System.Linq;

namespace Historical.Weather.Data.Miner;

public static class WeatherCharacteristicConverter
{
    private static readonly Dictionary<string, WeatherCharacteristics> StringToFlagMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "гололед", WeatherCharacteristics.BlackIce },
        { "град", WeatherCharacteristics.Hail },
        { "гроза", WeatherCharacteristics.Thunderstorm },
        { "дождь", WeatherCharacteristics.Rain },
        { "дождь с градом", WeatherCharacteristics.RainAndHail },
        { "дождь с грозой", WeatherCharacteristics.RainAndThunderstorm },
        { "дождь с грозой и градом", WeatherCharacteristics.RainThunderstormAndHail },
        { "дождь со снегом", WeatherCharacteristics.RainAndSnow },
        { "дымка", WeatherCharacteristics.Haze },
        { "ледяной дождь", WeatherCharacteristics.FreezingRain },
        { "ливневый дождь", WeatherCharacteristics.ShowerRain },
        { "ливневый дождь со снегом", WeatherCharacteristics.ShowerRainWithSnow },
        { "мгла", WeatherCharacteristics.Mist },
        { "мряка", WeatherCharacteristics.Drizzle },
        { "небольшая облачность", WeatherCharacteristics.FewClouds },
        { "переменная облачность", WeatherCharacteristics.VariableCloudiness },
        { "песчанная буря", WeatherCharacteristics.Sandstorm },
        { "преимущественно облачно", WeatherCharacteristics.MostlyCloudy },
        { "преимущественно ясно", WeatherCharacteristics.MostlyClear },
        { "сильная гроза", WeatherCharacteristics.SevereThunderstorm },
        { "сильная снежная крупа", WeatherCharacteristics.HeavySnowPellets },
        { "сильный дождь", WeatherCharacteristics.HeavyRain },
        { "сильный дождь со снегом", WeatherCharacteristics.HeavyRainWithSnow },
        { "сильный ливневый дождь", WeatherCharacteristics.HeavyShowerRain },
        { "сильный снег", WeatherCharacteristics.HeavySnow },
        { "слабая метель", WeatherCharacteristics.LightBlizzard },
        { "слабая мряка", WeatherCharacteristics.LightDrizzle },
        { "слабая снежная крупа", WeatherCharacteristics.LightSnowPellets },
        { "слабый град", WeatherCharacteristics.LightHail },
        { "слабый дождь", WeatherCharacteristics.LightRain },
        { "слабый дождь с грозой", WeatherCharacteristics.LightRainWithThunderstorm },
        { "слабый дождь со снегом", WeatherCharacteristics.LightRainWithSnow },
        { "слабый ливневый дождь", WeatherCharacteristics.LightShowerRain },
        { "слабый ливневый дождь со снегом", WeatherCharacteristics.LightShowerRainWithSnow },
        { "слабый поземок", WeatherCharacteristics.LightGroundBlizzard },
        { "слабый снег", WeatherCharacteristics.LightSnow },
        { "слабый туман", WeatherCharacteristics.LightFog },
        { "снег", WeatherCharacteristics.Snow },
        { "сплошная облачность", WeatherCharacteristics.Overcast },
        { "туман", WeatherCharacteristics.Fog },
        { "ухудшение видимости из-за дыма", WeatherCharacteristics.ReducedVisibilityDueToSmoke },
        { "частично облачно", WeatherCharacteristics.PartlyCloudy },
        { "ясно", WeatherCharacteristics.Clear }
    };

    public static WeatherCharacteristics FromStrings(IEnumerable<string> values, string? context = null)
    {
        if (values == null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        var aggregate = WeatherCharacteristics.None;
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!StringToFlagMap.TryGetValue(value, out var flag))
            {
                var location = string.IsNullOrEmpty(context) ? string.Empty : $" in {context}";
                throw new InvalidOperationException($"Unknown weather characteristic '{value}'{location}.");
            }

            aggregate |= flag;
        }

        return aggregate;
    }    

    public static IReadOnlyList<string> GetAllKnownCharacteristics()
    {
        return StringToFlagMap.Keys
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}


using System;

namespace Historical.Weather.Data.Miner;

[Flags]
public enum WeatherCharacteristics : long
{
    // нет данных
    None = 0,
    // гололед
    BlackIce = 1L << 0,
    // град
    Hail = 1L << 1,
    // гроза
    Thunderstorm = 1L << 2,
    // дождь
    Rain = 1L << 3,
    // дождь с градом
    RainAndHail = 1L << 4,
    // дождь с грозой
    RainAndThunderstorm = 1L << 5,
    // дождь с грозой и градом
    RainThunderstormAndHail = 1L << 6,
    // дождь со снегом
    RainAndSnow = 1L << 7,
    // дымка
    Haze = 1L << 8,
    // ледяной дождь
    FreezingRain = 1L << 9,
    // ливневый дождь
    ShowerRain = 1L << 10,
    // ливневый дождь со снегом
    ShowerRainWithSnow = 1L << 11,
    // мгла
    Mist = 1L << 12,
    // мряка
    Drizzle = 1L << 13,
    // небольшая облачность
    FewClouds = 1L << 14,
    // переменная облачность
    VariableCloudiness = 1L << 15,
    // песчанная буря
    Sandstorm = 1L << 16,
    // преимущественно облачно
    MostlyCloudy = 1L << 17,
    // преимущественно ясно
    MostlyClear = 1L << 18,
    // сильная гроза
    SevereThunderstorm = 1L << 19,
    // сильная снежная крупа
    HeavySnowPellets = 1L << 20,
    // сильный дождь
    HeavyRain = 1L << 21,
    // сильный дождь со снегом
    HeavyRainWithSnow = 1L << 22,
    // сильный ливневый дождь
    HeavyShowerRain = 1L << 23,
    // сильный снег
    HeavySnow = 1L << 24,
    // слабая метель
    LightBlizzard = 1L << 25,
    // слабая мряка
    LightDrizzle = 1L << 26,
    // слабая снежная крупа
    LightSnowPellets = 1L << 27,
    // слабый град
    LightHail = 1L << 28,
    // слабый дождь
    LightRain = 1L << 29,
    // слабый дождь с грозой
    LightRainWithThunderstorm = 1L << 30,
    // слабый дождь со снегом
    LightRainWithSnow = 1L << 31,
    // слабый ливневый дождь
    LightShowerRain = 1L << 32,
    // слабый ливневый дождь со снегом
    LightShowerRainWithSnow = 1L << 33,
    // слабый поземок
    LightGroundBlizzard = 1L << 34,
    // слабый снег
    LightSnow = 1L << 35,
    // слабый туман
    LightFog = 1L << 36,
    // снег
    Snow = 1L << 37,
    // сплошная облачность
    Overcast = 1L << 38,
    // туман
    Fog = 1L << 39,
    // ухудшение видимости из-за дыма
    ReducedVisibilityDueToSmoke = 1L << 40,
    // частично облачно
    PartlyCloudy = 1L << 41,
    // ясно
    Clear = 1L << 42
}


using System;

namespace Historical.Weather.Core;

[Flags]
public enum WeatherCharacteristics : long
{
    None = 0,
    BlackIce = 1L << 0,
    Hail = 1L << 1,
    Thunderstorm = 1L << 2,
    Rain = 1L << 3,
    RainAndHail = 1L << 4,
    RainAndThunderstorm = 1L << 5,
    RainThunderstormAndHail = 1L << 6,
    RainAndSnow = 1L << 7,
    Haze = 1L << 8,
    FreezingRain = 1L << 9,
    ShowerRain = 1L << 10,
    ShowerRainWithSnow = 1L << 11,
    Mist = 1L << 12,
    Drizzle = 1L << 13,
    FewClouds = 1L << 14,
    VariableCloudiness = 1L << 15,
    Sandstorm = 1L << 16,
    MostlyCloudy = 1L << 17,
    MostlyClear = 1L << 18,
    SevereThunderstorm = 1L << 19,
    HeavySnowPellets = 1L << 20,
    HeavyRain = 1L << 21,
    HeavyRainWithSnow = 1L << 22,
    HeavyShowerRain = 1L << 23,
    HeavySnow = 1L << 24,
    LightBlizzard = 1L << 25,
    LightDrizzle = 1L << 26,
    LightSnowPellets = 1L << 27,
    LightHail = 1L << 28,
    LightRain = 1L << 29,
    LightRainWithThunderstorm = 1L << 30,
    LightRainWithSnow = 1L << 31,
    LightShowerRain = 1L << 32,
    LightShowerRainWithSnow = 1L << 33,
    LightGroundBlizzard = 1L << 34,
    LightSnow = 1L << 35,
    LightFog = 1L << 36,
    Snow = 1L << 37,
    Overcast = 1L << 38,
    Fog = 1L << 39,
    ReducedVisibilityDueToSmoke = 1L << 40,
    PartlyCloudy = 1L << 41,
    Clear = 1L << 42
}



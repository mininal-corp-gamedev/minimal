using Ambi.Storage;
using Sandbox;

namespace Minimal.ItemUseHandlers;

public sealed class AlcoholUseHandler : IItemUseHandler
{
    private readonly AlcoholDrinkType _drinkType;

    public AlcoholUseHandler( AlcoholDrinkType drinkType )
    {
        _drinkType = drinkType;
    }

    public bool Use( Item item, Player caller )
    {
#if SERVER
        if ( !caller.IsValid() || item is null )
            return false;

        var profile = AlcoholDrinkProfile.FromType( _drinkType );
        return caller.HostTryDrinkAlcohol( item, profile.BloodAmount, profile.EffectDuration, profile.PixelateScale, profile.ShakeStrength );
#else
        return false;
#endif
    }
}

public enum AlcoholDrinkType
{
    Beer,
    Wine
}

public readonly struct AlcoholDrinkProfile
{
    public AlcoholDrinkProfile( float bloodAmount, float effectDuration, float pixelateScale, float shakeStrength )
    {
        BloodAmount = bloodAmount;
        EffectDuration = effectDuration;
        PixelateScale = pixelateScale;
        ShakeStrength = shakeStrength;
    }

    public float BloodAmount { get; }
    public float EffectDuration { get; }
    public float PixelateScale { get; }
    public float ShakeStrength { get; }

    public static AlcoholDrinkProfile FromType( AlcoholDrinkType drinkType )
    {
        return drinkType switch
        {
            AlcoholDrinkType.Wine => new AlcoholDrinkProfile( 0.42f, 25f, 18f, 1.35f ),
            _ => new AlcoholDrinkProfile( 0.24f, 15f, 10f, 0.75f )
        };
    }
}

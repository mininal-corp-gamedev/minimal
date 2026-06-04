using Ambi.Storage;
using Sandbox;
using System;

public sealed partial class Player
{
    public bool HostTryDrinkAlcohol( Item item, float bloodAmount, float effectDuration, float pixelateScale, float shakeStrength )
    {
        if ( !Networking.IsHost )
            return false;
        if ( item is null )
            return false;

        var owner = GameObject.Network.Owner;
        if ( owner is null )
            return false;

        var maxBlood = MathF.Max( 0f, MaxAlcoholBlood );
        var amount = MathF.Max( 0f, bloodAmount );
        if ( maxBlood <= 0f || amount <= 0f )
            return false;

        if ( AlcoholBlood >= maxBlood || AlcoholBlood + amount > maxBlood )
        {
            NotifyInventoryResult(
                owner,
                GameLocalization.Phrase( "notify.alcohol.too_drunk", "You are too drunk to drink more." ),
                false );
            return false;
        }

        AlcoholBlood = Math.Clamp( AlcoholBlood + amount, 0f, maxBlood );
        item.Remove( 1 );

        using ( Rpc.FilterInclude( c => c.SteamId.Value == owner.SteamId.Value ) )
        {
            RpcApplyAlcoholEffect( effectDuration, AlcoholBlood / maxBlood, pixelateScale, shakeStrength );
        }

        return true;
    }

    private void HostUpdateAlcoholBlood()
    {
        if ( !Networking.IsHost )
            return;
        if ( AlcoholBlood <= 0f )
            return;

        var decay = MathF.Max( 0f, AlcoholBloodDecayPerSecond ) * Time.Delta;
        AlcoholBlood = MathF.Max( 0f, AlcoholBlood - decay );
    }
}

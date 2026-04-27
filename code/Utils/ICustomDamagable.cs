namespace Ambi.Utils;

public interface ICustomDamagable
{
    void OnDamage(in DamageInfo damage);
}
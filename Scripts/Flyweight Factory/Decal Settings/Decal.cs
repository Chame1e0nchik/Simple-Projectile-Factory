namespace SimpleProjectileFactory
{
    /// <summary>
    /// Pooled decal. DecalSettings used to borrow the Projectile class for this; Projectile is now a
    /// scripted projectile, so decals get their own flyweight. Lifetime is manual (-1), exactly as
    /// before, until the fade-out in DecalSettings is finished.
    /// </summary>
    public class Decal : TimedFlyweight<DecalSettings>
    {
        protected override float GetDespawnTime()
        {
            return -1f;
        }
    }
}

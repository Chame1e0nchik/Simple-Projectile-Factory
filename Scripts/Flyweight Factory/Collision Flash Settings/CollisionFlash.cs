namespace SimpleProjectileFactory
{
    public class CollisionFlash : TimedFlyweight<CollisionFlashSettings>
    {
        protected override float GetDespawnTime()
        {
            return Settings.despawnTime;
        }

        protected override void OnSpawned()
        {
            // projectile-specific logic (velocity, direction, etc.)
        }

        protected override void OnDespawned()
        {
            // cleanup or something
        }
    }
}
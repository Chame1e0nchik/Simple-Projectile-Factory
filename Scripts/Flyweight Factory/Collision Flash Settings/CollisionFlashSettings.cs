using UnityEngine;

namespace SimpleProjectileFactory
{
    [CreateAssetMenu(fileName = "SimpleProjectileFactory", menuName = "Spells/Collision Effect Settings", order = 1)]
    public class CollisionFlashSettings : FlyweightSettings
    {
        public float despawnTime;

        public override Flyweight Create()
        {
            var go = Instantiate(prefab);
            go.SetActive(false);
            go.name = prefab.name;

            var collisionFlash = go.GetOrAdd<CollisionFlash>();
            collisionFlash.settings = this;

            return collisionFlash;
        }

        public void CreateFlash(Vector3 hitPoint)
        {
            var collisionFlash = flyweightFactory.Spawn(this) as CollisionFlash;
            if (collisionFlash == null)
            {
                Debug.LogError("Failed to spawn collision flash from factory");
                return;
            }

            collisionFlash.transform.SetLocalPositionAndRotation(hitPoint, Quaternion.identity);
        }
    }
}

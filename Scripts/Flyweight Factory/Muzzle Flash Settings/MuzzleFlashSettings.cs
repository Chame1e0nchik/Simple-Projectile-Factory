using UnityEngine;

namespace SimpleProjectileFactory
{
    public enum MuzzleFlashAttachMode
    {
        /// <summary>Placed once at the origin's position/rotation, then stays where it was spawned</summary>
        Static,
        /// <summary>Keeps tracking the origin's position/rotation every frame for as long as it's alive</summary>
        Follow
    }

    [CreateAssetMenu(fileName = "SimpleProjectileFactory", menuName = "Spells/Muzzle Flash Settings", order = 1)]
    public class MuzzleFlashSettings : FlyweightSettings
    {
        public float despawnTime;

        [Tooltip("Static: spawn once at the origin and stay put (a shot puff). " +
            "Follow: track the origin every frame until it despawns (a charge glow on a moving attack point).")]
        public MuzzleFlashAttachMode attachMode = MuzzleFlashAttachMode.Static;

        public override Flyweight Create()
        {
            var go = Instantiate(prefab);
            go.SetActive(false);
            go.name = prefab.name;

            var muzzleFlash = go.GetOrAdd<MuzzleFlash>();
            muzzleFlash.settings = this;

            return muzzleFlash;
        }

        /// <summary>Returns the spawned instance (rather than void) so a caller that needs to force-stop
        /// a Follow flash early — e.g. when a charge is released — has a handle to do it with.
        /// Existing call sites that ignore the return value keep compiling unchanged.</summary>
        public MuzzleFlash CreateMuzzleFlash(Transform origin)
        {
            var muzzleFlash = flyweightFactory.Spawn(this) as MuzzleFlash;
            if (muzzleFlash == null)
            {
                Debug.LogError("Failed to spawn flash from factory");
                return null;
            }

            muzzleFlash.transform.SetPositionAndRotation(origin.position, origin.rotation);

            if (attachMode == MuzzleFlashAttachMode.Follow)
                muzzleFlash.Attach(origin);
            else
                muzzleFlash.Detach();

            return muzzleFlash;
        }
    }
}
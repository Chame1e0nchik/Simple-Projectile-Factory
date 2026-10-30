using UnityEngine;

namespace SimpleProjectileFactory
{
    public class MuzzleFlash : TimedFlyweight<MuzzleFlashSettings>
    {
        private Transform followTarget;

        protected override float GetDespawnTime()
        {
            // Follow flashes (charge glows) live until something explicitly ends the charge and
            // calls ReturnToPoolExternally(), they shouldn't time out on their own mid-charge.
            // Static flashes (shot puffs) keep the normal fixed-duration despawn.
            if (Settings.attachMode == MuzzleFlashAttachMode.Follow)
                return -1f;

            return Settings.despawnTime;
        }

        protected override void OnSpawned()
        {
            // projectile-specific logic (velocity, direction, etc.)
        }

        protected override void OnDespawned()
        {
            // Clear the target here rather than in ReturnToPool/OnDisable directly — OnDespawned
            // already fires on every deactivation path (natural despawn timer AND ReturnToPoolExternally), so this can't be missed
            followTarget = null;
        }

        /// <summary>
        /// Assign a target point, so muzzle flash will follow it during lifetime
        /// </summary>
        /// <param name="target"></param>
        public void Attach(Transform target)
        {
            followTarget = target;
        }

        public void Detach()
        {
            followTarget = null;
        }

        private void Update()
        {
            if (followTarget != null)
            {
                transform.SetPositionAndRotation(followTarget.position, followTarget.rotation);
            }
        }
    }
}
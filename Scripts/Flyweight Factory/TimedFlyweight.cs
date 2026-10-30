using UnityEngine;
using System.Collections;

namespace SimpleProjectileFactory
{
    public abstract class TimedFlyweight<TSettings> : Flyweight where TSettings : FlyweightSettings
    {
        private Coroutine deactivateCoroutine;
        private bool isReturningToPool = false;

        protected TSettings Settings => settings as TSettings;

        /// <summary>
        /// Seconds before this instance auto-despawns.
        /// 0 (or less specifically: exactly &lt;= 0 but not negative) = despawn immediately on spawn.
        /// &lt; 0 = never auto-despawn; the caller must call ReturnToPoolExternally() itself.
        /// </summary>
        protected virtual float GetDespawnTime()
        {
            return 0f;
        }

        protected virtual void OnSpawned() { }
        protected virtual void OnDespawned() { }

        private void OnEnable()
        {
            if (Settings == null)
                return;

            isReturningToPool = false;

            if (deactivateCoroutine != null)
            {
                StopCoroutine(deactivateCoroutine);
                deactivateCoroutine = null;
            }

            OnSpawned();

            float despawnTime = GetDespawnTime();

            if (despawnTime < 0f)
            {
                // caller is responsible for calling ReturnToPoolExternally()
                // when it decides this instance is done
                return;
            }

            if (despawnTime <= 0f)
            {
                ReturnToPool();
                return;
            }

            deactivateCoroutine = StartCoroutine(DeactivateAfterTime(despawnTime));
        }

        private void OnDisable()
        {
            if (deactivateCoroutine != null)
            {
                StopCoroutine(deactivateCoroutine);
                deactivateCoroutine = null;
            }

            OnDespawned();
        }

        private IEnumerator DeactivateAfterTime(float time)
        {
            yield return Helpers.GetWaitForSeconds(time);
            ReturnToPool();
        }

        protected void ReturnToPool()
        {
            if (isReturningToPool) return;

            isReturningToPool = true;
            FlyweightFactory.Instance?.ReturnToPool(this);
        }

        public void ReturnToPoolExternally()
        {
            if (deactivateCoroutine != null)
            {
                StopCoroutine(deactivateCoroutine);
                deactivateCoroutine = null;
            }

            ReturnToPool();
        }
    }
}
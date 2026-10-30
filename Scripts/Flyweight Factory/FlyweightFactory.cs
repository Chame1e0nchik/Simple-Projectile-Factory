using UnityEngine;
using UnityEngine.Pool;
using System.Collections.Generic;

namespace SimpleProjectileFactory
{
    /// <summary>
    /// Pool owner for every flyweight (projectiles, flashes, decals, etc.). One pool per FlyweightSettings asset
    /// </summary>
    public class FlyweightFactory : MonoBehaviour
    {
        private static FlyweightFactory instance;
        private static bool quitting;

        /// <summary>Returns the factory, creating it lazily. Returns null only while the application is quitting.</summary>
        public static FlyweightFactory Instance
        {
            get
            {
                if (instance != null) return instance;
                if (quitting) return null;

                instance = FindFirstObjectByType<FlyweightFactory>();
                if (instance == null)
                {
                    var go = new GameObject("FlyweightFactory");
                    instance = go.AddComponent<FlyweightFactory>(); // Awake registers + DontDestroyOnLoad
                }
                return instance;
            }
        }

        // Static state survives when "Enter Play Mode Options > Reload Domain" is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            quitting = false;
        }

        private readonly Dictionary<FlyweightSettings, IObjectPool<Flyweight>> pools = new();

        // Lazily created factory has no inspector values and UnityEngine.Pool.ObjectPool throws when maxSize <= 0
        [SerializeField] private bool collectionCheck = true;
        [SerializeField] private int defaultCapacity = 10;
        [SerializeField] private int maxSize = 200;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogWarning("[FlyweightFactory] Duplicate factory destroyed.", this);
                Destroy(gameObject);
                return;
            }

            instance = this;
            if (transform.parent == null)
                DontDestroyOnLoad(gameObject);
        }

        private void OnApplicationQuit()
        {
            quitting = true;
        }

        private void OnDestroy()
        {
            pools.Clear();
            if (instance == this) instance = null;
        }

        public Flyweight Spawn(FlyweightSettings settings)
        {
            if (settings == null)
            {
                Debug.LogError("[FlyweightFactory] Spawn called with null settings.");
                return null;
            }

            var pool = GetPoolFor(settings);
            return pool?.Get();
        }

        public void ReturnToPool(Flyweight flyweight)
        {
            if (flyweight == null)
            {
                Debug.LogWarning("[FlyweightFactory] ReturnToPool called with null flyweight.");
                return;
            }

            var pool = GetPoolFor(flyweight.settings);
            pool?.Release(flyweight);
        }

        private IObjectPool<Flyweight> GetPoolFor(FlyweightSettings settings)
        {
            if (settings == null)
            {
                Debug.LogError("[FlyweightFactory] Tried to get pool for null settings.");
                return null;
            }

            if (pools.TryGetValue(settings, out var pool))
                return pool;

            pool = new ObjectPool<Flyweight>(
                settings.Create,
                settings.OnGet,
                settings.OnRelease,
                settings.OnDestroyPoolObject,
                collectionCheck,
                Mathf.Max(0, defaultCapacity),
                Mathf.Max(1, maxSize)
            );

            pools[settings] = pool;
            return pool;
        }
    }
}

using UnityEngine;

namespace SimpleProjectileFactory
{
    /// <summary>
    /// Unified SpellSettings combines both Flyweight Factory settings and spell casting behavior.
    /// This allows creating individual scriptable objects with unique logic for pooling and casting.
    /// </summary>
    public abstract class FlyweightSettings : ScriptableObject
    {
        [Header("Flyweight Factory Settings")]
        public FlyweightType type;
        public GameObject prefab;

        /// <summary>The scene-wide pool owner (created on demand)</summary>
        protected FlyweightFactory flyweightFactory => FlyweightFactory.Instance;

        /// <summary>
        /// Creates a new Flyweight instance with the specified prefab and configuration.
        /// </summary>
        public abstract Flyweight Create();

        /// <summary>
        /// Called when a flyweight is retrieved from the pool.
        /// </summary>
        public virtual void OnGet(Flyweight flyweight)
        {
            flyweight.gameObject.SetActive(true);
        }

        /// <summary>
        /// Called when a flyweight is returned to the pool.
        /// </summary>
        public virtual void OnRelease(Flyweight flyweight)
        {
            flyweight.gameObject.SetActive(false);
        }

        /// <summary>
        /// Called when a pooled object is permanently destroyed.
        /// </summary>
        public virtual void OnDestroyPoolObject(Flyweight flyweight)
        {
            Destroy(flyweight.gameObject);
        }
    }

    public enum FlyweightType
    {
        Instant,
        AboveCharge,
        SideCharge
    }
}

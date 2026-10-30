using UnityEngine;

namespace SimpleProjectileFactory
{
    /// <summary>
    /// Push FlyweightSettings to MonoBheaviour
    /// </summary>
    public abstract class Flyweight : MonoBehaviour
    {
        public FlyweightSettings settings;
    }
}
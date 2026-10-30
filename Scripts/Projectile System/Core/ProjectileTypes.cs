using System;
using UnityEngine;

namespace SimpleProjectileFactory
{
    /// <summary>Why a projectile exploded. Also used as a mask by modules.</summary>
    [Flags]
    public enum ExplodeReason
    {
        None = 0,
        Hit = 1 << 0, // struck something registered as a damage target
        Obstacle = 1 << 1, // struck anything that is not a damage target (walls, floor...)
        Timeout = 1 << 2, // lifetime / range ran out
        Manual = 1 << 3 // Explode() was called from gameplay code
    }

    /// <summary>Everything the caller can tell a projectile at spawn time.</summary>
    public struct LaunchParams
    {
        public Transform Owner;       // optional: colliders under this transform are ignored
        public Vector3 Position;      // world spawn position
        public Vector3 Direction;     // normalized launch direction
        public Vector3 TargetPoint;   // aim point (valid when HasTargetPoint)
        public bool HasTargetPoint;
        public Transform Target;      // optional: homing target
        public Camera Camera;         // optional: orients damage numbers
        public float SpeedOverride;   // > 0 replaces ProjectileSettings.speed
        public int DamageOverride;    // > 0 replaces ProjectileSettings.damage

        public static LaunchParams ToPoint(Transform owner, Vector3 position, Vector3 targetPoint)
        {
            Vector3 d = targetPoint - position;
            return new LaunchParams
            {
                Owner = owner,
                Position = position,
                TargetPoint = targetPoint,
                HasTargetPoint = true,
                Direction = d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward
            };
        }

        public static LaunchParams InDirection(Transform owner, Vector3 position, Vector3 direction)
        {
            return new LaunchParams
            {
                Owner = owner,
                Position = position,
                Direction = direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.forward
            };
        }
    }

    /// <summary>Passed (by ref) to IHitBehaviour. A behaviour sets Continue to let the projectile fly on.</summary>
    public struct HitInfo
    {
        public Collider Collider;
        public Vector3 Point;
        public Vector3 Normal;
        public bool HasTargetId;
        public int TargetId;
        public bool Continue;
    }

    /// <summary>Passed (by ref) to IExplodeBehaviour. LingerTime keeps the projectile alive (stopped) before pooling.</summary>
    public struct ExplodeContext
    {
        public ExplodeReason Reason;
        public Vector3 Point;
        public Vector3 Normal;
        public Collider Collider;
        public bool HasTargetId;
        public int TargetId;
        public float LingerTime;
    }

    /// <summary>
    /// Per-frame speed composition. Modifiers only contribute; the framework resolves
    /// speed = max(0, (base + sum(add)) * product(mul)) so modifiers never overwrite each other.
    /// </summary>
    public struct MotionFrame
    {
        public float BaseSpeed;
        public float DeltaTime;
        private float add;
        private float mul;

        public static MotionFrame Begin(float baseSpeed, float deltaTime)
        {
            return new MotionFrame { BaseSpeed = baseSpeed, DeltaTime = deltaTime, add = 0f, mul = 1f };
        }

        public void AddSpeed(float amount) { add += amount; }
        public void MultiplySpeed(float factor) { mul *= factor; }
        public float Resolve() { return Mathf.Max(0f, (BaseSpeed + add) * mul); }
    }

    /// <summary>Damage composition, same contract as <see cref="MotionFrame"/>.</summary>
    public struct DamageFrame
    {
        public float BaseDamage;
        private float add;
        private float mul;

        public static DamageFrame Begin(float baseDamage)
        {
            return new DamageFrame { BaseDamage = baseDamage, add = 0f, mul = 1f };
        }

        public void AddDamage(float amount) { add += amount; }
        public void MultiplyDamage(float factor) { mul *= factor; }
        public float Resolve() { return Mathf.Max(0f, (BaseDamage + add) * mul); }
    }
}

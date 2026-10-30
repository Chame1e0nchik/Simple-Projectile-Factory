using System;
using UnityEngine;

namespace SimpleProjectileFactory
{
    [Serializable]
    [ProjectileModule("Speed Over Lifetime", "Multiplies speed by a curve over normalized lifetime (0 = spawn, 1 = lifetime end).")]
    public class SpeedOverLifetimeModifier : MovementModifierModule
    {
        public AnimationCurve multiplier = AnimationCurve.Linear(0f, 1f, 1f, 1.5f);

        public override void Modify(Projectile p, ref MotionFrame frame)
        {
            frame.MultiplySpeed(multiplier.Evaluate(p.NormalizedLifetime));
        }

        public override void Validate(ProjectileValidationContext ctx)
        {
            if (multiplier == null || multiplier.length == 0)
                ctx.Error("Curve is empty: speed would drop to zero.");
        }
    }

    [Serializable]
    [ProjectileModule("Acceleration", "Adds acceleration (units/s²) to the speed. Negative values decelerate.")]
    public class AccelerationModifier : MovementModifierModule
    {
        [Tooltip("Units per second squared. Negative = deceleration.")]
        public float acceleration = 10f;
        [Tooltip("Speed never drops below this.")]
        public float minSpeed = 0f;
        [Tooltip("Speed never exceeds this. 0 = unlimited.")]
        public float maxSpeed = 0f;

        private float bonus;

        public override void Initialize(Projectile p) { bonus = 0f; }

        public override void Modify(Projectile p, ref MotionFrame frame)
        {
            bonus += acceleration * frame.DeltaTime;
            float lo = minSpeed - frame.BaseSpeed;
            float hi = maxSpeed > 0f ? maxSpeed - frame.BaseSpeed : float.PositiveInfinity;
            bonus = Mathf.Clamp(bonus, lo, hi);
            frame.AddSpeed(bonus);
        }

        public override void Validate(ProjectileValidationContext ctx)
        {
            if (maxSpeed > 0f && maxSpeed < minSpeed)
                ctx.Warning("Max Speed is lower than Min Speed.");
        }
    }

    [Serializable]
    [ProjectileModule("Speed By Distance", "Multiplies speed by a curve over distance travelled (0 = start, 1 = Distance Range).")]
    public class SpeedByDistanceModifier : MovementModifierModule
    {
        [Min(0.01f)] public float distanceRange = 20f;
        public AnimationCurve multiplier = AnimationCurve.Linear(0f, 1f, 1f, 0.4f);

        public override void Modify(Projectile p, ref MotionFrame frame)
        {
            float t = Mathf.Clamp01(p.DistanceTravelled / Mathf.Max(0.01f, distanceRange));
            frame.MultiplySpeed(multiplier.Evaluate(t));
        }

        public override void Validate(ProjectileValidationContext ctx)
        {
            if (multiplier == null || multiplier.length == 0)
                ctx.Error("Curve is empty: speed would drop to zero.");
        }
    }
}

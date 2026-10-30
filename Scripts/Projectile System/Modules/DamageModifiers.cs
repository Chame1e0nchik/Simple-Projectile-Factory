using System;
using UnityEngine;

namespace SimpleProjectileFactory
{
    [Serializable]
    [ProjectileModule("Damage Over Time", "Moves damage from the base value towards Max Damage while the projectile is alive (charge-up).")]
    public class DamageOverTimeModifier : DamageModifierModule
    {
        public float maxDamage = 50f;
        [Tooltip("Seconds to reach Max Damage. 0 = the projectile's lifetime.")]
        [Min(0f)] public float duration = 0f;
        [Tooltip("0 = base damage, 1 = Max Damage.")]
        public AnimationCurve progress = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        public override void Modify(Projectile p, ref DamageFrame frame)
        {
            float t = duration > 0f ? Mathf.Clamp01(p.Elapsed / duration) : p.NormalizedLifetime;
            frame.AddDamage((maxDamage - frame.BaseDamage) * progress.Evaluate(t));
        }
    }

    [Serializable]
    [ProjectileModule("Damage By Speed", "Multiplies damage by a curve of the current speed (units/s).")]
    public class DamageBySpeedModifier : DamageModifierModule
    {
        public AnimationCurve multiplierBySpeed = AnimationCurve.Linear(0f, 0.5f, 30f, 1.5f);

        public override void Modify(Projectile p, ref DamageFrame frame)
        {
            frame.MultiplyDamage(multiplierBySpeed.Evaluate(p.CurrentSpeed));
        }
    }

    [Serializable]
    [ProjectileModule("Damage Falloff", "Linearly reduces damage between Start and End distance down to Min Multiplier.")]
    public class DamageFalloffModifier : DamageModifierModule
    {
        public float startDistance = 10f;
        public float endDistance = 30f;
        [Range(0f, 1f)] public float minMultiplier = 0.25f;

        public override void Modify(Projectile p, ref DamageFrame frame)
        {
            float t = Mathf.InverseLerp(startDistance, endDistance, p.DistanceTravelled);
            frame.MultiplyDamage(Mathf.Lerp(1f, minMultiplier, t));
        }

        public override void Validate(ProjectileValidationContext ctx)
        {
            if (endDistance <= startDistance)
                ctx.Error("End Distance must be greater than Start Distance.");
        }
    }

    [Serializable]
    [ProjectileModule("Damage Multiplier", "Flat bonus and/or multiplier applied to damage. Can be added several times.")]
    public class DamageMultiplierModifier : DamageModifierModule
    {
        public float multiplier = 1.5f;
        public float flatBonus = 0f;

        public override bool AllowMultiple => true;

        public override void Modify(Projectile p, ref DamageFrame frame)
        {
            frame.AddDamage(flatBonus);
            frame.MultiplyDamage(multiplier);
        }
    }
}

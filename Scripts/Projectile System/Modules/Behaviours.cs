using System;
using UnityEngine;

namespace SimpleProjectileFactory
{
    /// <summary>Applies damage to the collider the projectile exploded on.</summary>
    [Serializable]
    [ProjectileModule("Direct Hit Damage", "Damages the target that was hit when the projectile explodes (plus a damage number).")]
    public class DirectHitDamageBehaviour : BehaviourModule, IExplodeBehaviour
    {
        public override bool DealsDamage => true;

        public void OnExplode(Projectile p, ref ExplodeContext ctx)
        {
            if (!ctx.HasTargetId) return;
            p.ApplyDamage(ctx.TargetId, p.CalculateDamage(), ctx.Point);
        }
    }

    /// <summary>
    /// Radius damage, optionally delayed (the old "second explosion"). While waiting, the projectile
    /// stays alive, stopped at the impact point, then pools itself.
    /// </summary>
    [Serializable]
    [ProjectileModule("Area Damage", "Damages everything in a radius on explosion, optionally after a delay with an extra effect.")]
    public class AreaDamageBehaviour : BehaviourModule, IExplodeBehaviour, ILingerBehaviour
    {
        [Min(0f)] public float radius = 2f;
        [Tooltip("Seconds between the impact and the area damage. 0 = immediately.")]
        [Min(0f)] public float delay = 0f;
        public ExplodeReason triggerOn = ExplodeReason.Hit | ExplodeReason.Obstacle;
        [Tooltip("Played when the area damage goes off (after the delay).")]
        public CollisionFlashSettings delayedEffect;
        public LayerMask damageLayers = ~0;
        [Tooltip("Damage multiplier over distance from the centre (0 = centre, 1 = radius).")]
        public AnimationCurve falloff = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        [Min(1)] public int maxTargets = 32;

        private Collider[] buffer;
        private int[] seen;
        private bool pending;

        public override bool DealsDamage => true;

        public override void Configure(Projectile p)
        {
            int n = Mathf.Max(1, maxTargets);
            buffer = new Collider[n];
            seen = new int[n];
        }

        public override void Initialize(Projectile p) { pending = false; }

        public void OnExplode(Projectile p, ref ExplodeContext ctx)
        {
            if ((triggerOn & ctx.Reason) == 0) return;

            if (delay > 0f)
            {
                pending = true;
                ctx.LingerTime = Mathf.Max(ctx.LingerTime, delay);
            }
            else
            {
                Detonate(p, ctx.Point);
            }
        }

        public void OnLingerComplete(Projectile p, in ExplodeContext ctx)
        {
            if (!pending) return;
            pending = false;
            Detonate(p, ctx.Point);
        }

        private void Detonate(Projectile p, Vector3 center)
        {
            if (delayedEffect != null) delayedEffect.CreateFlash(center);

            int mask = damageLayers.value & ~p.Settings.casterLayer.value;
            int count = Physics.OverlapSphereNonAlloc(center, radius, buffer, mask, p.TriggerInteraction);
            int damage = p.CalculateDamage();
            int seenCount = 0;

            for (int i = 0; i < count; i++)
            {
                Collider c = buffer[i];
                buffer[i] = null; // do not keep references to scene objects while pooled
                if (c == null || p.IsOwnCollider(c)) continue;
                if (!p.TryGetTargetId(c, out int id)) continue;

                // one entity can own several colliders: damage it once
                bool duplicate = false;
                for (int k = 0; k < seenCount; k++)
                    if (seen[k] == id) { duplicate = true; break; }
                if (duplicate) continue;
                seen[seenCount++] = id;

                Vector3 closest = c.bounds.ClosestPoint(center);
                float t = radius > 0f ? Mathf.Clamp01(Vector3.Distance(center, closest) / radius) : 0f;
                p.ApplyDamage(id, Mathf.RoundToInt(damage * falloff.Evaluate(t)), closest);
            }
        }

        public override void Validate(ProjectileValidationContext ctx)
        {
            if (radius <= 0f) ctx.Error("Radius must be greater than 0.");
            if (delay > 0f && delayedEffect == null) ctx.Info("No Delayed Effect assigned: the delayed explosion will be invisible.");
            if (falloff == null || falloff.length == 0) ctx.Error("Falloff curve is empty.");
        }
    }

    /// <summary>Lets the projectile pass through a number of targets, damaging each one.</summary>
    [Serializable]
    [ProjectileModule("Piercing", "Passes through targets, damaging each, before exploding on the final hit.")]
    public class PiercingBehaviour : BehaviourModule, IHitBehaviour
    {
        [Min(1)] public int maxPierces = 2;
        [Tooltip("Damage multiplier applied after each pierced target.")]
        [Range(0f, 1f)] public float damageRetained = 0.8f;
        [Tooltip("Also fly through non-damageable obstacles (counts as a pierce).")]
        public bool pierceObstacles = false;

        private int pierced;
        private int idCount;
        private int[] hitIds;

        public override bool DealsDamage => true;

        public override void Configure(Projectile p) { hitIds = new int[Mathf.Max(1, maxPierces)]; }
        public override void Initialize(Projectile p) { pierced = 0; idCount = 0; }

        public void OnHit(Projectile p, ref HitInfo hit)
        {
            if (hit.HasTargetId)
            {
                // another collider of an entity we already passed through
                for (int i = 0; i < idCount; i++)
                    if (hitIds[i] == hit.TargetId) { hit.Continue = true; return; }
            }

            if (pierced >= maxPierces) return; // final hit, falls through to exploding

            if (hit.HasTargetId)
            {
                p.ApplyDamage(hit.TargetId, p.CalculateDamage(), hit.Point);
                hitIds[idCount++] = hit.TargetId;
                p.DamageMultiplier *= damageRetained;
            }
            else if (!pierceObstacles)
            {
                return;
            }

            pierced++;
            hit.Continue = true;
        }
    }

    /// <summary>Steers a steerable movement towards the launch target (or auto-acquires one).</summary>
    [Serializable]
    [ProjectileModule("Homing", "Steers towards the launch target, or the nearest damageable collider in range. Needs a steerable movement.")]
    public class HomingBehaviour : BehaviourModule, IPreMoveBehaviour
    {
        [Tooltip("Maximum turn speed in degrees per second.")]
        public float turnRate = 180f;
        [Tooltip("Seconds after launch before steering starts.")]
        public float startDelay = 0.1f;
        [Tooltip("Added to the target transform's position when aiming (skipped for auto-acquired colliders).")]
        public Vector3 aimOffset = new Vector3(0f, 1f, 0f);
        [Tooltip("> 0: when there is no target, look for one within this radius.")]
        public float acquireRadius = 0f;
        public LayerMask acquireLayers = ~0;
        public float retargetInterval = 0.25f;

        private ISteerable steerable;
        private Transform target;
        private Collider targetCollider;
        private float nextAcquire;
        private Collider[] buffer;

        public override void Configure(Projectile p)
        {
            steerable = p.Movement as ISteerable;
            if (acquireRadius > 0f) buffer = new Collider[16];
        }

        public override void Initialize(Projectile p)
        {
            target = p.Launch.Target;
            targetCollider = null;
            nextAcquire = 0f;
        }

        public void PreMove(Projectile p, float dt)
        {
            if (steerable == null || p.Elapsed < startDelay) return;

            if (target == null || !target.gameObject.activeInHierarchy)
            {
                target = null;
                targetCollider = null;
                if (acquireRadius > 0f && p.Elapsed >= nextAcquire)
                {
                    nextAcquire = p.Elapsed + retargetInterval;
                    Acquire(p);
                }
                if (target == null) return;
            }

            Vector3 aim = targetCollider != null ? targetCollider.bounds.center : target.position + aimOffset;
            Vector3 to = aim - p.Position;
            if (to.sqrMagnitude < 1e-6f) return;
            steerable.Steer(to.normalized, turnRate * Mathf.Deg2Rad * dt);
        }

        private void Acquire(Projectile p)
        {
            int mask = acquireLayers.value & ~p.Settings.casterLayer.value;
            int count = Physics.OverlapSphereNonAlloc(p.Position, acquireRadius, buffer, mask, p.TriggerInteraction);
            float best = float.MaxValue;
            Collider bestCollider = null;
            for (int i = 0; i < count; i++)
            {
                Collider c = buffer[i];
                buffer[i] = null;
                if (c == null || p.IsOwnCollider(c) || !p.TryGetTargetId(c, out _)) continue;
                float d = (c.bounds.center - p.Position).sqrMagnitude;
                if (d < best) { best = d; bestCollider = c; }
            }
            if (bestCollider != null)
            {
                targetCollider = bestCollider;
                target = bestCollider.transform;
            }
        }

        public override void Validate(ProjectileValidationContext ctx)
        {
            if (!(ctx.Movement is ISteerable))
                ctx.Error("Homing needs a steerable movement (e.g. Direct Shot). The selected movement cannot be steered.");
            if (turnRate <= 0f) ctx.Warning("Turn Rate is 0: the projectile will never turn.");
        }
    }

    /// <summary>Ends the projectile (as a timeout) once it has flown a given distance.</summary>
    [Serializable]
    [ProjectileModule("Max Distance", "Explodes (as a timeout) once the projectile has travelled this far.")]
    public class MaxDistanceBehaviour : BehaviourModule, IPostMoveBehaviour
    {
        [Min(0.1f)] public float maxDistance = 30f;

        public void PostMove(Projectile p, float dt)
        {
            if (p.DistanceTravelled >= maxDistance)
                p.Explode(ExplodeReason.Timeout);
        }
    }

    /// <summary>Extra collision/impact effect with its own trigger mask.</summary>
    [Serializable]
    [ProjectileModule("Impact Effect", "Spawns an additional collision effect when the projectile explodes for the selected reasons.")]
    public class ImpactEffectBehaviour : BehaviourModule, IExplodeBehaviour
    {
        public CollisionFlashSettings effect;
        public ExplodeReason triggerOn = ExplodeReason.Hit | ExplodeReason.Obstacle | ExplodeReason.Timeout;
        public override bool AllowMultiple => true;

        public void OnExplode(Projectile p, ref ExplodeContext ctx)
        {
            if (effect != null && (triggerOn & ctx.Reason) != 0)
                effect.CreateFlash(ctx.Point);
        }

        public override void Validate(ProjectileValidationContext ctx)
        {
            if (effect == null) ctx.Warning("No effect assigned.");
        }
    }
}

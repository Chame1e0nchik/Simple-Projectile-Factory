using System;
using UnityEngine;

namespace SimpleProjectileFactory
{
    public enum ModuleCategory
    {
        Movement, // exactly one
        MovementModifier, // any number
        DamageModifier, // any number
        Behaviour // any number
    }

    /// <summary>Display metadata used by the Projectile Wizard.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ProjectileModuleAttribute : Attribute
    {
        public string DisplayName { get; }
        public string Description { get; }

        public ProjectileModuleAttribute(string displayName, string description = "")
        {
            DisplayName = displayName;
            Description = description;
        }
    }

    /// <summary>Capability implemented by movement modules whose heading can be bent (homing needs it).</summary>
    public interface ISteerable
    {
        void Steer(Vector3 desiredDirection, float maxRadians);
    }

    // Optional hooks for BehaviourModules. A behaviour implements only what it needs;
    // the projectile partitions behaviours into per-hook arrays once, in Configure().
    public interface IPreMoveBehaviour { void PreMove(Projectile p, float dt); }
    public interface IPostMoveBehaviour { void PostMove(Projectile p, float dt); }
    public interface IHitBehaviour { void OnHit(Projectile p, ref HitInfo hit); }
    public interface IExplodeBehaviour { void OnExplode(Projectile p, ref ExplodeContext ctx); }
    public interface ILingerBehaviour { void OnLingerComplete(Projectile p, in ExplodeContext ctx); }

    /// <summary>
    /// Base of every module. Modules are plain serializable classes stored inside a ProjectileSettings asset
    /// (the asset holds configuration). Each pooled projectile receives its own clone once, so any
    /// state a module keeps (timers, counters) is per-projectile and never touches the asset.
    ///
    /// Rule for runtime state: keep it in value-type fields, reset it in Initialize(), allocate
    /// buffers in Configure() (the clone is already private to the projectile at that point).
    /// </summary>
    [Serializable]
    public abstract class ProjectileModule
    {
        /// <summary>Disabled modules are skipped by the builder.</summary>
        public bool enabled = true;

        public abstract ModuleCategory Category { get; }

        /// <summary>Whether the same module type may appear more than once on one projectile.</summary>
        public virtual bool AllowMultiple => false;

        /// <summary>Used by validation to warn about projectiles that never deal damage.</summary>
        public virtual bool DealsDamage => false;

        /// <summary>Once per pooled instance.</summary>
        public virtual void Configure(Projectile projectile) { }

        /// <summary>Every spawn. Reset ALL runtime state here.</summary>
        public virtual void Initialize(Projectile projectile) { }

        /// <summary>Editor/build-time checks: missing references, incompatible combinations.</summary>
        public virtual void Validate(ProjectileValidationContext ctx) { }

        internal ProjectileModule CloneForRuntime()
        {
            return (ProjectileModule)MemberwiseClone();
        }
    }

    /// <summary>A. Exactly one per projectile: defines the primary trajectory.</summary>
    [Serializable]
    public abstract class MovementModule : ProjectileModule
    {
        public sealed override ModuleCategory Category => ModuleCategory.Movement;

        /// <summary>Current unit direction of travel. Valid after Initialize().</summary>
        public Vector3 Heading { get; protected set; } = Vector3.forward;

        /// <summary>Displacement for this frame at the already-resolved speed. The projectile sweeps it for collisions.</summary>
        public abstract Vector3 Step(Projectile p, float speed, float dt);

        /// <summary>Called once when the projectile explodes (stop internal simulation if any).</summary>
        public virtual void Stop(Projectile p) { }
    }

    /// <summary>B. Contribute to this frame's speed (see MotionFrame). Execution: list order, before the move.</summary>
    [Serializable]
    public abstract class MovementModifierModule : ProjectileModule
    {
        public sealed override ModuleCategory Category => ModuleCategory.MovementModifier;
        public abstract void Modify(Projectile p, ref MotionFrame frame);
    }

    /// <summary>C. Contribute to damage when it is evaluated (impact time). No per-frame cost.</summary>
    [Serializable]
    public abstract class DamageModifierModule : ProjectileModule
    {
        public sealed override ModuleCategory Category => ModuleCategory.DamageModifier;
        public abstract void Modify(Projectile p, ref DamageFrame frame);
    }

    /// <summary>D. Anything else. Implement the I...Behaviour interfaces you need.</summary>
    [Serializable]
    public abstract class BehaviourModule : ProjectileModule
    {
        public sealed override ModuleCategory Category => ModuleCategory.Behaviour;
    }
}

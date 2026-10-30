using System.Collections.Generic;
using DamageNumbersPro;
using UnityEngine;

namespace SimpleProjectileFactory
{
    /// <summary>
    /// Reusable projectile configuration, pool key. Base parameters live in plain fields; behaviour is a
    /// set of modules (exactly one movement, any number of movement modifiers, damage modifiers and behaviours).
    /// </summary>
    [CreateAssetMenu(fileName = "ProjectileSettings", menuName = "Spells/Projectile Settings", order = 1)]
    public class ProjectileSettings : FlyweightSettings
    {
        [Header("Projectile Settings")]
        public int damage;
        public float speed = 10f;
        [Tooltip("Seconds before the projectile times out (explodes with reason Timeout). 0 = never.")]
        public float lifetime = 5f;

        [Header("Collision")]
        [Tooltip("Layers of the caster. Never hit by this projectile.")]
        public LayerMask casterLayer;
        public LayerMask hitLayers = ~0;
        [Tooltip("Collide with trigger colliders as well.")]
        public bool hitTriggers = true;
        [Tooltip("0 = ray sweep, > 0 = sphere sweep with this radius.")]
        [Min(0f)] public float collisionRadius = 0f;

        [Header("Visuals")]
        [Tooltip("Rotate the projectile to face its direction of travel.")]
        public bool alignToMovement = true;
        public MuzzleFlashSettings muzzleFlash;
        public CollisionFlashSettings explosionEffect;
        [Tooltip("Explosion reasons that play the explosion effect.")]
        public ExplodeReason explosionEffectOn = ExplodeReason.Hit | ExplodeReason.Obstacle;
        public DamageNumberMesh numberPrefab;

        [HideInInspector] public int maxDamage;
        [HideInInspector] public float explosionRadius = 2f;
        [HideInInspector] public float spawnAfterCollisionTime = 3f;
        [HideInInspector] public CollisionFlashSettings additionalExplosionEffect;

        [SerializeReference] private MovementModule movement;
        [SerializeReference] private List<MovementModifierModule> movementModifiers = new List<MovementModifierModule>();
        [SerializeReference] private List<DamageModifierModule> damageModifiers = new List<DamageModifierModule>();
        [SerializeReference] private List<BehaviourModule> behaviours = new List<BehaviourModule>();

        // Property names for editor tooling
        public const string MovementField = nameof(movement);
        public const string MovementModifiersField = nameof(movementModifiers);
        public const string DamageModifiersField = nameof(damageModifiers);
        public const string BehavioursField = nameof(behaviours);

        public MovementModule Movement => movement;
        public IReadOnlyList<MovementModifierModule> MovementModifiers => movementModifiers;
        public IReadOnlyList<DamageModifierModule> DamageModifiers => damageModifiers;
        public IReadOnlyList<BehaviourModule> Behaviours => behaviours;

        [System.NonSerialized] private ProjectileProfile profile;
        [System.NonSerialized] private bool profileFailed;

        /// <summary>Validated, execution-ordered modules (built once, cached). Null when the configuration is invalid.</summary>
        public ProjectileProfile Profile
        {
            get
            {
                if (profile != null) return profile;
                if (profileFailed) return null;

                if (!ProjectileBuilder.FromSettings(this).TryBuild(out profile, out ValidationReport report))
                {
                    profileFailed = true;
                    Debug.LogError($"[ProjectileSettings] '{name}' is invalid and cannot be spawned:\n{report}", this);
                    return null;
                }
                return profile;
            }
        }

        private void OnValidate()
        {
            profile = null;
            profileFailed = false;
        }

        /// <summary>Replaces the module lists with the builder's contents (code-first configuration, presets, tests).</summary>
        public void SetModules(ProjectileBuilder builder)
        {
            movement = builder.Movement;
            movementModifiers = new List<MovementModifierModule>(builder.MovementModifiers);
            damageModifiers = new List<DamageModifierModule>(builder.DamageModifiers);
            behaviours = new List<BehaviourModule>(builder.Behaviours);
            profile = null;
            profileFailed = false;
        }

        /// <summary>Everything wrong with this asset: modules plus projectile-level checks.</summary>
        public ValidationReport Validate()
        {
            ValidationReport report = ProjectileBuilder.FromSettings(this).Validate();
            if (prefab == null) report.Error("No prefab assigned.");
            if (speed <= 0f) report.Warning("Speed is 0 or negative: the projectile will not move.");
            if (lifetime <= 0f) report.Warning("Lifetime is 0: the projectile only ends on impact, a behaviour (e.g. Max Distance) or a manual Explode().");
            if (explosionEffect == null) report.Info("No explosion effect assigned.");
            if (numberPrefab == null) report.Info("No damage number prefab assigned: no floating numbers will be shown.");
            return report;
        }

        #region Instantiate prefab

        /// <summary>Pool factory callback: runs once per instance, so this is where Configure happens.</summary>
        public override Flyweight Create()
        {
            var go = Instantiate(prefab);
            go.SetActive(false);
            go.name = prefab.name;

            var projectile = go.GetOrAdd<Projectile>();
            projectile.settings = this;
            projectile.Configure(this);

            return projectile;
        }

        #endregion


        #region Spawn

        /// <summary>Gets a projectile from the pool and launches it. Returns null (and logs) on failure.</summary>
        public Projectile Spawn(in LaunchParams launch)
        {
            if (Profile == null) return null; // invalid configuration, already logged

            FlyweightFactory factory = flyweightFactory;
            if (factory == null)
            {
                Debug.LogError("[ProjectileSettings] No FlyweightFactory available.");
                return null;
            }

            var projectile = factory.Spawn(this) as Projectile;
            if (projectile == null)
            {
                Debug.LogError("Failed to spawn projectile from factory");
                return null;
            }

            if (!projectile.Initialize(in launch))
            {
                factory.ReturnToPool(projectile);
                return null;
            }
            return projectile;
        }

        // Drop-in replacements for the previous API. needs camera so the damage number knows what camera to face
        public Projectile CreateSpell(Transform origin, Vector3 targetPoint, Camera camera)
        {
            LaunchParams launch = LaunchParams.ToPoint(null, origin.position, targetPoint);
            launch.Camera = camera;
            return Spawn(in launch);
        }

        public Projectile CreateSpell(Transform origin, Vector3 targetPoint)
        {
            return CreateSpell(origin, targetPoint, null);
        }
        
        #endregion
    }
}

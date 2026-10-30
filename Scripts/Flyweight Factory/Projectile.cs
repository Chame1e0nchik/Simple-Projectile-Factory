using System;
using System.Collections.Generic;
using DamageNumbersPro;
using HealthSystem;
using UnityEngine;

namespace SimpleProjectileFactory
{
    /// <summary>
    /// Pooled projectile with an explicit, Unity-callback-independent lifecycle:
    ///
    /// Configure()   once per pooled instance   (called by ProjectileSettings.Create)
    /// Initialize()  every spawn                (called by ProjectileSettings.Spawn)
    /// Update()      while active               (pipeline below)
    /// Explode()     once per activation        (hit / timeout / manual) -> pool
    ///
    /// Per-frame pipeline (fixed order, modules inside a stage run in list order):
    /// 1 timeout check
    /// 2 movement modifiers      -> resolved speed
    /// 3 pre-move behaviours     (homing steering...)
    /// 4 movement.Step + sweep   -> hit behaviours -> explode or continue
    /// 5 post-move behaviours    (range limits...)
    /// Explode: movement.Stop -> core explosion effect -> explode behaviours -> pool (or linger, then
    /// linger behaviours, then pool).
    ///
    /// OnEnable/OnDisable intentionally contain no gameplay logic.
    /// </summary>
    [DisallowMultipleComponent]
    public class Projectile : Flyweight
    {
        private enum State : byte { Pooled, Active, Lingering }

        // Shared scratch buffer for sweeps (main thread only)
        private static readonly RaycastHit[] SweepBuffer = new RaycastHit[32];

        private State state = State.Pooled;
        private bool configured;
        private ProjectileSettings config;

        // Runtime module instances (private clones, created once in Configure)
        private MovementModule movement;
        private MovementModifierModule[] movementModifiers;
        private DamageModifierModule[] damageModifiers;
        private BehaviourModule[] behaviours;
        private IPreMoveBehaviour[] preMove;
        private IPostMoveBehaviour[] postMove;
        private IHitBehaviour[] onHit;
        private IExplodeBehaviour[] onExplode;
        private ILingerBehaviour[] onLinger;

        // Cached config
        private int hitMask;
        private QueryTriggerInteraction triggerInteraction;

        // Runtime state, reset in Initialize
        private LaunchParams launch;
        private Vector3 position;
        private Vector3 lastHeading;
        private float elapsed;
        private float distanceTravelled;
        private float baseSpeed;
        private float currentSpeed;
        private float baseDamage;
        private float lifetime;
        private float lingerTimer;
        private ExplodeContext lastExplosion;



        public bool IsConfigured => configured;
        public bool IsActive => state == State.Active;
        public ProjectileSettings Settings => config;
        public LaunchParams Launch => launch;
        public Transform Owner => launch.Owner;
        public Transform Target => launch.Target;
        public MovementModule Movement => movement;
        public Vector3 Position => position;
        public Vector3 Heading => movement != null ? movement.Heading : transform.forward;
        public float Elapsed => elapsed;
        public float NormalizedLifetime => lifetime > 0f ? Mathf.Clamp01(elapsed / lifetime) : 0f;
        public float DistanceTravelled => distanceTravelled;
        public float BaseSpeed => baseSpeed;
        public float CurrentSpeed => currentSpeed;
        public float BaseDamage => baseDamage;
        public QueryTriggerInteraction TriggerInteraction => triggerInteraction;

        /// <summary>Extra damage factor for behaviours (e.g. piercing falloff). Reset to 1 on every spawn.</summary>
        public float DamageMultiplier { get; set; } = 1f;


        #region Configure

        /// <summary>Called once per instance. Clones the modules, caches config, prepares components.</summary>
        public void Configure(ProjectileSettings projectileSettings)
        {
            if (configured) return;
            if (projectileSettings == null)
            {
                Debug.LogError("[Projectile] Configure called without settings.", this);
                return;
            }

            ProjectileProfile profile = projectileSettings.Profile;
            if (profile == null) return; // reason already logged by ProjectileSettings

            config = projectileSettings;
            settings = projectileSettings;

            movement = (MovementModule)profile.Movement.CloneForRuntime();
            movementModifiers = CloneAll(profile.MovementModifiers);
            damageModifiers = CloneAll(profile.DamageModifiers);
            behaviours = CloneAll(profile.Behaviours);

            PartitionBehaviours();

            hitMask = config.hitLayers.value & ~config.casterLayer.value;
            triggerInteraction = config.hitTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;

            // Movement is script-driven (sweeps), never physics-driven.
            if (TryGetComponent(out Rigidbody rb)) rb.isKinematic = true;

            // Module Configure runs after all fields above exist so modules can inspect the projectile.
            movement.Configure(this);
            for (int i = 0; i < movementModifiers.Length; i++) movementModifiers[i].Configure(this);
            for (int i = 0; i < damageModifiers.Length; i++) damageModifiers[i].Configure(this);
            for (int i = 0; i < behaviours.Length; i++) behaviours[i].Configure(this);

            configured = true;
            state = State.Pooled;
        }

        private static T[] CloneAll<T>(T[] source) where T : ProjectileModule
        {
            var result = new T[source.Length];
            for (int i = 0; i < source.Length; i++) result[i] = (T)source[i].CloneForRuntime();
            return result;
        }

        private void PartitionBehaviours()
        {
            var pre = new List<IPreMoveBehaviour>();
            var post = new List<IPostMoveBehaviour>();
            var hit = new List<IHitBehaviour>();
            var exp = new List<IExplodeBehaviour>();
            var ling = new List<ILingerBehaviour>();

            for (int i = 0; i < behaviours.Length; i++)
            {
                object b = behaviours[i];
                if (b is IPreMoveBehaviour a1) pre.Add(a1);
                if (b is IPostMoveBehaviour a2) post.Add(a2);
                if (b is IHitBehaviour a3) hit.Add(a3);
                if (b is IExplodeBehaviour a4) exp.Add(a4);
                if (b is ILingerBehaviour a5) ling.Add(a5);
            }

            preMove = pre.ToArray();
            postMove = post.ToArray();
            onHit = hit.ToArray();
            onExplode = exp.ToArray();
            onLinger = ling.ToArray();
        }

        #endregion


        #region Initialize

        /// <summary>Every spawn, similar to OnEnable(). Resets all runtime state and starts the projectile. Returns false if it cannot start.</summary>
        public bool Initialize(in LaunchParams launchParams)
        {
            if (!configured)
            {
                Debug.LogError("[Projectile] Initialize called before Configure (invalid settings?).", this);
                return false;
            }

            // reset everything a previous activation could have left behind
            elapsed = 0f;
            distanceTravelled = 0f;
            currentSpeed = 0f;
            lingerTimer = 0f;
            DamageMultiplier = 1f;
            lastExplosion = default;

            launch = launchParams;
            baseSpeed = launch.SpeedOverride > 0f ? launch.SpeedOverride : config.speed;
            baseDamage = launch.DamageOverride > 0 ? launch.DamageOverride : config.damage;
            lifetime = config.lifetime;
            currentSpeed = baseSpeed;

            SetPosition(launch.Position);

            // modules, in category order
            movement.Initialize(this);
            for (int i = 0; i < movementModifiers.Length; i++) movementModifiers[i].Initialize(this);
            for (int i = 0; i < damageModifiers.Length; i++) damageModifiers[i].Initialize(this);
            for (int i = 0; i < behaviours.Length; i++) behaviours[i].Initialize(this);

            lastHeading = movement.Heading;
            if (config.alignToMovement && lastHeading.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(lastHeading);

            state = State.Active;

            if (config.muzzleFlash != null)
                config.muzzleFlash.CreateMuzzleFlash(transform);

            return true;
        }

        #endregion


        #region Update Lifecycle

        private void Update()
        {
            if (state == State.Pooled) return;

            try
            {
                if (state == State.Active) Tick(Time.deltaTime);
                else TickLinger(Time.deltaTime);
            }
            catch (Exception e)
            {
                // A faulty module must never leave a projectile alive forever
                Debug.LogException(e, this);
                Despawn();
            }
        }

        private void Tick(float dt)
        {
            // 1. timeout
            if (lifetime > 0f && elapsed >= lifetime)
            {
                Explode(ExplodeReason.Timeout);
                return;
            }

            // 2. movement modifiers -> speed
            var frame = MotionFrame.Begin(baseSpeed, dt);
            for (int i = 0; i < movementModifiers.Length; i++)
                movementModifiers[i].Modify(this, ref frame);
            currentSpeed = frame.Resolve();

            // 3. pre-move behaviours
            for (int i = 0; i < preMove.Length; i++)
            {
                preMove[i].PreMove(this, dt);
                if (state != State.Active) return;
            }

            // 4. move + collide
            Vector3 displacement = movement.Step(this, currentSpeed, dt);
            if (!MoveAndCollide(displacement)) return;

            if (config.alignToMovement)
            {
                Vector3 h = movement.Heading;
                if ((h - lastHeading).sqrMagnitude > 1e-6f && h.sqrMagnitude > 1e-6f)
                {
                    lastHeading = h;
                    transform.rotation = Quaternion.LookRotation(h);
                }
            }

            // 5. post-move behaviours
            for (int i = 0; i < postMove.Length; i++)
            {
                postMove[i].PostMove(this, dt);
                if (state != State.Active) return;
            }

            elapsed += dt;
        }

        private void TickLinger(float dt)
        {
            lingerTimer -= dt;
            if (lingerTimer > 0f) return;

            try
            {
                for (int i = 0; i < onLinger.Length; i++)
                    onLinger[i].OnLingerComplete(this, in lastExplosion);
            }
            finally
            {
                Despawn();
            }
        }

        #endregion


        #region Collision check

        /// <summary>Applies the displacement as a sweep. Returns false when the projectile is no longer active.</summary>
        private bool MoveAndCollide(Vector3 displacement)
        {
            float distance = displacement.magnitude;
            if (distance < 1e-6f) return true;

            Vector3 dir = displacement / distance;
            Vector3 start = position;
            Vector3 cursor = start;

            int count = config.collisionRadius > 0f
                ? Physics.SphereCastNonAlloc(start, config.collisionRadius, dir, SweepBuffer, distance, hitMask, triggerInteraction)
                : Physics.RaycastNonAlloc(start, dir, SweepBuffer, distance, hitMask, triggerInteraction);

            uint consumed = 0;
            while (count > 0)
            {
                // nearest hit not handled yet (the buffer is unsorted)
                int best = -1;
                float bestDistance = float.MaxValue;
                for (int i = 0; i < count; i++)
                {
                    if ((consumed & (1u << i)) != 0) continue;
                    if (IsOwnCollider(SweepBuffer[i].collider)) { consumed |= 1u << i; continue; }
                    if (SweepBuffer[i].distance < bestDistance)
                    {
                        best = i;
                        bestDistance = SweepBuffer[i].distance;
                    }
                }
                if (best < 0) break;
                consumed |= 1u << best;

                RaycastHit h = SweepBuffer[best]; // copy, callbacks must not see a shared buffer change under them
                Vector3 center = start + dir * h.distance;
                Vector3 point = (h.distance <= 0f && h.point == Vector3.zero) ? center : h.point;

                distanceTravelled += Vector3.Distance(cursor, center);
                cursor = center;
                SetPosition(center);

                var info = new HitInfo
                {
                    Collider = h.collider,
                    Point = point,
                    Normal = h.normal,
                    HasTargetId = TryGetTargetId(h.collider, out int id),
                    TargetId = id
                };

                for (int i = 0; i < onHit.Length; i++)
                    onHit[i].OnHit(this, ref info);

                if (state != State.Active) return false;
                if (info.Continue) continue;

                var ctx = new ExplodeContext
                {
                    Reason = info.HasTargetId ? ExplodeReason.Hit : ExplodeReason.Obstacle,
                    Point = point,
                    Normal = h.normal,
                    Collider = h.collider,
                    HasTargetId = info.HasTargetId,
                    TargetId = info.TargetId
                };
                ExplodeInternal(ref ctx);
                return false;
            }

            Vector3 end = start + displacement;
            distanceTravelled += Vector3.Distance(cursor, end);
            SetPosition(end);
            return true;
        }

        public bool IsOwnCollider(Collider c)
        {
            if (c == null) return false;
            Transform t = c.transform;
            if (t.IsChildOf(transform)) return true;
            return launch.Owner != null && t.IsChildOf(launch.Owner);
        }

        private void SetPosition(Vector3 p)
        {
            position = p;
            transform.position = p;
        }

        #endregion


        #region End Lifecycle

        /// <summary>
        /// Logically ends the projectile at its current position. Safe to call any number of times and from
        /// any module or gameplay code: only the first call per activation does anything.
        /// </summary>
        public void Explode(ExplodeReason reason = ExplodeReason.Manual)
        {
            if (state != State.Active) return;
            var ctx = new ExplodeContext
            {
                Reason = reason,
                Point = position,
                Normal = -Heading
            };
            ExplodeInternal(ref ctx);
        }

        private void ExplodeInternal(ref ExplodeContext ctx)
        {
            if (state != State.Active) return; // exactly once per activation
            state = State.Lingering; // closes the gate before any callback runs
            lingerTimer = 0f;

            try
            {
                movement.Stop(this);

                if (config.explosionEffect != null && (config.explosionEffectOn & ctx.Reason) != 0)
                    config.explosionEffect.CreateFlash(ctx.Point);

                for (int i = 0; i < onExplode.Length; i++)
                    onExplode[i].OnExplode(this, ref ctx);
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                ctx.LingerTime = 0f;
            }

            lastExplosion = ctx;
            if (ctx.LingerTime > 0f) lingerTimer = ctx.LingerTime;
            else Despawn();
        }

        /// <summary>Returns the projectile to the pool without any explosion logic. Idempotent.</summary>
        public void Despawn()
        {
            if (state == State.Pooled) return; // never release twice
            state = State.Pooled;

            // drop references so a pooled projectile does not keep scene objects alive
            launch = default;
            lastExplosion = default;

            FlyweightFactory factory = FlyweightFactory.Instance;
            if (factory != null) factory.ReturnToPool(this);
            else gameObject.SetActive(false);
        }

        [Obsolete("Use Despawn() for a silent release or Explode() for a full explosion.")]
        public void ReturnToPoolExternally()
        {
            Despawn();
        }

        // Pool callbacks only. No gameplay here: Initialize/Explode are driven explicitly.
        private void OnDisable()
        {
            // Deactivated behind our back (scene unload, someone called SetActive(false)):
            // make sure no stale "active" state survives into the next use.
            state = State.Pooled;
        }

        #endregion


        #region Applying damage

        /// <summary>Final damage at this moment: (base + sum(add)) * product(mul) * DamageMultiplier.</summary>
        public int CalculateDamage()
        {
            var frame = DamageFrame.Begin(baseDamage);
            for (int i = 0; i < damageModifiers.Length; i++)
            {
                damageModifiers[i].Modify(this, ref frame);
            }
            return Mathf.RoundToInt(frame.Resolve() * DamageMultiplier);
        }

        public bool TryGetTargetId(Collider collider, out int id)
        {
            return ColliderRegistry.TryGetID(collider, out id);
        }

        /// <summary>The single place that talks to the damage system and the floating numbers.</summary>
        public void ApplyDamage(int targetId, int amount, Vector3 numberPosition)
        {
            DamageSystem.Instance.AddDamage(targetId, amount);

            DamageNumberMesh numberPrefab = config.numberPrefab;
            if (numberPrefab == null) return;

            DamageNumber damageNumber = numberPrefab.Spawn(numberPosition, amount);
            Camera cam = launch.Camera;
            if (damageNumber != null && cam != null)
            {
                damageNumber.cameraOverride = cam.transform;
                damageNumber.fovCamera = cam;
                damageNumber.orthographicCamera = cam;
            }
        }

        #endregion
    }
}

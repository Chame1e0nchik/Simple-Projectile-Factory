using System;
using System.Collections.Generic;

namespace SimpleProjectileFactory
{
    public enum ValidationSeverity { Info, Warning, Error }

    public sealed class ValidationReport
    {
        public struct Entry
        {
            public ValidationSeverity Severity;
            public string Message;
            public Type ModuleType; // null = projectile-level message
        }

        private readonly List<Entry> entries = new List<Entry>();
        public IReadOnlyList<Entry> Entries => entries;
        public bool HasErrors { get; private set; }

        public void Add(ValidationSeverity severity, string message, Type moduleType = null)
        {
            if (severity == ValidationSeverity.Error) HasErrors = true;
            entries.Add(new Entry { Severity = severity, Message = message, ModuleType = moduleType });
        }

        public void Error(string message, Type moduleType = null) { Add(ValidationSeverity.Error, message, moduleType); }
        public void Warning(string message, Type moduleType = null) { Add(ValidationSeverity.Warning, message, moduleType); }
        public void Info(string message, Type moduleType = null) { Add(ValidationSeverity.Info, message, moduleType); }

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var e in entries)
            {
                sb.Append('[').Append(e.Severity).Append("] ").AppendLine(e.Message);
            }
            return sb.ToString();
        }
    }

    /// <summary>What a module sees while validating itself.</summary>
    public sealed class ProjectileValidationContext
    {
        private readonly ValidationReport report;
        private readonly List<ProjectileModule> modules;
        internal Type Current;

        public MovementModule Movement { get; }

        internal ProjectileValidationContext(ValidationReport report, MovementModule movement, List<ProjectileModule> modules)
        {
            this.report = report;
            this.modules = modules;
            Movement = movement;
        }

        public bool Has<T>() where T : class
        {
            for (int i = 0; i < modules.Count; i++)
                if (modules[i] is T) return true;
            return false;
        }

        public void Error(string message) { report.Error(message, Current); }
        public void Warning(string message) { report.Warning(message, Current); }
        public void Info(string message) { report.Info(message, Current); }
    }

    /// <summary>
    /// The immutable, validated result of the builder: modules in execution order, grouped by category.
    /// It is the template; every pooled projectile clones it once in Configure().
    /// </summary>
    public sealed class ProjectileProfile
    {
        public MovementModule Movement { get; }
        public MovementModifierModule[] MovementModifiers { get; }
        public DamageModifierModule[] DamageModifiers { get; }
        public BehaviourModule[] Behaviours { get; }

        internal ProjectileProfile(MovementModule movement, MovementModifierModule[] movementModifiers,
            DamageModifierModule[] damageModifiers, BehaviourModule[] behaviours)
        {
            Movement = movement;
            MovementModifiers = movementModifiers;
            DamageModifiers = damageModifiers;
            Behaviours = behaviours;
        }
    }

    /// <summary>
    /// Fluent builder. Order of Add*** calls = execution order inside a category.
    /// Exactly-one-movement is enforced by construction (WithMovement can be called once)
    /// and re-checked by Validate().
    ///
    ///   var profile = new ProjectileBuilder()
    ///       .WithMovement(new DirectShotMovement())
    ///       .AddMovementModifier(new AccelerationModifier { acceleration = 20f })
    ///       .AddBehaviour(new DirectHitDamageBehaviour())
    ///       .Build();
    /// </summary>
    public sealed class ProjectileBuilder
    {
        private MovementModule movement;
        private readonly List<MovementModifierModule> movementModifiers = new List<MovementModifierModule>();
        private readonly List<DamageModifierModule> damageModifiers = new List<DamageModifierModule>();
        private readonly List<BehaviourModule> behaviours = new List<BehaviourModule>();

        public MovementModule Movement => movement;
        public IReadOnlyList<MovementModifierModule> MovementModifiers => movementModifiers;
        public IReadOnlyList<DamageModifierModule> DamageModifiers => damageModifiers;
        public IReadOnlyList<BehaviourModule> Behaviours => behaviours;

        public static ProjectileBuilder FromSettings(ProjectileSettings settings)
        {
            var b = new ProjectileBuilder();
            b.movement = settings.Movement;
            b.movementModifiers.AddRange(settings.MovementModifiers);
            b.damageModifiers.AddRange(settings.DamageModifiers);
            b.behaviours.AddRange(settings.Behaviours);
            return b;
        }

        public ProjectileBuilder WithMovement(MovementModule module)
        {
            if (movement != null)
                throw new InvalidOperationException($"A projectile has exactly one movement module (already '{movement.GetType().Name}').");
            movement = module;
            return this;
        }

        public ProjectileBuilder AddMovementModifier(MovementModifierModule module)
        {
            movementModifiers.Add(module);
            return this;
        }
        public ProjectileBuilder AddDamageModifier(DamageModifierModule module)
        {
            damageModifiers.Add(module); return this;
        }
        public ProjectileBuilder AddBehaviour(BehaviourModule module)
        {
            behaviours.Add(module); return this;
        }

        /// <summary>Full validation. Allocates; call from the editor or once per settings asset.</summary>
        public ValidationReport Validate()
        {
            var report = new ValidationReport();
            var active = new List<ProjectileModule>(8);

            if (movement == null)
            {
                report.Error("No movement module selected. Every projectile needs exactly one.");
            }
            else if (!movement.enabled)
            {
                report.Error("The movement module is disabled. Every projectile needs exactly one active movement.", movement.GetType());
            }
            else
            {
                active.Add(movement);
            }

            Collect(movementModifiers, active, report);
            Collect(damageModifiers, active, report);
            Collect(behaviours, active, report);

            // duplicates
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].AllowMultiple) continue;
                for (int j = i + 1; j < active.Count; j++)
                {
                    if (active[j].GetType() == active[i].GetType())
                    {
                        report.Error($"'{active[i].GetType().Name}' can only be added once.", active[i].GetType());
                    }
                }
            }

            // module-specific rules (missing references, incompatible combinations)
            var ctx = new ProjectileValidationContext(report, movement, active);
            for (int i = 0; i < active.Count; i++)
            {
                ctx.Current = active[i].GetType();
                active[i].Validate(ctx);
            }

            bool dealsDamage = false;
            for (int i = 0; i < active.Count; i++) dealsDamage |= active[i].DealsDamage;
            if (!dealsDamage)
            {
                report.Warning("No module deals damage (add Direct Hit Damage, Area Damage or Piercing).");
            }
            return report;
        }

        public bool TryBuild(out ProjectileProfile profile, out ValidationReport report)
        {
            report = Validate();
            if (report.HasErrors)
            {
                profile = null;
                return false;
            }

            profile = new ProjectileProfile(
                movement,
                Filter(movementModifiers),
                Filter(damageModifiers),
                Filter(behaviours));
            return true;
        }

        /// <summary>Builds or throws with the validation messages.</summary>
        public ProjectileProfile Build()
        {
            if (!TryBuild(out var profile, out var report))
            {
                throw new InvalidOperationException("Invalid projectile configuration:\n" + report);
            }
            return profile;
        }

        private static void Collect<T>(List<T> source, List<ProjectileModule> active, ValidationReport report) where T : ProjectileModule
        {
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] == null)
                {
                    report.Warning("An empty module slot is ignored."); continue;
                }
                if (source[i].enabled)
                {
                    active.Add(source[i]);
                }
            }
        }

        private static T[] Filter<T>(List<T> source) where T : ProjectileModule
        {
            int n = 0;
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] != null && source[i].enabled) n++;
            }

            var result = new T[n];
            int k = 0;
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] != null && source[i].enabled) result[k++] = source[i];
            }
            return result;
        }
    }
}

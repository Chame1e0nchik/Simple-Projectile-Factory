using System.Collections.Generic;
using UnityEngine;

namespace SimpleProjectileFactory
{
    [CreateAssetMenu(fileName = "ProjectileProfile", menuName = "Projectiles/Projectile Profile")]
    public class ProjectileProfile : ScriptableObject
    {
        [SerializeField]
        private List<ProjectileBehaviourDefinition> behaviours = new();

        public IReadOnlyList<ProjectileBehaviourDefinition> Behaviours => behaviours;

    #if UNITY_EDITOR
        public List<ProjectileBehaviourDefinition> EditorBehaviours => behaviours;
    #endif
    }

    /// <summary>
    /// Define category of behaviour
    /// </summary>
    public enum ProjectileBehaviourCategory
    {
        Movement, // only one projectile movement beahviour
        MovementModifier, // influent on Movement behaviour
        DamageModifier, // damage defenition
        Lifecycle
    }

    public abstract class ProjectileBehaviourDefinition : ScriptableObject
    {
        [SerializeField] private string displayName;
        [SerializeField] private Texture2D icon;
        [SerializeField] private ProjectileBehaviourCategory category;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

        public Texture2D Icon => icon;
        public ProjectileBehaviourCategory Category => category;
    }
}
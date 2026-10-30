using System;
using System.Collections.Generic;
using UnityEditor;

namespace SimpleProjectileFactory.EditorTools
{
    /// <summary>Editor-side catalogue of every concrete module class (found via TypeCache, so new modules appear automatically)</summary>
    internal static class ProjectileModuleRegistry
    {
        internal sealed class Info
        {
            public Type Type;
            public string Name;
            public string Description;
            public ModuleCategory Category;
        }

        private static readonly List<Info> all = new List<Info>();
        private static readonly Dictionary<string, Info> byFullName = new Dictionary<string, Info>();
        private static readonly Dictionary<Type, Info> byType = new Dictionary<Type, Info>();
        private static bool built;

        public static IReadOnlyList<Info> All
        {
            get { Build(); return all; }
        }

        public static List<Info> Of(ModuleCategory category)
        {
            Build();
            var result = new List<Info>();
            for (int i = 0; i < all.Count; i++)
                if (all[i].Category == category) result.Add(all[i]);
            return result;
        }

        public static Info Find(Type type)
        {
            Build();
            byType.TryGetValue(type, out Info info);
            return info;
        }

        /// <summary>Resolves SerializedProperty.managedReferenceFullTypename ("Assembly Namespace.Type").</summary>
        public static Info FindByManagedTypename(string typename)
        {
            if (string.IsNullOrEmpty(typename)) return null;
            Build();
            int space = typename.IndexOf(' ');
            string full = space >= 0 ? typename.Substring(space + 1) : typename;
            byFullName.TryGetValue(full, out Info info);
            return info;
        }

        public static string NameOf(Type type)
        {
            Info info = Find(type);
            return info != null ? info.Name : ObjectNames.NicifyVariableName(type.Name);
        }

        private static void Build()
        {
            if (built) return;
            built = true;

            foreach (Type t in TypeCache.GetTypesDerivedFrom<ProjectileModule>())
            {
                if (t.IsAbstract || t.IsGenericType || t.GetConstructor(Type.EmptyTypes) == null) continue;

                ModuleCategory category;
                if (typeof(MovementModule).IsAssignableFrom(t)) category = ModuleCategory.Movement;
                else if (typeof(MovementModifierModule).IsAssignableFrom(t)) category = ModuleCategory.MovementModifier;
                else if (typeof(DamageModifierModule).IsAssignableFrom(t)) category = ModuleCategory.DamageModifier;
                else if (typeof(BehaviourModule).IsAssignableFrom(t)) category = ModuleCategory.Behaviour;
                else continue;

                var attr = (ProjectileModuleAttribute)Attribute.GetCustomAttribute(t, typeof(ProjectileModuleAttribute), false);
                var info = new Info
                {
                    Type = t,
                    Category = category,
                    Name = attr != null ? attr.DisplayName : ObjectNames.NicifyVariableName(t.Name),
                    Description = attr != null ? attr.Description : string.Empty
                };

                all.Add(info);
                byType[t] = info;
                byFullName[t.FullName] = info;
            }

            all.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        }
    }
}

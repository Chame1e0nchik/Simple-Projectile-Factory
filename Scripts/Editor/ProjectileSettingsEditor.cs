using UnityEditor;
using UnityEngine;

namespace SimpleProjectileFactory.EditorTools
{
    /// <summary>Inspector for ProjectileSettings: base parameters + a read-only module summary. Modules are edited in the wizard.</summary>
    [CustomEditor(typeof(ProjectileSettings), true)]
    public class ProjectileSettingsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject,
                "m_Script",
                ProjectileSettings.MovementField,
                ProjectileSettings.MovementModifiersField,
                ProjectileSettings.DamageModifiersField,
                ProjectileSettings.BehavioursField);
            serializedObject.ApplyModifiedProperties();

            var s = (ProjectileSettings)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Modules", EditorStyles.boldLabel);
            Summary("Movement", s.Movement == null ? "— none (invalid) —" : ProjectileModuleRegistry.NameOf(s.Movement.GetType()));
            Summary("Movement modifiers", Join(s.MovementModifiers));
            Summary("Damage modifiers", Join(s.DamageModifiers));
            Summary("Behaviours", Join(s.Behaviours));

            ValidationReport report = s.Validate();
            foreach (var e in report.Entries)
            {
                if (e.Severity == ValidationSeverity.Info) continue;
                EditorGUILayout.HelpBox(e.Message, e.Severity == ValidationSeverity.Error ? MessageType.Error : MessageType.Warning);
            }

            if (GUILayout.Button("Open in Projectile Wizard", GUILayout.Height(26)))
                ProjectileWizardWindow.Open(s);
        }

        private static void Summary(string label, string value)
        {
            EditorGUILayout.LabelField(label, value, EditorStyles.wordWrappedLabel);
        }

        private static string Join<T>(System.Collections.Generic.IReadOnlyList<T> modules) where T : ProjectileModule
        {
            if (modules.Count == 0) return "—";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < modules.Count; i++)
            {
                if (i > 0) sb.Append(" → ");
                sb.Append(modules[i] == null ? "(empty)" : ProjectileModuleRegistry.NameOf(modules[i].GetType()));
                if (modules[i] != null && !modules[i].enabled) sb.Append(" (off)");
            }
            return sb.ToString();
        }
    }
}

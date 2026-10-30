using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace SimpleProjectileFactory.EditorTools
{
    /// <summary>
    /// Tools > Simple Projectile Wizard
    /// </summary>
    public class ProjectileWizardWindow : EditorWindow
    {
        private const string DragKey = "Flyweight.ProjectileModuleType";

        private static readonly string[] BaseFields =
        {
            "type", "prefab", "damage", "speed", "lifetime", "casterLayer", "hitLayers", "hitTriggers", "collisionRadius",
            "alignToMovement", "muzzleFlash", "explosionEffect", "explosionEffectOn", "numberPrefab"
        };

        private static readonly string[] LegacyFields = { "maxDamage", "explosionRadius", "spawnAfterCollisionTime", "additionalExplosionEffect" };

        [SerializeField] private GameObject prefab;
        [SerializeField] private ProjectileSettings settings;
        [SerializeField] private ProjectileSettings copySource;
        [SerializeField] private List<GameObject> extraPrefabs = new List<GameObject>();
        [SerializeField] private bool removeMissingScripts = true;
        [SerializeField] private bool showReuse;
        [SerializeField] private bool showLegacy;

        private SerializedObject so;
        private SerializedProperty movementProp;
        private ReorderableList movementModifiersList;
        private ReorderableList damageModifiersList;
        private ReorderableList behavioursList;

        private ValidationReport report;
        private readonly Dictionary<Type, string> blocked = new Dictionary<Type, string>();
        private readonly List<KeyValuePair<string, MessageType>> prefabNotes = new List<KeyValuePair<string, MessageType>>();
        private readonly Queue<Action> deferred = new Queue<Action>();
        private bool needsRevalidate;

        private Vector2 mainScroll;
        private Vector2 libraryScroll;
        private Type pressedType;
        private Vector2 pressPosition;
        private GUIStyle boldFoldout;

        #region Initialize

        [MenuItem("Tools/SimpleProjectile Wizard", priority = 0)]
        public static void OpenWindow()
        {
            var window = GetWindow<ProjectileWizardWindow>("Projectile Wizard");
            window.minSize = new Vector2(860, 600);
            window.Show();
        }

        public static void Open(ProjectileSettings settings)
        {
            OpenWindow();
            GetWindow<ProjectileWizardWindow>().SetSettings(settings);
        }

        [MenuItem("CONTEXT/ProjectileSettings/Open in Projectile Wizard")]
        private static void OpenFromContext(MenuCommand command)
        {
            Open((ProjectileSettings)command.context);
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            Rebuild();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnUndoRedo()
        {
            Rebuild();
            Repaint();
        }

        private void OnProjectChange()
        {
            needsRevalidate = true;
            Repaint();
        }

        #endregion

        #region Selections

        private void SetPrefab(GameObject newPrefab)
        {
            prefab = newPrefab;
            if (newPrefab != null)
            {
                var component = newPrefab.GetComponent<Projectile>();
                ProjectileSettings linked = component != null ? component.settings as ProjectileSettings : null;
                if (linked == null) linked = FindSettingsFor(newPrefab);
                settings = linked;
            }
            Rebuild();
        }

        private void SetSettings(ProjectileSettings newSettings)
        {
            settings = newSettings;
            if (newSettings != null && newSettings.prefab != null) prefab = newSettings.prefab;
            Rebuild();
        }

        private static ProjectileSettings FindSettingsFor(GameObject p)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:ProjectileSettings"))
            {
                var s = AssetDatabase.LoadAssetAtPath<ProjectileSettings>(AssetDatabase.GUIDToAssetPath(guid));
                if (s != null && s.prefab == p) return s;
            }
            return null;
        }

        private void Rebuild()
        {
            if (settings != null)
            {
                so = new SerializedObject(settings);
                movementProp = so.FindProperty(ProjectileSettings.MovementField);
                movementModifiersList = CreateList(ProjectileSettings.MovementModifiersField, "Movement Modifiers");
                damageModifiersList = CreateList(ProjectileSettings.DamageModifiersField, "Damage Modifiers");
                behavioursList = CreateList(ProjectileSettings.BehavioursField, "Additional Behaviours");
            }
            else
            {
                so = null;
                movementProp = null;
                movementModifiersList = damageModifiersList = behavioursList = null;
            }

            Revalidate();
            RefreshPrefabNotes();
        }

        private void Revalidate()
        {
            needsRevalidate = false;
            report = settings != null ? settings.Validate() : null;
            blocked.Clear();
            if (settings == null || report == null) return;

            // Dry-run: would adding this module introduce a NEW error? If so the library greys it out.
            foreach (var info in ProjectileModuleRegistry.All)
            {
                if (info.Category == ModuleCategory.Movement) continue;

                var candidate = (ProjectileModule)Activator.CreateInstance(info.Type);
                ProjectileBuilder builder = ProjectileBuilder.FromSettings(settings);
                switch (info.Category)
                {
                    case ModuleCategory.MovementModifier: builder.AddMovementModifier((MovementModifierModule)candidate); break;
                    case ModuleCategory.DamageModifier: builder.AddDamageModifier((DamageModifierModule)candidate); break;
                    default: builder.AddBehaviour((BehaviourModule)candidate); break;
                }

                string reason = FirstNewError(builder.Validate(), report);
                if (reason != null) blocked[info.Type] = reason;
            }
        }

        private static string FirstNewError(ValidationReport after, ValidationReport before)
        {
            foreach (var e in after.Entries)
            {
                if (e.Severity != ValidationSeverity.Error) continue;
                bool existed = false;
                foreach (var b in before.Entries)
                    if (b.Message == e.Message) { existed = true; break; }
                if (!existed) return e.Message;
            }
            return null;
        }

        private void RefreshPrefabNotes()
        {
            prefabNotes.Clear();
            if (prefab == null) return;

            if (PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.NotAPrefab || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(prefab)))
            {
                Note("Select a prefab ASSET from the Project window (not a scene object).", MessageType.Error);
                return;
            }

            if (prefab.GetComponent<Projectile>() == null)
                Note("A Projectile component will be added when you press Apply.", MessageType.Info);

            int missing = 0;
            foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
                missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            if (missing > 0)
                Note(missing + " missing script(s) on the prefab (the old SpellBehaviour components). They are removed on Apply when the option below is on.", MessageType.Warning);

            foreach (Collider c in prefab.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger) continue;
                Note("The prefab has solid collider(s). Projectiles are moved by script and swept with raycasts, so they are not needed and can push physics objects. Consider removing or making them triggers.", MessageType.Warning);
                break;
            }

            if (prefab.GetComponentInChildren<Rigidbody>(true) != null)
                Note("Rigidbody found: it is set to kinematic at runtime; movement no longer uses physics.", MessageType.Info);
        }

        private void Note(string message, MessageType type)
        {
            prefabNotes.Add(new KeyValuePair<string, MessageType>(message, type));
        }

        #endregion


        #region OnGUI

        private void OnGUI()
        {
            if (Event.current.type == EventType.Layout && needsRevalidate)
                Revalidate();

            DrawTargetSection();

            if (settings == null || so == null)
            {
                DrawNoConfiguration();
                RunDeferred();
                return;
            }

            so.Update();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawLibrary();

                using (var scroll = new EditorGUILayout.ScrollViewScope(mainScroll))
                {
                    mainScroll = scroll.scrollPosition;
                    DrawBaseParameters();
                    DrawMovementSection();
                    DrawListSection(movementModifiersList, ModuleCategory.MovementModifier, "MOVEMENT MODIFIERS");
                    DrawListSection(damageModifiersList, ModuleCategory.DamageModifier, "DAMAGE MODIFIERS");
                    DrawListSection(behavioursList, ModuleCategory.Behaviour, "ADDITIONAL BEHAVIOURS");
                    DrawValidation();
                }
            }

            if (so.ApplyModifiedProperties())
                needsRevalidate = true;

            DrawFooter();
            RunDeferred();
        }

        private void RunDeferred()
        {
            if (deferred.Count == 0) return;
            while (deferred.Count > 0) deferred.Dequeue().Invoke();
            Repaint();
        }

        #endregion

        #region Target and Section

        private void DrawTargetSection()
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("1 · Target", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var p = (GameObject)EditorGUILayout.ObjectField("Projectile Prefab", prefab, typeof(GameObject), false);
                    if (EditorGUI.EndChangeCheck()) deferred.Enqueue(() => SetPrefab(p));
                    if (GUILayout.Button("Create New Prefab…", GUILayout.Width(140))) deferred.Enqueue(CreateNewPrefab);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var s = (ProjectileSettings)EditorGUILayout.ObjectField("Configuration", settings, typeof(ProjectileSettings), false);
                    if (EditorGUI.EndChangeCheck()) deferred.Enqueue(() => SetSettings(s));
                    if (GUILayout.Button("New Configuration…", GUILayout.Width(140))) deferred.Enqueue(CreateNewConfiguration);
                }
            }
        }

        private void DrawNoConfiguration()
        {
            EditorGUILayout.Space(8);
            if (prefab == null)
            {
                EditorGUILayout.HelpBox("Pick an existing projectile prefab (its configuration is found automatically), " +
                    "open a configuration asset, or create a new prefab.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox($"'{prefab.name}' has no projectile configuration yet.", MessageType.Info);
                if (GUILayout.Button("Create Configuration for this Prefab", GUILayout.Height(30)))
                {
                    deferred.Enqueue(() => CreateConfigurationFor(prefab));
                }
            }

            foreach (var note in prefabNotes)
            {
                EditorGUILayout.HelpBox(note.Key, note.Value);
            }
        }

        private void CreateNewPrefab()
        {
            string path = EditorUtility.SaveFilePanelInProject("Create Projectile Prefab", "NewProjectile", "prefab",
                "Choose where to save the new projectile prefab.");
            if (string.IsNullOrEmpty(path)) return;

            var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = Vector3.one * 0.3f;
            DestroyImmediate(visual.GetComponent<Collider>()); // projectiles are swept, they need no collider
            root.AddComponent<Projectile>();

            GameObject asset = PrefabUtility.SaveAsPrefabAsset(root, path);
            DestroyImmediate(root);
            SetPrefab(asset);
        }

        private void CreateNewConfiguration()
        {
            string path = EditorUtility.SaveFilePanelInProject("Create Projectile Configuration", "ProjectileSettings", "asset",
                "Choose where to save the configuration.");
            if (string.IsNullOrEmpty(path)) return;
            SetSettings(CreateSettingsAsset(path, prefab));
        }

        private void CreateConfigurationFor(GameObject p)
        {
            string dir = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(p)).Replace('\\', '/');
            string path = AssetDatabase.GenerateUniqueAssetPath(dir + "/" + p.name + "Settings.asset");
            SetSettings(CreateSettingsAsset(path, p));
        }

        private static ProjectileSettings CreateSettingsAsset(string path, GameObject forPrefab)
        {
            var s = CreateInstance<ProjectileSettings>();
            s.prefab = forPrefab;
            s.SetModules(ProjectilePresets.InstantBolt());
            AssetDatabase.CreateAsset(s, path);
            AssetDatabase.SaveAssets();
            return s;
        }

        #endregion

        #region Draw Libary Assets from Left side of Window

        private void DrawLibrary()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(235)))
            {
                EditorGUILayout.LabelField("Module Library", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Click to add, or drag onto a section.", EditorStyles.wordWrappedMiniLabel);

                using (var scroll = new EditorGUILayout.ScrollViewScope(libraryScroll))
                {
                    libraryScroll = scroll.scrollPosition;
                    DrawLibraryCategory("Movement  (replaces the current one)", ModuleCategory.Movement);
                    DrawLibraryCategory("Movement Modifiers", ModuleCategory.MovementModifier);
                    DrawLibraryCategory("Damage Modifiers", ModuleCategory.DamageModifier);
                    DrawLibraryCategory("Additional Behaviours", ModuleCategory.Behaviour);
                }
            }
        }

        private void DrawLibraryCategory(string title, ModuleCategory category)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
            foreach (var info in ProjectileModuleRegistry.Of(category))
            {
                string reason = null;
                if (category != ModuleCategory.Movement) blocked.TryGetValue(info.Type, out reason);
                DrawLibraryItem(info, reason);
            }
        }

        private void DrawLibraryItem(ProjectileModuleRegistry.Info info, string blockedReason)
        {
            bool usable = blockedReason == null;
            string tip = usable ? info.Description : info.Description + "\n\nUnavailable: " + blockedReason;
            var content = new GUIContent(info.Name, tip);
            Rect r = GUILayoutUtility.GetRect(content, EditorStyles.miniButton, GUILayout.Height(20));
            Event e = Event.current;

            switch (e.type)
            {
                case EventType.Repaint:
                {
                    Color prev = GUI.color;
                    if (!usable) GUI.color = new Color(1f, 1f, 1f, 0.4f);
                    EditorStyles.miniButton.Draw(r, content, r.Contains(e.mousePosition), false, false, false);
                    GUI.color = prev;
                    break;
                }
                case EventType.MouseDown:
                    if (e.button == 0 && r.Contains(e.mousePosition) && usable)
                    {
                        pressedType = info.Type;
                        pressPosition = e.mousePosition;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (pressedType == info.Type && (e.mousePosition - pressPosition).sqrMagnitude > 25f)
                    {
                        DragAndDrop.PrepareStartDrag();
                        DragAndDrop.SetGenericData(DragKey, info.Type);
                        DragAndDrop.StartDrag(info.Name);
                        pressedType = null;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (pressedType == info.Type)
                    {
                        pressedType = null;
                        if (r.Contains(e.mousePosition))
                        {
                            Type t = info.Type;
                            deferred.Enqueue(() => AddModule(t));
                        }
                        e.Use();
                    }
                    break;
            }
        }

        #endregion

        #region Right Side Section

        private static readonly Color MandatoryColor = new Color(0.90f, 0.55f, 0.15f, 0.30f);
        private static readonly Color OptionalColor = new Color(0.30f, 0.55f, 0.90f, 0.20f);
        private static readonly Color BaseColor = new Color(0.5f, 0.5f, 0.5f, 0.20f);

        private static void SectionHeader(string title, string tag, Color color)
        {
            EditorGUILayout.Space(6);
            Rect r = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, color);
            GUI.Label(new Rect(r.x + 6, r.y + 2, r.width - 12, 18), title, EditorStyles.boldLabel);
            var right = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
            GUI.Label(new Rect(r.x + 6, r.y + 2, r.width - 12, 18), tag, right);
        }

        private void DrawBaseParameters()
        {
            SectionHeader("BASE PARAMETERS", "REQUIRED", BaseColor);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                foreach (string field in BaseFields)
                {
                    SerializedProperty p = so.FindProperty(field);
                    if (p != null) EditorGUILayout.PropertyField(p, true);
                }

                showLegacy = EditorGUILayout.Foldout(showLegacy, "Legacy values (read by the 'Charged Blast' preset)", true);
                if (showLegacy)
                {
                    foreach (string field in LegacyFields)
                    {
                        SerializedProperty p = so.FindProperty(field);
                        if (p != null) EditorGUILayout.PropertyField(p, true);
                    }
                }
            }
        }

        private void DrawMovementSection()
        {
            SectionHeader("MOVEMENT", "MANDATORY · exactly one", MandatoryColor);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var info = ProjectileModuleRegistry.FindByManagedTypename(movementProp.managedReferenceFullTypename);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel("Movement Type");
                    string label = info != null ? info.Name : "— select a movement —";
                    if (EditorGUILayout.DropdownButton(new GUIContent(label), FocusType.Keyboard))
                        ShowMovementMenu();
                }

                if (info == null)
                {
                    EditorGUILayout.HelpBox("Every projectile needs exactly one movement. Pick one above, or drag one from the library.", MessageType.Error);
                }
                else
                {
                    EditorGUILayout.LabelField(info.Description, EditorStyles.wordWrappedMiniLabel);
                    DrawChildren(movementProp);
                    DrawModuleMessagesLayout(info.Type);
                }
            }
            HandleDrop(GUILayoutUtility.GetLastRect(), ModuleCategory.Movement);
        }

        private void ShowMovementMenu()
        {
            var menu = new GenericMenu();
            string current = movementProp.managedReferenceFullTypename;
            foreach (var info in ProjectileModuleRegistry.Of(ModuleCategory.Movement))
            {
                Type t = info.Type;
                bool selected = current != null && current.EndsWith(t.FullName, StringComparison.Ordinal);
                menu.AddItem(new GUIContent(info.Name), selected, () => deferred.Enqueue(() => SetMovement(t)));
            }
            menu.ShowAsContext();
        }

        private void DrawListSection(ReorderableList list, ModuleCategory category, string title)
        {
            SectionHeader(title, "OPTIONAL · any number · runs top → bottom", OptionalColor);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                list.DoLayoutList();
            }
            HandleDrop(GUILayoutUtility.GetLastRect(), category);
        }

        private ReorderableList CreateList(string fieldName, string header)
        {
            SerializedProperty listProp = so.FindProperty(fieldName);
            var list = new ReorderableList(so, listProp, true, true, false, true);

            list.drawHeaderCallback = r => EditorGUI.LabelField(r, header + "  (drag ≡ to reorder)");
            list.elementHeightCallback = i => i < listProp.arraySize ? ElementHeight(listProp.GetArrayElementAtIndex(i)) : EditorGUIUtility.singleLineHeight;
            list.drawElementCallback = (r, i, active, focused) =>
            {
                if (i < listProp.arraySize) DrawElement(r, listProp.GetArrayElementAtIndex(i));
            };
            list.drawNoneElementCallback = r =>
                EditorGUI.LabelField(r, "Empty — click or drag a module from the library.", EditorStyles.centeredGreyMiniLabel);
            return list;
        }

        #endregion


        #region List Elements

        private float ElementHeight(SerializedProperty element)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float h = line + 8f;

            var info = ProjectileModuleRegistry.FindByManagedTypename(element.managedReferenceFullTypename);
            if (info == null) return h;

            if (element.isExpanded)
            {
                SerializedProperty child = element.Copy();
                SerializedProperty end = element.GetEndProperty();
                bool enter = true;
                while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
                {
                    enter = false;
                    if (child.name == "enabled") continue;
                    h += EditorGUI.GetPropertyHeight(child, true) + 2f;
                }
            }

            return h + CountMessages(info.Type) * 38f;
        }

        private void DrawElement(Rect rect, SerializedProperty element)
        {
            float line = EditorGUIUtility.singleLineHeight;
            rect.y += 3f;

            var info = ProjectileModuleRegistry.FindByManagedTypename(element.managedReferenceFullTypename);
            if (info == null)
            {
                EditorGUI.LabelField(new Rect(rect.x, rect.y, rect.width, line),
                    "⚠ Missing module type (class renamed or deleted). Remove this entry.", EditorStyles.boldLabel);
                return;
            }

            if (boldFoldout == null) boldFoldout = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };

            SerializedProperty enabledProp = element.FindPropertyRelative("enabled");
            enabledProp.boolValue = EditorGUI.Toggle(new Rect(rect.x, rect.y, 18f, line), enabledProp.boolValue);
            element.isExpanded = EditorGUI.Foldout(new Rect(rect.x + 20f, rect.y, rect.width - 20f, line),
                element.isExpanded, new GUIContent(info.Name, info.Description), true, boldFoldout);

            float y = rect.y + line + 4f;

            using (new EditorGUI.DisabledScope(!enabledProp.boolValue))
            {
                if (element.isExpanded)
                {
                    SerializedProperty child = element.Copy();
                    SerializedProperty end = element.GetEndProperty();
                    bool enter = true;
                    while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
                    {
                        enter = false;
                        if (child.name == "enabled") continue;
                        float h = EditorGUI.GetPropertyHeight(child, true);
                        EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, h), child, true);
                        y += h + 2f;
                    }
                }
            }

            foreach (var entry in report.Entries)
            {
                if (entry.ModuleType != info.Type) continue;
                EditorGUI.HelpBox(new Rect(rect.x, y, rect.width, 34f), entry.Message, ToMessageType(entry.Severity));
                y += 38f;
            }
        }

        private static void DrawChildren(SerializedProperty module)
        {
            SerializedProperty child = module.Copy();
            SerializedProperty end = module.GetEndProperty();
            bool enter = true;
            while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
            {
                enter = false;
                if (child.name == "enabled") continue;
                EditorGUILayout.PropertyField(child, true);
            }
        }

        private int CountMessages(Type moduleType)
        {
            int n = 0;
            foreach (var e in report.Entries)
                if (e.ModuleType == moduleType) n++;
            return n;
        }

        private void DrawModuleMessagesLayout(Type moduleType)
        {
            foreach (var e in report.Entries)
                if (e.ModuleType == moduleType)
                    EditorGUILayout.HelpBox(e.Message, ToMessageType(e.Severity));
        }

        private static MessageType ToMessageType(ValidationSeverity s)
        {
            return s == ValidationSeverity.Error ? MessageType.Error : s == ValidationSeverity.Warning ? MessageType.Warning : MessageType.Info;
        }

        #endregion

        #region Drag&Drop and Adding

        private void HandleDrop(Rect rect, ModuleCategory category)
        {
            Event e = Event.current;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
            if (!rect.Contains(e.mousePosition)) return;

            var type = DragAndDrop.GetGenericData(DragKey) as Type;
            if (type == null) return;

            var info = ProjectileModuleRegistry.Find(type);
            bool accept = info != null && info.Category == category;
            DragAndDrop.visualMode = accept ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

            if (e.type == EventType.DragPerform && accept)
            {
                DragAndDrop.AcceptDrag();
                deferred.Enqueue(() => AddModule(type));
                e.Use();
            }
        }

        private void AddModule(Type type)
        {
            var info = ProjectileModuleRegistry.Find(type);
            if (info == null) return;

            if (info.Category == ModuleCategory.Movement)
            {
                SetMovement(type);
                return;
            }

            if (blocked.TryGetValue(type, out string reason))
            {
                ShowNotification(new GUIContent(reason));
                return;
            }

            so.Update();
            string field = info.Category == ModuleCategory.MovementModifier ? ProjectileSettings.MovementModifiersField
                         : info.Category == ModuleCategory.DamageModifier ? ProjectileSettings.DamageModifiersField
                         : ProjectileSettings.BehavioursField;

            SerializedProperty list = so.FindProperty(field);
            int index = list.arraySize;
            list.InsertArrayElementAtIndex(index);
            SerializedProperty element = list.GetArrayElementAtIndex(index);
            element.managedReferenceValue = Activator.CreateInstance(type);
            element.isExpanded = true;
            so.ApplyModifiedProperties();
            needsRevalidate = true;
        }

        private void SetMovement(Type type)
        {
            so.Update();
            movementProp.managedReferenceValue = Activator.CreateInstance(type);
            movementProp.isExpanded = true;
            so.ApplyModifiedProperties();
            needsRevalidate = true;
        }

        #endregion

        #region Validation

        private void DrawValidation()
        {
            SectionHeader("VALIDATION", report.HasErrors ? "errors block Apply" : "ok", BaseColor);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (report.Entries.Count == 0)
                {
                    EditorGUILayout.HelpBox("Configuration is valid.", MessageType.Info);
                    return;
                }

                foreach (var e in report.Entries)
                {
                    string prefix = e.ModuleType != null ? ProjectileModuleRegistry.NameOf(e.ModuleType) + ": " : string.Empty;
                    EditorGUILayout.HelpBox(prefix + e.Message, ToMessageType(e.Severity));
                }
            }
        }

        private void DrawFooter()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                showReuse = EditorGUILayout.Foldout(showReuse, "Reuse: presets, copy, apply to several prefabs", true);
                if (showReuse) DrawReuse();

                foreach (var note in prefabNotes)
                    EditorGUILayout.HelpBox(note.Key, note.Value);

                removeMissingScripts = EditorGUILayout.ToggleLeft(
                    "Remove missing scripts from the prefab on Apply (old SpellBehaviour components)", removeMissingScripts);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Save Configuration", GUILayout.Height(30)))
                        deferred.Enqueue(Save);

                    bool canApply = prefab != null && report != null && !report.HasErrors;
                    using (new EditorGUI.DisabledScope(!canApply))
                    {
                        if (GUILayout.Button(new GUIContent("Apply to Prefab", canApply ? "Saves the configuration and links it to the prefab." : "Fix the errors above and select a prefab."),
                                GUILayout.Height(30)))
                            deferred.Enqueue(() => ApplyToPrefab(prefab, settings));
                    }
                }
            }
        }

        private void DrawReuse()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Presets");
                if (EditorGUILayout.DropdownButton(new GUIContent("Replace modules with…"), FocusType.Keyboard))
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("Instant Bolt  (old ProjectileBehaviour)"), false, () => deferred.Enqueue(() => ApplyPreset(ProjectilePresets.InstantBolt())));
                    menu.AddItem(new GUIContent("Lobbed  (old BallisticBehaviour)"), false, () => deferred.Enqueue(() => ApplyPreset(ProjectilePresets.Lobbed())));
                    menu.AddItem(new GUIContent("Charged Blast  (old ChargeProjectileBehaviour)"), false, () => deferred.Enqueue(() => ApplyPreset(ProjectilePresets.ChargedBlast(settings))));
                    menu.AddItem(new GUIContent("Seeker Showcase"), false, () => deferred.Enqueue(() => ApplyPreset(ProjectilePresets.SeekerShowcase())));
                    menu.ShowAsContext();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                copySource = (ProjectileSettings)EditorGUILayout.ObjectField("Copy From", copySource, typeof(ProjectileSettings), false);
                using (new EditorGUI.DisabledScope(copySource == null || copySource == settings))
                {
                    if (GUILayout.Button("Copy", GUILayout.Width(60))) deferred.Enqueue(() => CopyFrom(copySource));
                }
            }

            // multi-prefab target
            Rect drop = GUILayoutUtility.GetRect(0, 34, GUILayout.ExpandWidth(true));
            GUI.Box(drop, "Drop prefabs here: each gets its own copy of this configuration", EditorStyles.helpBox);
            HandlePrefabDrop(drop);

            for (int i = 0; i < extraPrefabs.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.ObjectField(extraPrefabs[i], typeof(GameObject), false);
                    if (GUILayout.Button("✕", GUILayout.Width(24)))
                    {
                        int index = i;
                        deferred.Enqueue(() => extraPrefabs.RemoveAt(index));
                    }
                }
            }

            using (new EditorGUI.DisabledScope(extraPrefabs.Count == 0 || report == null || report.HasErrors))
            {
                if (GUILayout.Button("Create Configurations and Apply to " + extraPrefabs.Count + " Prefab(s)"))
                    deferred.Enqueue(ApplyToExtraPrefabs);
            }
        }

        private void HandlePrefabDrop(Rect rect)
        {
            Event e = Event.current;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
            if (!rect.Contains(e.mousePosition) || DragAndDrop.GetGenericData(DragKey) != null) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type != EventType.DragPerform) return;

            DragAndDrop.AcceptDrag();
            foreach (UnityEngine.Object o in DragAndDrop.objectReferences)
            {
                var go = o as GameObject;
                if (go == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(go))) continue;
                if (PrefabUtility.GetPrefabAssetType(go) == PrefabAssetType.NotAPrefab) continue;
                if (go != prefab && !extraPrefabs.Contains(go)) extraPrefabs.Add(go);
            }
            e.Use();
        }

        #endregion

        #region Action delegates

        private void Save()
        {
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            ShowNotification(new GUIContent("Configuration saved"));
        }

        private void ApplyPreset(ProjectileBuilder builder)
        {
            Undo.RecordObject(settings, "Apply Projectile Preset");
            settings.SetModules(builder);
            EditorUtility.SetDirty(settings);
            Rebuild();
        }

        private void CopyFrom(ProjectileSettings source)
        {
            Undo.RecordObject(settings, "Copy Projectile Configuration");
            GameObject keepPrefab = settings.prefab;
            FlyweightType keepType = settings.type;
            string keepName = settings.name;

            EditorUtility.CopySerialized(source, settings);

            settings.prefab = keepPrefab;
            settings.type = keepType;
            settings.name = keepName;
            EditorUtility.SetDirty(settings);
            Rebuild();
        }

        private void ApplyToPrefab(GameObject target, ProjectileSettings config)
        {
            so?.ApplyModifiedProperties();

            ValidationReport check = config.Validate();
            if (check.HasErrors)
            {
                EditorUtility.DisplayDialog("Cannot apply", check.ToString(), "OK");
                return;
            }

            if (!LinkPrefab(target, config)) return;

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            RefreshPrefabNotes();
            ShowNotification(new GUIContent("Applied to " + target.name));
        }

        private bool LinkPrefab(GameObject target, ProjectileSettings config)
        {
            string path = AssetDatabase.GetAssetPath(target);
            if (string.IsNullOrEmpty(path))
            {
                EditorUtility.DisplayDialog("Cannot apply", "'" + target.name + "' is not a prefab asset.", "OK");
                return false;
            }

            using (var scope = new PrefabUtility.EditPrefabContentsScope(path))
            {
                GameObject root = scope.prefabContentsRoot;

                if (removeMissingScripts)
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

                var projectile = root.GetComponent<Projectile>();
                if (projectile == null) projectile = root.AddComponent<Projectile>();
                projectile.settings = config; // lets the wizard find the configuration from the prefab later
            }

            config.prefab = target;
            return true;
        }

        private void ApplyToExtraPrefabs()
        {
            so?.ApplyModifiedProperties();
            int created = 0;

            foreach (GameObject p in extraPrefabs)
            {
                if (p == null || p == prefab) continue;

                string dir = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(p)).Replace('\\', '/');
                string path = AssetDatabase.GenerateUniqueAssetPath(dir + "/" + p.name + "Settings.asset");

                ProjectileSettings copy = Instantiate(settings); // deep copy, including the module lists
                copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
                copy.prefab = p;
                AssetDatabase.CreateAsset(copy, path);

                if (LinkPrefab(p, copy)) created++;
                EditorUtility.SetDirty(copy);
            }

            AssetDatabase.SaveAssets();
            extraPrefabs.Clear();
            ShowNotification(new GUIContent("Created " + created + " configuration(s)"));
        }

        #endregion
    }
}

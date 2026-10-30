using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimpleProjectileFactory
{
    // private ListView behaviourList;
    // private VisualElement catalogue;
    // private Label validationLabel;
    public class ProjectileFactoryWizard : EditorWindow
    {
        private ProjectileProfile profile;
        private ListView behaviourList;
        private VisualElement catalogue;
        private Label validationLabel;


        [MenuItem("Tools/Projectile Wizard")]
        public static void Open()
        {
            var window = GetWindow<ProjectileFactoryWizard>();
            window.titleContent = new GUIContent("Projectile Wizard");
            window.minSize = new Vector2(620, 480);
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.paddingLeft = 10;
            rootVisualElement.style.paddingRight = 10;
            rootVisualElement.style.paddingTop = 10;
            rootVisualElement.style.paddingBottom = 10;

            var heading = new Label("Projectile Behaviour Wizard");
            heading.style.fontSize = 20;
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            rootVisualElement.Add(heading);

            rootVisualElement.Add(new Label("Drag cards into the profile list."));

            var profileField = new ObjectField("Projectile Profiler")
            {
                objectType = typeof(ProjectileProfile),
                allowSceneObjects = false,
                value = profile
            };

            profileField.RegisterValueChangedCallback(evt =>
            {
                profile = evt.newValue as ProjectileProfile;
                RefreshList();
                RefreshValidation();
            });

            rootVisualElement.Add(profileField);

            var createButton = new Button(CreateProfile)
            {
                text = "Create New Projectile Profile"
            };

            rootVisualElement.Add(createButton);
        }

        private void CreateProfile()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Projectile Profile",
                "NewProjectileProfile",
                "asset",
                "Choose where to save the projectile profile.");

            if (string.IsNullOrEmpty(path))
                return;

            var newProfile = CreateInstance<ProjectileProfile>();

            AssetDatabase.CreateAsset(newProfile, path);
            AssetDatabase.SaveAssets();

            profile = newProfile;
            RefreshList();
            RefreshValidation();
        }

        private void RefreshCatalogue()
        {
            if (catalogue == null)
            {
                return;
            }

            catalogue.Clear();

            var definitions = AssetDatabase.FindAssets("t:ProjectileBehaviourDefinition")
                .Select(guid => AssetDatabase.LoadAssetAtPath<ProjectileBehaviourDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(asset => asset != null)
                .OrderBy(asset => asset.Category)
                .ThenBy(asset => asset.DisplayName);

            foreach (var definition in definitions)
            {
                catalogue.Add(MakeCatalogueCard(definition));
            }
        }

        private void RefreshList()
        {
            if (behaviourList == null)
                return;

            behaviourList.itemsSource = profile != null
                ? profile.EditorBehaviours
                : new List<ProjectileBehaviourDefinition>();

            behaviourList.Rebuild();
        }

        private VisualElement MakeCatalogueCard(ProjectileBehaviourDefinition definition)
        {
            var card = new VisualElement();
            card.style.flexDirection = FlexDirection.Row;
            card.style.alignItems = Align.Center;
            card.style.paddingTop = 6;
            card.style.paddingBottom = 6;
            card.style.paddingLeft = 6;
            card.style.paddingRight = 6;
            card.style.marginBottom = 5;
            card.style.borderBottomWidth = 1;
            card.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f);

            var icon = new Image
            {
                image = definition.Icon != null
                    ? definition.Icon
                    : AssetPreview.GetMiniThumbnail(definition)
            };

            icon.style.width = 42;
            icon.style.height = 42;
            icon.style.marginRight = 8;

            var labels = new VisualElement();
            labels.style.flexGrow = 1;

            var nameLabel = new Label(definition.DisplayName);
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

            var categoryLabel = new Label(definition.Category.ToString());
            categoryLabel.style.fontSize = 10;

            labels.Add(nameLabel);
            labels.Add(categoryLabel);

            var addButton = new Button(() => AddBehaviour(definition))
            {
                text = "+"
            };

            addButton.tooltip = "Add this behaviour to the profile";

            card.Add(icon);
            card.Add(labels);
            card.Add(addButton);

            // Drag this card into the behaviour list
            card.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;

                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = new Object[] { definition };
                DragAndDrop.StartDrag(definition.DisplayName);
            });

            return card;
        }

        private void RefreshValidation()
        {
            if (validationLabel == null)
                return;

            if (profile == null)
            {
                validationLabel.text = "Select or create a profile.";
                return;
            }

            int movementCount = profile.EditorBehaviours.Count(item =>item != null && item.Category == ProjectileBehaviourCategory.Movement);

            validationLabel.text = movementCount switch
            {
                0 => "Warning: Select one movement behaviour.",
                1 => "Profile configuration is valid.",
                _ => "Error: Only one movement behaviour is allowed."
            };

            validationLabel.style.color = movementCount == 1
                ? new Color(0.3f, 0.8f, 0.4f)
                : new Color(1f, 0.6f, 0.3f);
        }

        private void AddBehaviour(ProjectileBehaviourDefinition definition)
        {
            if (profile == null)
            {
                EditorUtility.DisplayDialog(
                    "No Projectile Profile",
                    "Create or select a projectile profile first.",
                    "OK");
                return;
            }

            if (definition == null)
                return;

            var behaviours = profile.EditorBehaviours;

            if (behaviours.Contains(definition))
                return;

            if (definition.Category == ProjectileBehaviourCategory.Movement &&
                behaviours.Any(item => item != null &&
                    item.Category == ProjectileBehaviourCategory.Movement))
            {
                EditorUtility.DisplayDialog(
                    "Movement Already Selected",
                    "A projectile can have only one primary movement behaviour.",
                    "OK");
                return;
            }

            Undo.RecordObject(profile, "Add Projectile Behaviour");
            behaviours.Add(definition);

            MarkProfileDirty();
            RefreshList();
            RefreshValidation();
        }

        private void MarkProfileDirty()
        {
            if (profile == null)
                return;

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }
    }
}
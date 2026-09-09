using System.IO;
using HypeSwarm.ClientOnly.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = System.Random;

namespace HypeSwarm.Editor
{
    /// <summary>
    /// Builds the movement prototype — arena, champion prefab, and camera — from code.
    /// </summary>
    /// <remarks>
    /// Scenes and prefabs are the one part of a Unity project that cannot be reviewed in a diff or
    /// merged by hand, so the arrangement is written down here instead. Rebuilding is a menu item
    /// rather than an archaeology exercise, changes to the layout show up as changes to this file,
    /// and the obstacle placement is seeded — which matters more than it looks, because obstacle
    /// density is a balance surface once items start bouncing off walls (§5.5.2). Judging whether
    /// movement feels good in an arena that is different every time is not judging anything.
    ///
    /// <para>Running this replaces the scene and prefab assets it owns. It does not touch anything
    /// else.</para>
    /// </remarks>
    public static class PrototypeArenaBuilder
    {
        const string ScenePath = "Assets/Scenes/Prototype.unity";
        const string PrefabPath = "Assets/HypeSwarm/Prefabs/Champion.prefab";
        const string MaterialFolder = "Assets/HypeSwarm/Art/Prototype";
        const string ControlsPath = "Assets/HypeSwarm/ClientOnly/Controls/HypeSwarmControls.inputactions";

        /// <summary>Fixed so the arena is the same one every time it is rebuilt or reviewed.</summary>
        const int ArenaSeed = 20260909;

        /// <summary>
        /// Metres per grid square on the floor. The grid is the only reference the eye has for how
        /// fast the champion is going — on a flat colour, seven metres per second and fourteen look
        /// identical, and speed is most of what is being judged here.
        /// </summary>
        const float GridMetres = 4f;

        const float ArenaRadius = 55f;
        const float SpawnClearance = 9f;
        const int ObstacleCount = 44;

        [MenuItem("Hype Swarm/Build Prototype Arena")]
        public static void BuildFromMenu()
        {
            if (EditorUtility.DisplayDialog(
                    "Build prototype arena",
                    $"This replaces {ScenePath} and {PrefabPath}.\n\nAny hand edits to them are lost.",
                    "Rebuild",
                    "Cancel"))
            {
                Build();
            }
        }

        /// <summary>
        /// Rebuilds without asking, so the arena can be regenerated from the command line. The menu
        /// entry is the one that confirms first.
        /// </summary>
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var ground = CreateMaterial("Ground", new Color(0.30f, 0.31f, 0.35f), smoothness: 0.1f);
            ApplyGroundGrid(ground);
            var obstacle = CreateMaterial("Obstacle", new Color(0.32f, 0.30f, 0.36f), smoothness: 0.2f);
            var champion = CreateMaterial("Champion", new Color(0.35f, 0.72f, 0.95f), smoothness: 0.4f);
            var accent = CreateMaterial("Accent", new Color(1f, 0.78f, 0.28f), smoothness: 0.5f);
            var trail = CreateUnlitMaterial("Trail", new Color(0.55f, 0.85f, 1f));
            var reticle = CreateUnlitMaterial("Reticle", new Color(1f, 0.78f, 0.28f));

            CreateLighting();
            CreateArena(ground, obstacle);

            var prefab = CreateChampionPrefab(champion, accent, trail, reticle);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = new Vector3(0f, 1.1f, 0f);

            CreateCamera();

            EnsureFolder(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Prototype arena rebuilt at {ScenePath}. Press Play; WASD moves, the mouse aims, " +
                      "Space or right mouse dashes.");
        }

        // --- Scene contents -------------------------------------------------------------------

        static void CreateLighting()
        {
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.9f);
            light.intensity = 1.3f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(48f, 35f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.28f, 0.30f, 0.36f);
            RenderSettings.ambientEquatorColor = new Color(0.20f, 0.21f, 0.25f);
            RenderSettings.ambientGroundColor = new Color(0.12f, 0.12f, 0.14f);
        }

        /// <summary>
        /// A floor, a boundary, and scattered cover. Cover is the point: judging a dash in an empty
        /// field tells you nothing, because there is nothing to dash behind, around, or into.
        /// </summary>
        static void CreateArena(Material ground, Material obstacle)
        {
            var root = new GameObject("Arena").transform;

            var floor = CreateBox("Floor", root, Vector3.zero, new Vector3(ArenaRadius * 2f, 1f, ArenaRadius * 2f), ground);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);

            // Walls, so a dash toward the edge has something to end against.
            for (var i = 0; i < 4; i++)
            {
                var angle = i * 90f;
                var direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;

                CreateBox(
                    $"Boundary {i}",
                    root,
                    direction * ArenaRadius + Vector3.up * 1.5f,
                    new Vector3(i % 2 == 0 ? ArenaRadius * 2f : 1f, 3f, i % 2 == 0 ? 1f : ArenaRadius * 2f),
                    obstacle);
            }

            var random = new Random(ArenaSeed);

            for (var i = 0; i < ObstacleCount; i++)
            {
                var angle = (float)random.NextDouble() * Mathf.PI * 2f;
                var radius = Mathf.Lerp(SpawnClearance, ArenaRadius - 6f, (float)random.NextDouble());

                var width = Mathf.Lerp(1.5f, 5f, (float)random.NextDouble());
                var depth = Mathf.Lerp(1.5f, 5f, (float)random.NextDouble());
                var height = Mathf.Lerp(1.5f, 3.5f, (float)random.NextDouble());

                var position = new Vector3(Mathf.Cos(angle) * radius, height * 0.5f, Mathf.Sin(angle) * radius);
                var box = CreateBox($"Obstacle {i}", root, position, new Vector3(width, height, depth), obstacle);
                box.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
            }
        }

        static void CreateCamera()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };

            var camera = go.AddComponent<Camera>();
            camera.fieldOfView = 50f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 400f;
            camera.backgroundColor = new Color(0.09f, 0.10f, 0.13f);
            camera.clearFlags = CameraClearFlags.SolidColor;

            // The rig finds the champion itself, so nothing here needs a scene reference that a
            // prefab could not carry.
            go.AddComponent<ChampionCameraRig>();
        }

        // --- Champion -------------------------------------------------------------------------

        static GameObject CreateChampionPrefab(
            Material body,
            Material accent,
            Material trailMaterial,
            Material reticleMaterial)
        {
            var root = new GameObject("Champion");

            var controller = root.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.5f;
            controller.center = new Vector3(0f, 0f, 0f);
            controller.slopeLimit = 50f;
            controller.stepOffset = 0.4f;
            controller.skinWidth = 0.04f;

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            var capsule = CreateShape("Body", PrimitiveType.Capsule, visual, Vector3.zero, Vector3.one, body);

            // The facing marker. With no basic attack and every ability aimed, which way the champion
            // is pointing is information the player needs continuously (§5.5.1) — and a capsule
            // gives them none of it. Kept low and long rather than mounted on the front, because the
            // camera looks down: anything at chest height is hidden by the champion's own body.
            CreateShape("Facing", PrimitiveType.Cube, visual, new Vector3(0f, -0.85f, 0.75f),
                new Vector3(0.3f, 0.12f, 0.8f), accent);

            var trail = new GameObject("Dash Trail").AddComponent<TrailRenderer>();
            trail.transform.SetParent(visual, false);
            trail.transform.localPosition = new Vector3(0f, -0.6f, 0f);
            trail.time = 0.28f;
            trail.widthCurve = AnimationCurve.Linear(0f, 0.55f, 1f, 0f);
            trail.sharedMaterial = trailMaterial;
            trail.emitting = false;
            trail.alignment = LineAlignment.View;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;

            // The reticle is a readout, not an object in the world: it must not cast a shadow, and
            // it must not be lit, or it dims exactly when the player most needs to find it.
            var reticle = CreateShape("Reticle", PrimitiveType.Cylinder, root.transform,
                Vector3.zero, new Vector3(0.5f, 0.01f, 0.5f), reticleMaterial);

            var reticleRenderer = reticle.GetComponent<MeshRenderer>();
            reticleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            reticleRenderer.receiveShadows = false;

            var champion = root.AddComponent<ChampionController>();
            Wire(champion, new (string, Object)[]
            {
                ("controls", AssetDatabase.LoadAssetAtPath<InputActionAsset>(ControlsPath)),
                ("visual", visual),
                ("reticle", reticle.transform)
            });

            var feedback = root.AddComponent<ChampionDashFeedback>();
            Wire(feedback, new (string, Object)[]
            {
                ("champion", champion),
                ("visual", capsule.transform),
                ("trail", trail)
            });

            EnsureFolder(Path.GetDirectoryName(PrefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            return prefab;
        }

        // --- Plumbing -------------------------------------------------------------------------

        /// <summary>
        /// Assigns private serialized fields. Those fields are private because nothing but the
        /// inspector should write them; this is the inspector, in code.
        /// </summary>
        static void Wire(Object target, (string Field, Object Value)[] assignments)
        {
            var serialized = new SerializedObject(target);

            foreach (var (field, value) in assignments)
            {
                var property = serialized.FindProperty(field);

                if (property == null)
                {
                    Debug.LogError($"{target.GetType().Name} has no serialized field '{field}'.");
                    continue;
                }

                property.objectReferenceValue = value;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject CreateBox(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            return CreateShape(name, PrimitiveType.Cube, parent, position, scale, material, keepCollider: true);
        }

        static GameObject CreateShape(
            string name,
            PrimitiveType type,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;

            // Everything on the champion is decoration. A collider here would fight the
            // CharacterController for the same space and jitter the character against itself.
            if (!keepCollider)
            {
                Object.DestroyImmediate(go.GetComponent<Collider>());
            }

            return go;
        }

        /// <summary>
        /// Paints a metre grid onto the floor material. Generated rather than authored so the square
        /// size stays tied to <see cref="GridMetres"/> and cannot drift into lying about scale.
        /// </summary>
        public static void ApplyGroundGrid(Material ground)
        {
            const int pixelsPerMetre = 32;
            const int line = 2;

            var size = (int)GridMetres * pixelsPerMetre;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);

            var fill = new Color(1f, 1f, 1f, 1f);
            var minor = new Color(0.78f, 0.80f, 0.86f, 1f);
            var major = new Color(0.55f, 0.60f, 0.72f, 1f);
            var pixels = new Color[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var onMajor = x < line || y < line;
                    var onMinor = x % pixelsPerMetre < line || y % pixelsPerMetre < line;

                    pixels[y * size + x] = onMajor ? major : onMinor ? minor : fill;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            EnsureFolder(MaterialFolder);

            // Written as a PNG rather than a .asset. A Texture2D asset serialises its pixels as
            // base64 YAML — 175KB of it for this one, rewritten in full every time the arena is
            // rebuilt. The PNG is a couple of kilobytes and Unity imports it with the settings below.
            var path = $"{MaterialFolder}/Grid.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.anisoLevel = 8;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }

            var tiling = Vector2.one * (ArenaRadius * 2f / GridMetres);

            ground.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            ground.SetTextureScale("_BaseMap", tiling);
            EditorUtility.SetDirty(ground);
        }

        static Material CreateMaterial(string name, Color color, float smoothness)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);

            return SaveMaterial(material, name);
        }

        static Material CreateUnlitMaterial(string name, Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", color);

            return SaveMaterial(material, name);
        }

        static Material SaveMaterial(Material material, string name)
        {
            EnsureFolder(MaterialFolder);

            var path = $"{MaterialFolder}/{name}.mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(material, path);

            return AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        /// <summary>
        /// Creates a folder through the asset database rather than the file system. A directory made
        /// behind Unity's back is not a folder it will write assets into until the next import, and
        /// the failure is a null asset rather than an error.
        /// </summary>
        static void EnsureFolder(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            var parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetPath));
        }

        static void AddToBuildSettings(string scenePath)
        {
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.path == scenePath)
                {
                    return;
                }
            }

            var scenes = new EditorBuildSettingsScene[EditorBuildSettings.scenes.Length + 1];
            EditorBuildSettings.scenes.CopyTo(scenes, 0);
            scenes[scenes.Length - 1] = new EditorBuildSettingsScene(scenePath, true);

            EditorBuildSettings.scenes = scenes;
        }
    }
}

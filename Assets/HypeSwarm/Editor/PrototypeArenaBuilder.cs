using System.IO;
using HypeSwarm.ClientOnly.Net;
using HypeSwarm.ClientOnly.Player;
using HypeSwarm.ClientOnly.Presentation;
using HypeSwarm.Shared.Abilities;
using HypeSwarm.Shared.Combat;
using HypeSwarm.Shared.Stats;
using HypeSwarm.Shared.Net;
using kcp2k;
using Mirror;
using Mirror.FizzySteam;
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
        const string ChampionDefinitionPath = "Assets/HypeSwarm/Content/Champions/Pyre.asset";

        /// <summary>Fixed so the arena is the same one every time it is rebuilt or reviewed.</summary>
        const int ArenaSeed = 20260909;

        /// <summary>
        /// Metres per grid square on the floor. The grid is the only reference the eye has for how
        /// fast the champion is going — on a flat colour, seven metres per second and fourteen look
        /// identical, and speed is most of what is being judged here.
        /// </summary>
        const float GridMetres = 4f;

        /// <summary>
        /// Training dummies, and enough of them to tell a cone from a circle.
        /// </summary>
        /// <remarks>
        /// Cubes, placed in the scene rather than spawned: Mirror spawns scene network objects itself on
        /// server start, so they need no spawn logic and no prefab, and the arena stays the only thing
        /// that decides where they are. The horde proper is Phase 2 step 7 and will not work like this.
        /// </remarks>
        const int DummyCount = 6;

        const float DummyRingRadius = 13f;

        const float ArenaRadius = 55f;
        const float SpawnClearance = 9f;
        const int ObstacleCount = 44;

        /// <summary>Metres from the middle to each spawn point. Inside <see cref="SpawnClearance"/>,
        /// so nobody arrives inside an obstacle.</summary>
        const float SpawnRingRadius = 5f;

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

            // White, because every shape tints it: one material for every outline an ability draws.
            var shapes = CreateUnlitMaterial("Shapes", Color.white);

            CreateLighting();
            CreateArena(ground, obstacle);

            var prefab = CreateChampionPrefab(champion, accent, trail, reticle, shapes);

            CreateSpawnPoints();
            CreateTrainingDummies(accent, shapes);
            CreateNetwork(prefab);
            CreateCamera();

            EnsureFolder(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Prototype arena rebuilt at {ScenePath}. Press Play to host; WASD moves, the " +
                      "mouse aims, left mouse is the primary, E the secondary, Space or right mouse " +
                      "dashes, hold R to channel the ultimate, F1 shows the network panel.");
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

            // Left without a target on purpose. Five champions will be in this scene and only one of
            // them is ours; ChampionOwnership points the rig at it the moment authority arrives.
            go.AddComponent<ChampionCameraRig>();
        }

        /// <summary>
        /// One spawn point per player, in a ring facing outward. Slot <c>n</c> always gets point
        /// <c>n</c>, so five players arriving at once do not land inside one another.
        /// </summary>
        static void CreateSpawnPoints()
        {
            var root = new GameObject("Spawn Points").transform;

            for (var i = 0; i < LobbyRoster.MaxPlayers; i++)
            {
                var angle = i * 360f / LobbyRoster.MaxPlayers;
                var direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;

                var point = new GameObject($"Spawn {i}");
                point.transform.SetParent(root, false);
                point.transform.position = direction * SpawnRingRadius + Vector3.up * 1.1f;
                point.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

                point.AddComponent<NetworkStartPosition>();
            }
        }

        /// <summary>
        /// The session object: a manager, both transports, and the switch that picks between them.
        /// </summary>
        /// <remarks>
        /// Both transports are here and neither is wired into the manager. That is the whole of spec
        /// §13.4 in scene form — <see cref="NetworkBootstrap"/> assigns one at runtime, so which one
        /// this build uses is not a property of the scene.
        /// </remarks>
        static void CreateNetwork(GameObject championPrefab)
        {
            var go = new GameObject("Network");

            var direct = go.AddComponent<KcpTransport>();
            direct.port = NetworkLaunchOptions.DefaultPort;

            var steam = go.AddComponent<FizzySteamworks>();
            steam.enabled = false;

            var manager = go.AddComponent<HypeSwarmNetworkManager>();
            manager.playerPrefab = championPrefab;
            manager.maxConnections = LobbyRoster.MaxPlayers;
            manager.autoCreatePlayer = false;
            manager.playerSpawnMethod = PlayerSpawnMethod.RoundRobin;

            // Mirror binds this in Awake; the bootstrap overwrites it before anything starts. Left
            // null the manager logs an error on load, which is noise on every single run.
            manager.transport = direct;

            var bootstrap = go.AddComponent<NetworkBootstrap>();
            Wire(bootstrap, new (string, Object)[]
            {
                ("manager", manager),
                ("directTransport", direct),
                ("steamTransport", steam)
            });

            var panel = go.AddComponent<NetworkDevPanel>();
            Wire(panel, new (string, Object)[]
            {
                ("manager", manager),
                ("bootstrap", bootstrap)
            });
        }

        /// <summary>
        /// Something to hit. A ring of cubes with health, on the hostile side.
        /// </summary>
        /// <remarks>
        /// In a ring rather than a line, and inside the obstacle clearance, so a cone aimed from the middle
        /// catches two or three and a circle catches more — which is the only way to see by eye that a
        /// shape is the shape it was authored as.
        /// </remarks>
        static void CreateTrainingDummies(Material material, Material shapes)
        {
            var root = new GameObject("Training Dummies").transform;

            for (var i = 0; i < DummyCount; i++)
            {
                var angle = i * 360f / DummyCount + 18f;
                var direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;

                var dummy = CreateBox(
                    $"Dummy {i}",
                    root,
                    direction * DummyRingRadius + Vector3.up,
                    new Vector3(1.1f, 2f, 1.1f),
                    material);

                dummy.AddComponent<NetworkIdentity>();

                // A stat sheet as well as health, so a slow or a shred applied to a dummy has somewhere to
                // land — otherwise half of what an ability does is untestable.
                dummy.AddComponent<ChampionStats>();

                var health = dummy.AddComponent<Health>();

                WireEnum(health, "faction", (int)Faction.Enemies);

                // So a hit, a slow and a death are visible on the dummy itself, on every machine.
                var feedback = dummy.AddComponent<CombatantFeedbackPresenter>();
                Wire(feedback, new (string, Object)[] { ("lineMaterial", shapes) });
                WireArray(feedback, "renderers", new Object[] { dummy.GetComponent<MeshRenderer>() });
            }
        }

        // --- Champion -------------------------------------------------------------------------

        static GameObject CreateChampionPrefab(
            Material body,
            Material accent,
            Material trailMaterial,
            Material reticleMaterial,
            Material shapesMaterial)
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

            root.AddComponent<NetworkIdentity>();

            // Position is client-authoritative (§10). In a PvE game the only person a movement
            // cheat affects is the cheater, and taking that trade removes prediction and
            // reconciliation from the project entirely — which is the difference between five
            // players working and a netcode rewrite in Phase 5.
            var networkTransform = root.AddComponent<NetworkTransformUnreliable>();
            networkTransform.syncDirection = SyncDirection.ClientToServer;
            networkTransform.syncPosition = true;
            networkTransform.syncRotation = false; // the root never turns; facing is on the visual
            networkTransform.syncScale = false;

            root.AddComponent<ChampionNetworkState>();

            // Stats before health, and health before abilities: health reads the stat sheet for its
            // maximum, and an ability reads both. None of them depends on Awake order — ChampionStats
            // builds its sheet on first read — but the inspector reading in this order is a kindness.
            root.AddComponent<ChampionStats>();
            root.AddComponent<Health>();

            var abilities = root.AddComponent<AbilityRunner>();
            Wire(abilities, new (string, Object)[]
            {
                ("champion", AssetDatabase.LoadAssetAtPath<ChampionDefinition>(ChampionDefinitionPath))
            });

            var champion = root.AddComponent<ChampionController>();
            Wire(champion, new (string, Object)[]
            {
                ("controls", AssetDatabase.LoadAssetAtPath<InputActionAsset>(ControlsPath)),
                ("reticle", reticle.transform)
            });

            // Off on the prefab. A champion that is not ours must never read this machine's
            // keyboard, not even for the frame between spawning and authority arriving —
            // ChampionOwnership switches it on for the one that is.
            champion.enabled = false;

            var ownership = root.AddComponent<ChampionOwnership>();
            Wire(ownership, new (string, Object)[] { ("controller", champion) });
            WireArray(ownership, "localOnly", new Object[] { reticle });

            var facing = root.AddComponent<ChampionFacingPresenter>();
            Wire(facing, new (string, Object)[] { ("visual", visual) });

            var feedback = root.AddComponent<ChampionDashFeedback>();
            Wire(feedback, new (string, Object)[]
            {
                ("visual", capsule.transform),
                ("trail", trail)
            });

            // What the abilities do, drawn (§12). On every champion, not just the local one: the runner
            // raises the same events for the other four players' casts.
            var casts = root.AddComponent<AbilityCastPresenter>();
            Wire(casts, new (string, Object)[] { ("lineMaterial", shapesMaterial) });

            var combat = root.AddComponent<CombatantFeedbackPresenter>();
            Wire(combat, new (string, Object)[] { ("lineMaterial", shapesMaterial) });
            WireArray(combat, "renderers", new Object[] { capsule.GetComponent<MeshRenderer>() });

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

        /// <summary>Assigns a serialized array field, for the same reason as <see cref="Wire"/>.</summary>
        static void WireArray(Object target, string field, Object[] values)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);

            if (property == null)
            {
                Debug.LogError($"{target.GetType().Name} has no serialized field '{field}'.");
                return;
            }

            property.arraySize = values.Length;

            for (var i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Assigns a serialized enum field, for the same reason as <see cref="Wire"/>.</summary>
        static void WireEnum(Object target, string field, int value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);

            if (property == null)
            {
                Debug.LogError($"{target.GetType().Name} has no serialized field '{field}'.");

                return;
            }

            property.enumValueIndex = value;
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

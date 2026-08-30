using System;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-shot builder for the drunk swordfight minigame. Run headless:
///
///   Unity.exe -batchmode -projectPath &lt;repo&gt; -executeMethod DrunkSwordfightSetup.RunAll -logFile -
///
/// It generates the wood/label textures and URP materials, wires the real
/// character model into the PlayerAvatar prefab (all minigames benefit),
/// builds the MG_DrunkSwordfight scene, and registers the scene in Build
/// Settings and in the Hub's MinigameManager list. Safe to re-run.
/// </summary>
public static class DrunkSwordfightSetup
{
    private const string ScenePath = "Assets/Scenes/MG_DrunkSwordfight.unity";
    private const string SceneName = "MG_DrunkSwordfight";
    private const string HubPath = "Assets/Scenes/Hub.unity";
    private const string HubPlatformFallPath = "Assets/Scenes/MG_PlatformFall.unity";
    private const string ResDir = "Assets/Resources/DrunkFight";
    private const string TexDir = ResDir + "/Tex";
    private const string MatDir = ResDir + "/Mats";
    private const string CharacterFbx = "Assets/Character/character.fbx";
    private const string ControllerPath = "Assets/Character/PlayerController.controller";
    private const string AvatarPrefabPath = "Assets/Prefab/PlayerAvatar.prefab";

    public static void RunAll()
    {
        EnsureFolders();
        CreateTextures();
        CreateMaterials();
        SetupAvatarPrefab();
        BuildScene();
        WireHubAndBuildSettings();
        AssetDatabase.SaveAssets();
        Debug.Log("[DrunkSwordfightSetup] All done.");
    }

    // ------------------------------------------------------------------ folders

    private static void EnsureFolders()
    {
        foreach (var dir in new[] { "Assets/Resources", ResDir, TexDir, MatDir })
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(dir), Path.GetFileName(dir));
    }

    // ------------------------------------------------------------------ textures

    private static void CreateTextures()
    {
        WoodTexture(TexDir + "/WoodFloor.png", planksAlongX: true, baseShade: 1.0f);
        WoodTexture(TexDir + "/WoodWall.png", planksAlongX: false, baseShade: 0.72f);
        LabelTexture(TexDir + "/WhiskeyLabel.png");
    }

    /// <summary>512x512 painted planks, deterministic (fixed seed).</summary>
    private static void WoodTexture(string path, bool planksAlongX, float baseShade)
    {
        const int size = 512;
        const int planks = 8;
        int plankSize = size / planks;

        var rng = new System.Random(planksAlongX ? 1001 : 1002);
        var tints = new float[planks];
        var joints = new int[planks];
        for (int i = 0; i < planks; i++)
        {
            tints[i] = 0.82f + 0.18f * (float)rng.NextDouble();
            joints[i] = rng.Next(0, size);
        }

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // "across" = perpendicular to the grain, "along" = with it
                int across = planksAlongX ? y : x;
                int along = planksAlongX ? x : y;
                int plank = across / plankSize;
                int inPlank = across % plankSize;

                float tint = tints[plank];

                // grain streaks running along the plank
                float grain = Streak(along, plank * 37);
                float fine = Hash01(x * 3 + 11, y * 3 + plank) * 0.06f;

                // dark gaps between planks and at the butt joints
                float edge =
                    (inPlank == 0 || inPlank == plankSize - 1) ? 0.45f :
                    Math.Abs(along - joints[plank]) < 2 ? 0.55f : 1f;

                float v = (0.85f - grain * 0.18f + fine) * tint * edge * baseShade;

                // warm brown wood
                px[y * size + x] = new Color32(
                    Byte(v * 0.70f), Byte(v * 0.52f), Byte(v * 0.33f), 255);
            }
        }

        tex.SetPixels32(px);
        tex.Apply();
        WriteAndImport(tex, path);
    }

    private static byte Byte(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp(v, 0f, 1f) * 255f);

    /// <summary>Smooth-ish 0..1 streak pattern along one axis.</summary>
    private static float Streak(int along, int seed)
    {
        float a = Hash01(along / 24 + seed, seed);
        float b = Hash01(along / 24 + 1 + seed, seed);
        float t = (along % 24) / 24f;
        return Mathf.Lerp(a, b, t);
    }

    private static float Hash01(int x, int y)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0x7fffffff) / (float)0x7fffffff;
        }
    }

    /// <summary>Cream label with an amber border and cheeky XXX marks.</summary>
    private static void LabelTexture(string path)
    {
        const int w = 256, h = 128;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color32[w * h];

        var cream = new Color32(236, 225, 200, 255);
        var amber = new Color32(196, 128, 38, 255);
        var dark = new Color32(56, 38, 22, 255);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var c = cream;
                bool border = x < 8 || x >= w - 8 || y < 8 || y >= h - 8;
                bool band = y > h - 26 && y < h - 12;
                int cx = x - w / 2;
                bool xxx = y > 34 && y < 84 && (
                    (cx > -60 && cx < -44) || (cx > -8 && cx < 8) || (cx > 44 && cx < 60));
                if (band) c = dark;
                if (xxx) c = amber;
                if (border) c = amber;
                px[y * w + x] = c;
            }
        }

        tex.SetPixels32(px);
        tex.Apply();
        WriteAndImport(tex, path);
    }

    private static void WriteAndImport(Texture2D tex, string path)
    {
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        if (AssetDatabase.LoadAssetAtPath<TextureImporter>(path) is { } ti)
        {
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.filterMode = FilterMode.Bilinear;
            ti.mipmapEnabled = true;
            ti.SaveAndReimport();
        }
    }

    // ------------------------------------------------------------------ materials

    private static void CreateMaterials()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) throw new Exception("URP Lit shader not found — is this a URP project?");

        var woodFloor = LoadTex("WoodFloor");
        var woodWall = LoadTex("WoodWall");
        var label = LoadTex("WhiskeyLabel");

        Mat("WoodFloor", m => { m.SetTexture("_BaseMap", woodFloor); m.SetTextureScale("_BaseMap", new Vector2(3.5f, 3.5f)); m.SetFloat("_Smoothness", 0.15f); });
        Mat("WoodWall", m => { m.SetTexture("_BaseMap", woodWall); m.SetFloat("_Smoothness", 0.12f); });
        Mat("PlainWood", m => { m.SetColor("_BaseColor", Wood(0.47f, 0.33f, 0.19f)); m.SetFloat("_Smoothness", 0.2f); });
        Mat("Stone", m => { m.SetColor("_BaseColor", new Color(0.36f, 0.35f, 0.38f)); m.SetFloat("_Smoothness", 0.08f); });
        Mat("MetalDark", m => { m.SetColor("_BaseColor", new Color(0.13f, 0.13f, 0.15f)); m.SetFloat("_Metallic", 1f); m.SetFloat("_Smoothness", 0.55f); });
        Mat("GlassAmber", m =>
        {
            m.SetColor("_BaseColor", new Color(1f, 0.6f, 0.2f));
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(1f, 0.45f, 0.12f) * 1.6f);
            m.SetFloat("_Smoothness", 0.8f);
        });
        Mat("Steel", m => { m.SetColor("_BaseColor", new Color(0.72f, 0.75f, 0.80f)); m.SetFloat("_Metallic", 0.9f); m.SetFloat("_Smoothness", 0.85f); });
        Mat("Brass", m => { m.SetColor("_BaseColor", new Color(0.78f, 0.58f, 0.22f)); m.SetFloat("_Metallic", 0.85f); m.SetFloat("_Smoothness", 0.7f); });
        Mat("Leather", m => { m.SetColor("_BaseColor", Wood(0.30f, 0.15f, 0.07f)); m.SetFloat("_Smoothness", 0.3f); });
        Mat("WhiskeyGlass", m =>
        {
            m.SetColor("_BaseColor", Wood(0.55f, 0.22f, 0.05f));
            m.SetFloat("_Smoothness", 0.75f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(0.3f, 0.09f, 0.01f));
        });
        Mat("Cork", m => { m.SetColor("_BaseColor", new Color(0.66f, 0.51f, 0.36f)); m.SetFloat("_Smoothness", 0.4f); });
        Mat("WhiskeyLabel", m => { m.SetTexture("_BaseMap", label); m.SetFloat("_Smoothness", 0.2f); });
    }

    private static Color Wood(float r, float g, float b) => new(r, g, b);

    private static Texture2D LoadTex(string name) =>
        AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{name}.png");

    private static void Mat(string name, Action<Material> configure)
    {
        var path = $"{MatDir}/{name}.mat";
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(lit);
            AssetDatabase.CreateAsset(mat, path);
        }
        else
        {
            mat.shader = lit;
        }
        configure(mat);
        EditorUtility.SetDirty(mat);
    }

    // ------------------------------------------------------------------ avatar prefab

    private static void SetupAvatarPrefab()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterFbx);
        if (source == null) throw new Exception($"Character model not found: {CharacterFbx}");

        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        var avatar = AssetDatabase.LoadAllAssetsAtPath(CharacterFbx).OfType<Avatar>().FirstOrDefault();

        var contents = PrefabUtility.LoadPrefabContents(AvatarPrefabPath);
        try
        {
            // capsule stays as the collider/physics proxy, stop drawing it
            var capsuleRenderer = contents.GetComponent<MeshRenderer>();
            if (capsuleRenderer != null) capsuleRenderer.enabled = false;

            var existingModel = contents.transform.Find("Model");
            if (existingModel != null) UnityEngine.Object.DestroyImmediate(existingModel.gameObject);

            var model = (GameObject)PrefabUtility.InstantiatePrefab(source, contents.transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            // fit the model to game scale no matter what units the FBX came in
            var smr = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr == null) throw new Exception("Character model has no SkinnedMeshRenderer.");
            float rawHeight = smr.bounds.size.y;
            if (rawHeight < 0.01f) throw new Exception($"Character model has nonsense bounds: {rawHeight}");
            float scale = 1.75f / rawHeight;
            model.transform.localScale = Vector3.one * scale;

            // put the feet on local y = -1 (root spawns 1m above the floor)
            float footY = smr.bounds.min.y;
            model.transform.localPosition = new Vector3(0f, -footY - 1f, 0f);

            Debug.Log($"[DrunkSwordfightSetup] model '{source.name}' raw height {rawHeight:F2} -> scale {scale:F4}, " +
                      $"foot at {footY:F2}, material '{(smr.sharedMaterial != null ? smr.sharedMaterial.name : "none")}'");

            var animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.avatar = avatar;
            animator.applyRootMotion = false;

            if (contents.GetComponent<DrunkFightPlayer>() == null)
                contents.AddComponent<DrunkFightPlayer>();
            if (contents.GetComponent<AvatarAnimatorDriver>() == null)
                contents.AddComponent<AvatarAnimatorDriver>();

            PrefabUtility.SaveAsPrefabAsset(contents, AvatarPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
        Debug.Log("[DrunkSwordfightSetup] Avatar prefab updated with the real character.");
    }

    // ------------------------------------------------------------------ scene

    private static void BuildScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---- arena --------------------------------------------------------
        var arenaGo = new GameObject("TavernArena");
        var arena = arenaGo.AddComponent<TavernArena>();
        var arenaSo = new SerializedObject(arena);
        SetRef(arenaSo, "woodFloor", "WoodFloor");
        SetRef(arenaSo, "woodWall", "WoodWall");
        SetRef(arenaSo, "plainWood", "PlainWood");
        SetRef(arenaSo, "stone", "Stone");
        SetRef(arenaSo, "metalDark", "MetalDark");
        SetRef(arenaSo, "glassAmber", "GlassAmber");
        arenaSo.ApplyModifiedPropertiesWithoutUndo();

        // ---- minigame ------------------------------------------------------
        var mgGo = new GameObject("Minigame");
        mgGo.AddComponent<NetworkObject>();
        mgGo.AddComponent<DrunkSwordfightMinigame>();

        var spawnPositions = new[]
        {
            new Vector3(-3.5f, 1f, -3.5f), new Vector3(3.5f, 1f, -3.5f),
            new Vector3(-3.5f, 1f, 3.5f), new Vector3(3.5f, 1f, 3.5f)
        };
        var spawns = new Transform[spawnPositions.Length];
        for (int i = 0; i < spawnPositions.Length; i++)
        {
            var spawn = new GameObject($"Spawn{i}");
            spawn.transform.SetParent(mgGo.transform, false);
            spawn.transform.localPosition = spawnPositions[i];
            spawns[i] = spawn.transform;
        }

        var mgSo = new SerializedObject(mgGo.GetComponent<DrunkSwordfightMinigame>());
        var sp = mgSo.FindProperty("spawnPoints");
        sp.arraySize = spawns.Length;
        for (int i = 0; i < spawns.Length; i++)
            sp.GetArrayElementAtIndex(i).objectReferenceValue = spawns[i];
        mgSo.FindProperty("timeLimitSeconds").floatValue = 120f;
        mgSo.ApplyModifiedPropertiesWithoutUndo();

        // editor-preview atmosphere (runtime re-applies it per peer anyway)
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.27f, 0.21f, 0.16f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.07f, 0.05f, 0.03f);
        RenderSettings.fogDensity = 0.008f;

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[DrunkSwordfightSetup] Scene saved: {ScenePath}");
    }

    private static void SetRef(SerializedObject so, string field, string matName)
    {
        so.FindProperty(field).objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{matName}.mat");
    }

    // ------------------------------------------------------------------ registration

    private static void WireHubAndBuildSettings()
    {
        // Build Settings: Hub first, then minigames
        var wanted = new[] { HubPath, HubPlatformFallPath, ScenePath };
        var existing = EditorBuildSettings.scenes.ToList();
        foreach (var path in wanted)
            if (!existing.Any(s => s.path == path))
                existing.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = existing.ToArray();

        // Hub scene's MinigameManager list
        var hub = EditorSceneManager.OpenScene(HubPath, OpenSceneMode.Single);
        var manager = UnityEngine.Object.FindFirstObjectByType<MinigameManager>();
        if (manager == null) throw new Exception("No MinigameManager found in the Hub scene.");

        var so = new SerializedObject(manager);
        var list = so.FindProperty("minigameScenes");
        bool present = false;
        for (int i = 0; i < list.arraySize; i++)
            if (list.GetArrayElementAtIndex(i).stringValue == SceneName) present = true;

        if (!present)
        {
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).stringValue = SceneName;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorSceneManager.SaveScene(hub);
        Debug.Log($"[DrunkSwordfightSetup] Hub now lists {list.arraySize} minigame scenes.");
    }
}

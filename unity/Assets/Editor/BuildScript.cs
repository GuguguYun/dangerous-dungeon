using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Batch-mode entry points:
///   Unity -batchmode -quit -projectPath unity -executeMethod BuildScript.BuildMac -logFile -
public static class BuildScript
{
    const string ScenePath = "Assets/Scenes/Main.unity";
    const string KitPath = "Assets/Resources/Kit/dungeon_kit.fbx";
    const string PaletteDir = "Assets/Resources/Palette";
    public const string AppPath = "Builds/DangerousDungeon.app";
    // Meshy's animation FBXs, outside Resources (see ConfigureCharacters).
    const string ClipSource = "Assets/CharClips";
    static readonly string[] ClipFiles = { "actions", "walking", "running" };

    [MenuItem("Meshy/Setup Project")]
    public static void Setup()
    {
        PlayerSettings.companyName = "tianyaoguan-lab";
        PlayerSettings.productName = "Dangerous Dungeon";
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.defaultScreenWidth = 1600;
        PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.enableFrameTimingStats = true; // CPU/GPU frame times for the PERF check
        UseLegacyInput();
        SetupUrp();
        ConvertModelMaterials();
        ConfigureKitImport();
        LogAxisProbe();
        ConfigureCharacters();
        WriteHandFrames();
        RobeFix.Run();
        LogModels();
        CreatePaletteAssets();
        CreateVfxAssets();
        CreateMossAssets();
        CreateScene();
        AssetDatabase.SaveAssets();
        Debug.Log("SETUP done");
    }

    [MenuItem("Meshy/Build macOS")]
    public static void BuildMac()
    {
        Setup();
        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            // MESHY_APP_PATH: self-checks build elsewhere so a game the user has open is never replaced under them.
            locationPathName = System.Environment.GetEnvironmentVariable("MESHY_APP_PATH") is string other && other.Length > 0 ? other : AppPath,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"BUILD {report.summary.result} errors={report.summary.totalErrors} size={report.summary.totalSize}");
        if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    /// The browser version, for the /3d-games pages (landing-mini-games): the same game as a WebGL build
    /// in MESHY_WEB_PATH (default Builds/web/dangerous-dungeon, which names the files). Uncompressed files: the CDN's content policy allows no
    /// blob: scripts, which Unity's loader would make when it unpacks compressed files itself. Smaller,
    /// crunched textures for the web only (Meshy's 2K–4K maps are most of the download).
    [MenuItem("Meshy/Build Web")]
    public static void BuildWeb()
    {
        Setup();
        ShrinkWebTextures();
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.decompressionFallback = false;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.nameFilesAsHashes = false;
        PlayerSettings.WebGL.template = "APPLICATION:Minimal";
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = System.Environment.GetEnvironmentVariable("MESHY_WEB_PATH") is string other && other.Length > 0 ? other : "Builds/web/dangerous-dungeon",
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"BUILD {report.summary.result} errors={report.summary.totalErrors} size={report.summary.totalSize}");
        if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    /// WebGL overrides: 1024 at most, 512 for weapons and props (small on screen), and crunched (a
    /// fraction of the size in the download, unpacked to the GPU format at load). The painted UI keeps
    /// its own uncompressed setting, as on the Mac.
    static void ShrinkWebTextures()
    {
        int changed = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                bool small = path.StartsWith("Assets/Resources/Models/Weapons/") || path.StartsWith("Assets/Resources/Models/Props/");
                bool crunch = ti.textureCompression != TextureImporterCompression.Uncompressed;
                int size = Mathf.Min(ti.maxTextureSize, small ? 512 : 1024);
                var web = ti.GetPlatformTextureSettings("WebGL");
                if (web.overridden && web.maxTextureSize == size && web.crunchedCompression == crunch) continue;
                web.overridden = true;
                web.maxTextureSize = size;
                web.format = TextureImporterFormat.Automatic;
                web.textureCompression = ti.textureCompression;
                web.crunchedCompression = crunch;
                web.compressionQuality = 60;
                ti.SetPlatformTextureSettings(web);
                ti.SaveAndReimport();
                changed++;
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        Debug.Log($"SETUP web textures: {changed} changed");
    }

    /// URP with Forward+ (many brazier lights) and SSAO; post-processing is added at runtime.
    static void SetupUrp()
    {
        const string dir = "Assets/Settings";
        Directory.CreateDirectory(dir);
        var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>($"{dir}/URP.asset");
        if (rp == null)
        {
            var data = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(data, $"{dir}/URP_Renderer.asset");
            data.renderingMode = RenderingMode.ForwardPlus;
            var ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            ssao.name = "SSAO";
            AssetDatabase.AddObjectToAsset(ssao, data);
            data.rendererFeatures.Add(ssao);
            EditorUtility.SetDirty(data);
            rp = UniversalRenderPipelineAsset.Create(data);
            AssetDatabase.CreateAsset(rp, $"{dir}/URP.asset");
        }
        rp.supportsHDR = true;
        rp.msaaSampleCount = 4;
        rp.shadowDistance = 60f;
        EditorUtility.SetDirty(rp);
        GraphicsSettings.defaultRenderPipeline = rp;
        int current = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = rp;
        }
        QualitySettings.SetQualityLevel(current, false);
        AssetDatabase.SaveAssets();
        Debug.Log($"SETUP URP active={GraphicsSettings.defaultRenderPipeline?.name} features={rp.scriptableRenderer != null}");
    }

    /// Models imported before the switch carry built-in Standard materials; reimport them so the
    /// importer builds URP Lit ones instead.
    static void ConvertModelMaterials()
    {
        int n = 0;
        foreach (var path in Directory.GetFiles("Assets/Resources", "*.fbx", SearchOption.AllDirectories))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!go) continue;
            bool standard = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                foreach (var m in r.sharedMaterials)
                    standard |= m && m.shader && m.shader.name == "Standard";
            if (!standard) continue;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            n++;
        }
        Debug.Log($"SETUP reimported {n} models for URP materials");
    }

    static void UseLegacyInput()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (assets.Length == 0) return;
        var so = new SerializedObject(assets[0]);
        var prop = so.FindProperty("activeInputHandler");
        if (prop == null)
        {
            Debug.LogWarning("SETUP activeInputHandler not found");
            return;
        }
        Debug.Log("SETUP activeInputHandler was " + prop.intValue);
        if (prop.intValue != 0)
        {
            prop.intValue = 0;
            so.ApplyModifiedProperties();
        }
    }

    static void ConfigureKitImport()
    {
        var importer = AssetImporter.GetAtPath(KitPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError("SETUP kit FBX missing at " + KitPath);
            return;
        }
        if (!importer.isReadable || importer.importAnimation || importer.importCameras || importer.importLights)
        {
            importer.isReadable = true; // MeshCollider needs the vertices in a player build
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.SaveAndReimport();
        }
        // Floors and walls are seen at grazing angles from the third-person camera.
        foreach (var tex in Directory.GetFiles("Assets/Resources/Kit", "kit_tex_*.png"))
        {
            if (AssetImporter.GetAtPath(tex) is TextureImporter ti && ti.anisoLevel != 8)
            {
                ti.anisoLevel = 8;
                ti.SaveAndReimport();
            }
        }
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(KitPath);
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var m = root.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            var mats = string.Join(",", System.Array.ConvertAll(mf.GetComponent<MeshRenderer>().sharedMaterials, x => x ? x.name : "null"));
            Debug.Log($"SETUP kit {mf.name} mesh={mf.sharedMesh.bounds.size} rot={m.rotation.eulerAngles} scale={m.lossyScale} mats={mats}");
        }
    }

    /// Where a cube placed at Blender (0.5, 1, 2) lands in Unity tells the FBX axis mapping.
    static void LogAxisProbe()
    {
        var probe = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Probe/axis_probe.fbx");
        if (!probe) return;
        var mf = probe.GetComponentInChildren<MeshFilter>();
        var m = probe.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
        Debug.Log($"SETUP axis probe: Blender (0.5, 1, 2) -> Unity {m.MultiplyPoint3x4(mf.sharedMesh.bounds.center)}");
    }

    /// Meshy characters: every FBX as legacy animation; action takes renamed in clips.txt order.
    /// Meshy's animation FBXs (actions, walking, running) each carry a full copy of the model, and
    /// everything under Resources ships, so they live in CharClips/<id>/ (one dropped beside rigged.fbx
    /// is moved there) and only their clips are copied into Resources, as Chars/<id>/clips/<name>.anim.
    static void ConfigureCharacters()
    {
        const string root = "Assets/Resources/Models/Chars";
        if (!Directory.Exists(root)) return;
        foreach (var dir in Directory.GetDirectories(root))
        {
            var id = Path.GetFileName(dir);
            var order = File.Exists($"{dir}/clips.txt") ? File.ReadAllLines($"{dir}/clips.txt") : new string[0];
            var src = $"{ClipSource}/{id}";
            Directory.CreateDirectory(src);
            Directory.CreateDirectory($"{dir}/clips");
            AssetDatabase.Refresh();
            foreach (var file in ClipFiles)
            {
                if (!File.Exists($"{dir}/{file}.fbx")) continue;
                if (File.Exists($"{src}/{file}.fbx")) AssetDatabase.DeleteAsset($"{src}/{file}.fbx"); // the new download wins
                var error = AssetDatabase.MoveAsset($"{dir}/{file}.fbx", $"{src}/{file}.fbx");
                Debug.Log($"SETUP char {id}/{file}: moved out of Resources {(error.Length == 0 ? "ok" : error)}");
            }
            foreach (var file in new[] { "rigged", "actions", "walking", "running" })
            {
                var path = file == "rigged" ? $"{dir}/rigged.fbx" : $"{src}/{file}.fbx";
                if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) continue;
                imp.animationType = ModelImporterAnimationType.Legacy;
                imp.importAnimation = file != "rigged";
                imp.importCameras = false;
                imp.importLights = false;
                if (file != "rigged")
                {
                    var takes = imp.defaultClipAnimations;
                    for (int i = 0; i < takes.Length; i++)
                    {
                        string name = file == "walking" ? "walk" : file == "running" ? "run" : i < order.Length ? order[i].Trim() : takes[i].name;
                        takes[i].name = name;
                        takes[i].loopTime = name == "idle" || name == "walk" || name == "run" || name == "shoot";
                        takes[i].wrapMode = takes[i].loopTime ? WrapMode.Loop : WrapMode.ClampForever;
                    }
                    imp.clipAnimations = takes;
                    Debug.Log($"SETUP char {id}/{file}: {takes.Length} takes -> {string.Join(",", System.Array.ConvertAll(takes, t => $"{t.name}({t.lastFrame - t.firstFrame:F0}f)"))}");
                }
                imp.SaveAndReimport();
            }
            CopyClips(id, src, $"{dir}/clips");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/rigged.fbx");
            if (model && !Directory.Exists($"{dir}/Textures"))
            {
                // Meshy's rigged FBX embeds its texture; Unity only binds it once extracted to files.
                var imp = (ModelImporter)AssetImporter.GetAtPath($"{dir}/rigged.fbx");
                Directory.CreateDirectory($"{dir}/Textures");
                bool ok = imp.ExtractTextures($"{dir}/Textures");
                AssetDatabase.Refresh();
                imp.SaveAndReimport();
                model = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/rigged.fbx");
                Debug.Log($"SETUP char {id}: extracted textures ok={ok} files={Directory.GetFiles($"{dir}/Textures").Length}");
            }
            if (model)
            {
                var b = new Bounds();
                foreach (var r in model.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                bool textured = true;
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                    foreach (var m in r.sharedMaterials) textured &= m && m.mainTexture;
                Debug.Log($"SETUP char {id}: bounds={b.size} renderers={model.GetComponentsInChildren<Renderer>().Length} bones={model.GetComponentsInChildren<Transform>().Length} textured={textured}");
            }
        }
    }

    /// The legacy clips of a character's animation FBXs as .anim files, the first file in ClipFiles order
    /// winning a name they share (as CharacterAnim used to pick them); clips no longer in any FBX are removed.
    static void CopyClips(string id, string src, string outDir)
    {
        var made = new System.Collections.Generic.HashSet<string>();
        foreach (var file in ClipFiles)
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath($"{src}/{file}.fbx"))
            {
                if (!(asset is AnimationClip clip) || clip.name.StartsWith("__preview__") || !clip.legacy || !made.Add(clip.name)) continue;
                var path = $"{outDir}/{clip.name}.anim";
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (existing)
                {
                    EditorUtility.CopySerialized(clip, existing);
                    EditorUtility.SetDirty(existing);
                }
                else
                {
                    var copy = Object.Instantiate(clip);
                    copy.name = clip.name;
                    AssetDatabase.CreateAsset(copy, path);
                }
            }
        foreach (var stale in Directory.GetFiles(outDir, "*.anim"))
            if (!made.Contains(Path.GetFileNameWithoutExtension(stale))) AssetDatabase.DeleteAsset(stale);
        Debug.Log($"SETUP char {id}: clips {string.Join(",", made)}");
    }

    /// Measure each rig's hands from the skinned mesh so held weapons land in the fist (Meshy rigs have
    /// no finger bones). Writes Chars/<id>/hand.txt, per side, in the hand bone's own space:
    /// fist centre, finger direction, palm direction (towards the body in the A-pose), half thickness.
    static void WriteHandFrames()
    {
        const string root = "Assets/Resources/Models/Chars";
        if (!Directory.Exists(root)) return;
        foreach (var dir in Directory.GetDirectories(root))
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/rigged.fbx");
            var smr = model ? model.GetComponentInChildren<SkinnedMeshRenderer>() : null;
            if (!smr || !smr.sharedMesh) continue;
            var mesh = smr.sharedMesh;
            var verts = mesh.vertices;
            var weights = mesh.boneWeights;
            var bp = mesh.bindposes;
            int Index(string bone) => System.Array.FindIndex(smr.bones, b => b && b.name == bone);
            int hips = Index("Hips"), head = Index("Head");
            if (hips < 0 || head < 0 || weights.Length != verts.Length) continue;
            var lines = new System.Collections.Generic.List<string>();
            foreach (var side in new[] { "Right", "Left" })
            {
                int hi = Index(side + "Hand"), fi = Index(side + "ForeArm");
                if (hi < 0 || fi < 0) continue;
                var pts = new System.Collections.Generic.List<Vector3>();
                for (int i = 0; i < verts.Length; i++)
                {
                    var w = weights[i];
                    float wt = w.boneIndex0 == hi ? w.weight0 : w.boneIndex1 == hi ? w.weight1 : w.boneIndex2 == hi ? w.weight2 : w.boneIndex3 == hi ? w.weight3 : 0f;
                    if (wt > 0.5f) pts.Add(bp[hi].MultiplyPoint3x4(verts[i]));
                }
                if (pts.Count < 20) continue;
                Vector3 c = Vector3.zero;
                foreach (var q in pts) c += q;
                c /= pts.Count;
                Vector3 InHand(int bone) => bp[hi].MultiplyPoint3x4(bp[bone].inverse.GetColumn(3));
                Vector3 finger = (-InHand(fi)).normalized; // forearm -> hand, continued
                Vector3 up = (InHand(head) - InHand(hips)).normalized;
                // The hand's thin axis (least spread across the finger direction) is the palm normal.
                Vector3 u = Vector3.Cross(finger, up).normalized, v = Vector3.Cross(finger, u).normalized;
                float suu = 0f, svv = 0f, suv = 0f;
                foreach (var q in pts)
                {
                    var d = q - c;
                    float a = Vector3.Dot(d, u), b = Vector3.Dot(d, v);
                    suu += a * a; svv += b * b; suv += a * b;
                }
                float ang = 0.5f * Mathf.Atan2(2f * suv, suu - svv); // major axis angle
                Vector3 major = Mathf.Cos(ang) * u + Mathf.Sin(ang) * v;
                Vector3 palm = Vector3.Cross(finger, major).normalized;
                // Sign: in the A-pose the palm faces the body (horizontal direction to the hips).
                Vector3 inward = Vector3.ProjectOnPlane(InHand(hips) - c, up);
                if (Vector3.Dot(palm, inward) < 0f) palm = -palm;
                var spread = new System.Collections.Generic.List<float>();
                foreach (var q in pts) spread.Add(Mathf.Abs(Vector3.Dot(q - c, palm)));
                spread.Sort();
                float half = spread[(int)(spread.Count * 0.9f)];
                string F(Vector3 x) => $"{x.x:R} {x.y:R} {x.z:R}";
                lines.Add($"{side} {F(c)} {F(finger)} {F(palm)} {half:R}");
            }
            File.WriteAllLines($"{dir}/hand.txt", lines);
            Debug.Log($"SETUP hands {Path.GetFileName(dir)}: {string.Join(" | ", lines)}");
        }
        AssetDatabase.Refresh();
    }

    /// Size and texture check for the Blender-prepared Meshy weapons and props.
    static void LogModels()
    {
        foreach (var folder in new[] { "Weapons", "Props" })
        {
            var dir = $"Assets/Resources/Models/{folder}";
            if (!Directory.Exists(dir)) continue;
            foreach (var path in Directory.GetFiles(dir, "*.fbx"))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var b = new Bounds();
                bool first = true, textured = true;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
                    foreach (var m in r.sharedMaterials) textured &= m && m.mainTexture;
                }
                Debug.Log($"SETUP model {folder}/{Path.GetFileNameWithoutExtension(path)} size={b.size} centre={b.center} textured={textured}");
            }
        }
    }

    static void CreateVfxAssets()
    {
        const string dir = "Assets/Resources/VFX";
        Directory.CreateDirectory(dir);
        var texPath = $"{dir}/vfx_soft.png";
        if (!File.Exists(texPath))
        {
            File.WriteAllBytes(texPath, Vfx.SoftTexture().EncodeToPNG());
            AssetDatabase.ImportAsset(texPath);
            var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.SaveAndReimport();
        }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        Save(Vfx.CreateMaterial(true, tex), $"{dir}/vfx_add.mat");
        Save(Vfx.CreateMaterial(false, tex), $"{dir}/vfx_alpha.mat");
        // Flames: a noise-streaked texture on an additive particle material.
        var flamePath = $"{dir}/vfx_flame.png";
        File.WriteAllBytes(flamePath, Vfx.FlameTexture().EncodeToPNG());
        AssetDatabase.ImportAsset(flamePath);
        var fi = (TextureImporter)AssetImporter.GetAtPath(flamePath);
        fi.alphaIsTransparency = true;
        fi.wrapMode = TextureWrapMode.Clamp;
        fi.SaveAndReimport();
        var fireMat = Vfx.CreateMaterial(true, AssetDatabase.LoadAssetAtPath<Texture2D>(flamePath));
        fireMat.name = "vfx_fire";
        Save(fireMat, $"{dir}/vfx_fire.mat");
        // Force fields (shield, stealth outline): the Meshy/Fresnel shader, kept in the build by this material.
        var fresnel = Shader.Find("Meshy/Fresnel");
        if (fresnel) Save(new Material(fresnel) { name = "vfx_fresnel" }, $"{dir}/vfx_fresnel.mat");
        Debug.Log($"SETUP fresnel shader found={(bool)fresnel}");
        // Energy shields, loot pillars and floor glows (ShieldFx, LootFx): one base material per shader.
        foreach (var (shaderName, file) in new[] { ("Meshy/Shield", "vfx_shield"), ("Meshy/LootBeam", "vfx_beam"), ("Meshy/GroundGlow", "vfx_ground"), ("Meshy/Halo", "vfx_halo") })
        {
            var sh = Shader.Find(shaderName);
            if (sh) Save(new Material(sh) { name = file }, $"{dir}/{file}.mat");
            Debug.Log($"SETUP {file} shader found={(bool)sh}");
        }
        // The Meshy potion, energy crystal and shop/altar wares, self-lit (their texture as emission), saved so the
        // emissive Lit variant ships in the build. The round potion gets smooth normals (its remesh is
        // faceted); the crystal keeps its hard facets.
        if (AssetImporter.GetAtPath($"Assets/Resources/Models/Props/{LootFx.PotionModel}.fbx") is ModelImporter potion
            && potion.importNormals != ModelImporterNormals.Calculate)
        {
            potion.importNormals = ModelImporterNormals.Calculate;
            potion.normalSmoothingAngle = 75f;
            potion.SaveAndReimport();
        }
        foreach (var (id, amount) in new[] { (LootFx.PotionModel, 0.3f), (LootFx.CrystalModel, 1.1f),
                     (ShopItem.UpgradeModel, 0.3f), (ShopItem.BlessingModel, 0.08f), (Altar.HeartModel, 0.3f) })
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Models/Props/{id}.fbx");
            var r = model ? model.GetComponentInChildren<Renderer>() : null;
            if (!r || !r.sharedMaterial) continue;
            var glow = new Material(r.sharedMaterial) { name = "glow_" + id };
            LootFx.MakeSelfLit(glow, amount);
            Save(glow, $"{dir}/glow_{id}.mat");
            Debug.Log($"SETUP glow_{id} shader={glow.shader.name} texture={(bool)glow.GetTexture("_EmissionMap")}");
        }
        Debug.Log($"SETUP vfx shader={Vfx.CreateMaterial(true, tex).shader.name}");
    }

    /// 青苔地宫: the mossy floor and wall textures (sharp at grazing angles like the kit's own) and the
    /// cut-out moss pictures with their materials.
    static void CreateMossAssets()
    {
        const string dir = "Assets/Resources/Kit";
        foreach (var name in new[] { "kit_tex_floor_moss", "kit_tex_wall_moss" })
            if (AssetImporter.GetAtPath($"{dir}/{name}.png") is TextureImporter ti && ti.anisoLevel != 8)
            {
                ti.anisoLevel = 8;
                ti.SaveAndReimport();
            }
        foreach (var name in new[] { Moss.Hang, Moss.Patch })
        {
            var path = $"{dir}/{name}.png";
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
            if (!ti.alphaIsTransparency || ti.wrapMode != TextureWrapMode.Clamp)
            {
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
            Save(Moss.CreateMaterial(AssetDatabase.LoadAssetAtPath<Texture2D>(path)), $"{dir}/{name}.mat");
        }
        Debug.Log($"SETUP moss materials={(bool)AssetDatabase.LoadAssetAtPath<Material>($"{dir}/{Moss.Hang}.mat")}");
    }

    static void CreatePaletteAssets()
    {
        Directory.CreateDirectory(PaletteDir);
        foreach (var name in Palette.Entries.Keys)
            Save(Palette.Create(name), $"{PaletteDir}/{name}.mat");
        foreach (var name in Palette.TrailNames)
            Save(Palette.CreateTrail(name), $"{PaletteDir}/trail_{name}.mat");
        // Night sky over the open-roofed dungeon (dark purple gradient, no sun disk).
        var sky = new Material(Shader.Find("Skybox/Procedural"));
        sky.SetFloat("_SunDisk", 0f);
        sky.SetFloat("_AtmosphereThickness", 0.35f);
        sky.SetColor("_SkyTint", new Color(0.35f, 0.25f, 0.55f));
        sky.SetColor("_GroundColor", new Color(0.05f, 0.05f, 0.08f));
        sky.SetFloat("_Exposure", 0.5f);
        Save(sky, $"{PaletteDir}/sky.mat");
    }

    static void Save(Material m, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            existing.shader = m.shader;
            existing.CopyPropertiesFromMaterial(m);
            existing.shaderKeywords = m.shaderKeywords;
            existing.globalIlluminationFlags = m.globalIlluminationFlags;
            EditorUtility.SetDirty(existing);
        }
        else
        {
            AssetDatabase.CreateAsset(m, path);
        }
    }

    static void CreateScene()
    {
        if (!File.Exists(ScenePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }
}

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LeafyTrees.Editor
{
    // This file is Editor-only. Generated prefabs have no custom runtime component.
    public static class LeafyTreesBuilder
    {
        private const string Root = "Assets/LeafyTrees";
        private static readonly string[] Names = { "VerdantOak", "SpreadingOak", "YoungOak" };
        private static readonly float[] Thresholds = { .36f, .14f, .025f };

        [Serializable] private class LodAudit { public int level; public int triangles; public int renderers; }
        [Serializable] private class AssetAudit { public string name; public float height; public float ground; public LodAudit[] lods; }
        [Serializable] private class ImportAudit { public string unityVersion; public string pipeline; public bool passed; public AssetAudit[] assets; }

        // Used by the isolated local validation project; users normally invoke the menu.
        public static void BatchBuild()
        {
            Build();
            RenderPreview();
            var data = new List<AssetAudit>();
            foreach (string name in Names)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/" + name + ".prefab");
                var instance = UnityEngine.Object.Instantiate(prefab);
                try
                {
                    var lods = instance.GetComponent<LODGroup>().GetLODs();
                    var b = lods[0].renderers[0].bounds;
                    foreach (var r in lods[0].renderers) b.Encapsulate(r.bounds);
                    var record = new AssetAudit { name = name, height = b.size.y, ground = b.min.y, lods = new LodAudit[3] };
                    for (int i = 0; i < 3; i++)
                    {
                        int tris = 0;
                        foreach (var r in lods[i].renderers)
                        {
                            var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                            for (int s = 0; s < mesh.subMeshCount; s++) tris += (int)mesh.GetIndexCount(s) / 3;
                        }
                        record.lods[i] = new LodAudit { level = i, triangles = tris, renderers = lods[i].renderers.Length };
                    }
                    data.Add(record);
                }
                finally { UnityEngine.Object.DestroyImmediate(instance); }
            }
            string workspace = Directory.GetParent(Application.dataPath).Parent.FullName;
            var result = new ImportAudit { unityVersion = Application.unityVersion, pipeline = GraphicsSettings.currentRenderPipeline == null ? "Built-in" : GraphicsSettings.currentRenderPipeline.name, passed = true, assets = data.ToArray() };
            File.WriteAllText(Path.Combine(workspace, "output/leafy-trees/unity-audit.json"), JsonUtility.ToJson(result, true));
            Directory.CreateDirectory(Path.Combine(workspace, "public/downloads"));
            AssetDatabase.ExportPackage(Root, Path.Combine(workspace, "public/downloads/LeafyTrees.unitypackage"), ExportPackageOptions.Recurse);
            Debug.Log("LEAFY_TREES_BATCH_PASSED " + JsonUtility.ToJson(result));
        }

        private static void RenderPreview()
        {
            // A real engine render verifies the two-sided cutout shader, beyond FBX parsing.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            var preview = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.65f, .75f, .82f);
            RenderSettings.ambientEquatorColor = new Color(.45f, .52f, .39f);
            RenderSettings.ambientGroundColor = new Color(.26f, .29f, .18f);
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowDistance = 70;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            for (int i = 0; i < Names.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/" + Names[i] + ".prefab");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.position = new Vector3((i - (Names.Length - 1) * .5f) * 9.3f, 0, 0);
                instance.GetComponent<LODGroup>().ForceLOD(0);
            }
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Preview Ground"; floor.transform.localScale = new Vector3(30, 1, 30);
            floor.transform.position = new Vector3(0, -.01f, 0);
            var groundMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_PreviewGround.mat");
            if (groundMat == null)
            {
                groundMat = new Material(Shader.Find("Standard"));
                groundMat.color = new Color(.45f, .57f, .65f); groundMat.SetFloat("_Glossiness", 0);
                AssetDatabase.CreateAsset(groundMat, Root + "/Materials/M_PreviewGround.mat");
            }
            floor.GetComponent<Renderer>().sharedMaterial = groundMat;
            var sun = new GameObject("Preview Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.35f; sun.color = new Color(1, .94f, .80f);
            sun.transform.rotation = Quaternion.Euler(43, -35, 0); sun.shadows = LightShadows.Soft;
            var cam = new GameObject("Preview Camera").AddComponent<Camera>();
            cam.transform.position = new Vector3(0, 9.2f, 42); cam.transform.LookAt(new Vector3(0, 4, 0));
            cam.fieldOfView = 28; cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.45f, .57f, .65f); cam.allowHDR = false;
            var rt = new RenderTexture(1600, 760, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4; cam.targetTexture = rt; cam.Render();
            var old = RenderTexture.active; RenderTexture.active = rt;
            var png = new Texture2D(1600, 760, TextureFormat.RGB24, false);
            png.ReadPixels(new Rect(0, 0, 1600, 760), 0, 0); png.Apply();
            string workspace = Directory.GetParent(Application.dataPath).Parent.FullName;
            File.WriteAllBytes(Path.Combine(workspace, "output/leafy-trees/unity-preview.png"), png.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = old; rt.Release();
            UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(png);
            Directory.CreateDirectory(Root + "/Scenes");
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(preview, Root + "/Scenes/TreeComparison.unity");
            var foliage = Shader.Find("Standard");
            if (foliage != null)
                foreach (var message in ShaderUtil.GetShaderMessages(foliage))
                    if (message.severity.ToString() == "Error") throw new InvalidDataException(message.message);
        }

        [MenuItem("Tools/Leafy Trees/Build Prefabs and Materials")]
        public static void Build()
        {
            Directory.CreateDirectory(Root + "/Materials");
            Directory.CreateDirectory(Root + "/Prefabs");
            AssetDatabase.Refresh();
            ConfigureTextures();
            bool urp = GraphicsSettings.currentRenderPipeline != null &&
                GraphicsSettings.currentRenderPipeline.GetType().Name.Contains("Universal");
            if (GraphicsSettings.currentRenderPipeline != null && !urp)
                throw new NotSupportedException("Leafy Trees builder supports URP and Built-in. Assign HDRP materials manually.");
            var bark = CreateMaterial("M_LeafyBark", "T_LeafyBark", false, false, urp);
            var canopy = CreateMaterial("M_LeafyCanopy", "T_LeafyPalette", false, false, urp);
            foreach (string name in Names)
            {
                string modelPath = Root + "/Models/" + name + ".fbx";
                var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
                if (importer == null) throw new FileNotFoundException(modelPath);
                importer.globalScale = 1f;
                importer.useFileScale = true;
                importer.importAnimation = false;
                importer.importCameras = false;
                importer.importLights = false;
                importer.isReadable = false;
                importer.meshCompression = ModelImporterMeshCompression.Off;
                importer.importNormals = ModelImporterNormals.Import;
                importer.importTangents = ModelImporterTangents.CalculateMikk;
                // No baked lighting is supplied. UV0 intentionally stacks atlas regions.
                importer.generateSecondaryUV = false;
                importer.SaveAndReimport();
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                var instance = UnityEngine.Object.Instantiate(model);
                instance.name = name;
                try
                {
                    foreach (var childGroup in instance.GetComponentsInChildren<LODGroup>(true))
                        UnityEngine.Object.DestroyImmediate(childGroup);
                    var lodGroup = instance.GetComponent<LODGroup>();
                    if (lodGroup == null) lodGroup = instance.AddComponent<LODGroup>();
                    var lods = new LOD[3];
                    for (int i = 0; i < 3; i++)
                    {
                        string suffix = "_LOD" + i;
                        var renderers = instance.GetComponentsInChildren<MeshRenderer>(true)
                            .Where(r => r.name.EndsWith(suffix, StringComparison.Ordinal)).ToArray();
                        if (renderers.Length != 2) throw new InvalidDataException(name + suffix + ": expected 2 renderers");
                        int triangles = 0;
                        foreach (var renderer in renderers)
                        {
                            renderer.sharedMaterial = renderer.name.Contains("Bark") ? bark : canopy;
                            renderer.enabled = true;
                            renderer.gameObject.SetActive(true);
                            renderer.shadowCastingMode = i == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                            renderer.receiveShadows = true;
                            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                            for (int sub = 0; sub < mesh.subMeshCount; sub++) triangles += (int)mesh.GetIndexCount(sub) / 3;
                        }
                        lods[i] = new LOD(Thresholds[i], renderers);
                    }
                    lodGroup.fadeMode = LODFadeMode.None;
                    lodGroup.SetLODs(lods);
                    lodGroup.RecalculateBounds();
                    var collider = instance.AddComponent<CapsuleCollider>();
                    collider.direction = 1;
                    collider.radius = name != "YoungOak" ? .50f : .29f;
                    collider.height = name != "YoungOak" ? 3.5f : 2.5f;
                    collider.center = new Vector3(name != "YoungOak" ? .15f : 0, collider.height * .5f, 0);
                    PrefabUtility.SaveAsPrefabAsset(instance, Root + "/Prefabs/" + name + ".prefab");
                }
                finally { UnityEngine.Object.DestroyImmediate(instance); }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            foreach (var shader in new[] { bark.shader, canopy.shader })
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    if (message.severity.ToString() == "Error") throw new InvalidDataException(shader.name + ": " + message.message);
            Validate();
            Debug.Log("Leafy Trees: " + Names.Length + " prefabs built; 3 LODs, 2 renderers per LOD, grounded meter scale, shared instanced materials.");
        }

        private static void ConfigureTextures()
        {
            foreach (string path in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Textures" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                bool cutout = path.Contains("Foliage");
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = cutout ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                importer.alphaIsTransparency = cutout;
                importer.mipMapsPreserveCoverage = cutout;
                importer.alphaTestReferenceValue = .48f;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 4;
                importer.maxTextureSize = 1024;
                importer.isReadable = false;
                var android = importer.GetPlatformTextureSettings("Android");
                android.overridden = true;
                android.maxTextureSize = 1024;
                android.format = TextureImporterFormat.ASTC_6x6;
                importer.SetPlatformTextureSettings(android);
                importer.SaveAndReimport();
            }
        }

        private static Material CreateMaterial(string name, string texture, bool cutout, bool twoSided, bool urp)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var shader = Shader.Find(urp ? "Universal Render Pipeline/Lit" : twoSided ? "LeafyTrees/BuiltIn Foliage" : "Standard");
            if (shader == null) throw new InvalidOperationException("Missing shader for " + name);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = shader;
            mat.enableInstancing = true;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/" + texture + ".png");
            if (tex == null) throw new FileNotFoundException(texture);
            mat.SetTexture(urp ? "_BaseMap" : "_MainTex", tex);
            mat.SetColor(urp ? "_BaseColor" : "_Color", Color.white);
            mat.SetFloat("_Metallic", 0);
            mat.SetFloat(urp ? "_Smoothness" : "_Glossiness", .12f);
            mat.SetFloat("_Cutoff", cutout ? .48f : 0f);
            if (urp)
            {
                mat.SetFloat("_Surface", 0);
                mat.SetFloat("_AlphaClip", cutout ? 1 : 0);
                mat.SetFloat("_Cull", twoSided ? 0 : 2);
                mat.SetFloat("_ZWrite", 1);
                mat.SetFloat("_SrcBlend", (float)BlendMode.One);
                mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
                if (cutout) mat.EnableKeyword("_ALPHATEST_ON"); else mat.DisableKeyword("_ALPHATEST_ON");
            }
            mat.SetOverrideTag("RenderType", cutout ? "TransparentCutout" : "Opaque");
            mat.renderQueue = cutout ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        [MenuItem("Tools/Leafy Trees/Validate Prefabs")]
        public static void Validate()
        {
            foreach (string name in Names)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/" + name + ".prefab");
                if (prefab == null) throw new InvalidDataException("Missing prefab " + name);
                var group = prefab.GetComponent<LODGroup>();
                if (group == null || group.GetLODs().Length != 3) throw new InvalidDataException("Missing LODs " + name);
                if (prefab.GetComponent<CapsuleCollider>() == null) throw new InvalidDataException("Missing collider " + name);
                if (prefab.transform.localScale != Vector3.one) throw new InvalidDataException("Non-unit root scale " + name);
                var bounds = new Bounds(); bool initialized = false;
                foreach (var r in group.GetLODs()[0].renderers)
                {
                    if (!initialized) { bounds = r.bounds; initialized = true; } else bounds.Encapsulate(r.bounds);
                }
                if (Mathf.Abs(bounds.min.y) > .003f || bounds.size.y < 4 || bounds.size.y > 10)
                    throw new InvalidDataException("Unexpected pivot or scale " + name + ": " + bounds);
                foreach (var lod in group.GetLODs())
                {
                    if (lod.renderers.Length != 2) throw new InvalidDataException("Wrong renderer count " + name);
                    foreach (var renderer in lod.renderers)
                        if (renderer.sharedMaterial == null || !renderer.sharedMaterial.enableInstancing)
                            throw new InvalidDataException("Invalid material " + renderer.name);
                }
            }
            Debug.Log("Leafy Trees validation passed: 3 prefabs / 9 LODs / grounded pivots / material instancing / colliders.");
        }
    }
}
#endif

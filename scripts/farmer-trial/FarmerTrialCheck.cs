using System;
using System.IO;
using System.Linq;
using GatheringDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FarmerTrialCheck
{
    private const string Folder = "Assets/FarmerTrial/", Key = "FarmerTrial.Check";
    private static int Mode => int.TryParse(Environment.GetEnvironmentVariable("FARMER_MODE"),out int value)?value:0;
    private static bool Preferred => Environment.GetEnvironmentVariable("FARMER_PREFERRED")=="2";
    private static readonly string Output = Environment.GetEnvironmentVariable("FARMER_CAPTURE_DIR")??Path.GetFullPath(Application.dataPath+"/../.."+(Preferred?"/comparison/selected":Mode>0?"/comparison/"+Mode:""));
    private static CarryClearanceCheck clearance;
    private static int lastFrame = -1, frameIndex, count, depositedAt = -1;
    private static bool outbound, inbound;
    private static double deadline;
    private static RenderTexture screen;
    private static Texture2D pixels;
    private static string runtimeError;

    public static void Batch()
    {
        if (!Application.isBatchMode || !Directory.Exists(Folder)) throw new InvalidOperationException("Use the isolated project");
        PlayerSettings.productName = "Gathering Farmer Trial";
        string normalPath = Folder+"Farmer_001_A_Cloths&Stuff_Norm.png";
        var normal = (TextureImporter)AssetImporter.GetAtPath(normalPath);
        if (normal.textureType != TextureImporterType.NormalMap) {normal.textureType=TextureImporterType.NormalMap; normal.SaveAndReimport();}
        var scene = EditorSceneManager.OpenScene("Assets/Gathering/Scenes/GatheringDemo.unity");
        var trial = UnityEngine.Object.FindFirstObjectByType<GatheringWorld>().gameObject.AddComponent<FarmerTrial>();
        if(Preferred)trial.gameObject.AddComponent<MapShadowProfile>();
        trial.model = AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"Famer_001_A.fbx");
        var renderer = trial.model.GetComponentInChildren<SkinnedMeshRenderer>();
        Debug.Log("TRIAL_IMPORT: " + string.Join(",",renderer.sharedMaterials.Select(mat => mat.name))+"; bones="+renderer.bones.Length+"; triangles="+renderer.sharedMesh.triangles.Length/3);
        Material Surface(string name,string texture)
        {
            var path = Folder+name+".mat"; var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) {mat=new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat,path);}
            mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+texture+".png");
            if (!mat.mainTexture) throw new FileNotFoundException(texture);
            mat.SetFloat("_Glossiness",.18f); EditorUtility.SetDirty(mat); return mat;
        }
        trial.face = Surface("Skin", "Male_Face_003_Diff"); trial.clothing = Surface("Workwear", "Farmer_001_A_Cloths&Stuff_Diff");
        trial.clothing.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath)); trial.clothing.EnableKeyword("_NORMALMAP"); trial.clothing.SetFloat("_BumpScale",.55f);
        trial.hair = Surface("HairCutout","HairPacked"); trial.hair.SetFloat("_Mode",1); trial.hair.SetFloat("_Cutoff",.36f);
        trial.hair.SetOverrideTag("RenderType","TransparentCutout"); trial.hair.EnableKeyword("_ALPHATEST_ON"); trial.hair.renderQueue=2450;
        trial.studyMode=Mode; trial.contactBoard=Mode>0&&!Preferred;
        if(Preferred){
            if(Mode!=2)throw new InvalidOperationException("Only select the verified carry variant");
            if(!AssetDatabase.LoadAssetAtPath<SceneAsset>(Folder+"FarmerLegacy.unity")&&!AssetDatabase.CopyAsset(Folder+"FarmerWalk.unity",Folder+"FarmerLegacy.unity"))throw new IOException("Could not preserve legacy trial");
            foreach(int number in new[]{1,2,3}){var studyScene=EditorSceneManager.OpenScene(Folder+"FarmerStudy"+number+".unity",OpenSceneMode.Additive);var setup=studyScene.GetRootGameObjects().SelectMany(obj=>obj.GetComponentsInChildren<FarmerTrial>()).Single();setup.contactBoard=true;EditorSceneManager.MarkSceneDirty(studyScene);EditorSceneManager.SaveScene(studyScene);EditorSceneManager.CloseScene(studyScene,true);}
        }
        EditorSceneManager.SaveScene(scene,Folder+(Mode>0&&!Preferred?"FarmerStudy"+Mode:"FarmerWalk")+".unity"); AssetDatabase.SaveAssets();
        Directory.CreateDirectory(Path.Combine(Output,"frames")); SessionState.SetBool(Key,true); EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod] private static void Resume()
    {
        if (!Application.isBatchMode || !SessionState.GetBool(Key,false)) return;
        deadline = EditorApplication.timeSinceStartup+100; EditorApplication.update+=Check;
        Application.logMessageReceived+=(message,stack,type)=>{if(type==LogType.Exception||type==LogType.Error)runtimeError=message;};
    }

    private static void Capture(Camera camera,string file,Vector3? focus=null)
    {
        if (!screen) {screen=new RenderTexture(960,640,24); pixels=new Texture2D(960,640,TextureFormat.RGB24,false);}
        var oldTarget=camera.targetTexture; var oldActive=RenderTexture.active; var position=camera.transform.position; float size=camera.orthographicSize;
        try
        {
            if (focus.HasValue) {camera.transform.position=focus.Value-camera.transform.forward*30; camera.orthographicSize=2.25f;}
            camera.targetTexture=screen; camera.Render(); RenderTexture.active=screen;
            pixels.ReadPixels(new Rect(0,0,960,640),0,0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(Output,file),file.EndsWith(".jpg")?pixels.EncodeToJPG(90):pixels.EncodeToPNG());
        }
        finally {camera.targetTexture=oldTarget; RenderTexture.active=oldActive; camera.orthographicSize=size; camera.transform.position=position;}
    }

    private static void Check()
    {
        try
        {
            if(runtimeError!=null)throw new InvalidOperationException(runtimeError);
            if (EditorApplication.timeSinceStartup>deadline) throw new TimeoutException("Trial timed out");
            if (!EditorApplication.isPlaying || Time.frameCount==lastFrame) return;
            lastFrame=Time.frameCount; Time.captureFramerate=24;
            var trial=UnityEngine.Object.FindFirstObjectByType<FarmerTrial>(); if (!trial || !trial.Worker || trial.Height==0) return;
            var worker=trial.Worker; var world=trial.GetComponent<GatheringWorld>(); var camera=world.View;
            var focus=worker.transform.position + Vector3.up*.9f;
            if(Mode>=2 && trial.WoodVisible){clearance??=new CarryClearanceCheck(worker.transform);clearance.Sample();}
            if (Mode>0 || lastFrame%2==0) Capture(camera,$"frames/{frameIndex++:D4}.jpg",focus);
            if (!outbound && worker.Phase==WorkerPhase.ToWork && Vector3.Distance(worker.transform.position,world.Home(worker.Id))>6)
            {
                if (worker.CargoCount!=0 || trial.WoodVisible) throw new InvalidOperationException("Visible cargo on outward leg");
                Capture(camera,"outbound.png",focus); Capture(camera,"map-outbound.png"); outbound=true;
            }
            if (!inbound && worker.Phase==WorkerPhase.ToDepot && trial.WoodVisible && Vector3.Distance(worker.transform.position,worker.Source.transform.position)>3.5f)
            {
                var skin=worker.GetComponentInChildren<SkinnedMeshRenderer>(); var mesh=new Mesh(); skin.BakeMesh(mesh);
                var vertical=mesh.vertices.Select(point => skin.transform.TransformPoint(point).y).ToArray(); UnityEngine.Object.Destroy(mesh);
                Debug.Log($"TRIAL_RETURN: feetY={vertical.Min():F3} headY={vertical.Max():F3} agentY={worker.transform.position.y:F3}");
                count=worker.CargoCount; Capture(camera,"return.png",focus); Capture(camera,"map-return.png"); inbound=true;
                if(Mode>=2){var angle=camera.transform.rotation;float size=camera.orthographicSize;var pos=camera.transform.position;
                    try{camera.transform.rotation=worker.transform.rotation*Quaternion.Euler(8,202,0);camera.orthographicSize=.70f;camera.transform.position=focus+Vector3.up*.16f-camera.transform.forward*2.5f;Capture(camera,"hands-front.png");}
                    finally{camera.transform.SetPositionAndRotation(pos,angle);camera.orthographicSize=size;}}
            }
            if (world.CompletedTrips==0) return;
            if (!outbound || !inbound || count<=0 || worker.CargoCount!=0 || trial.WoodVisible || world.Inventory[(int)ResourceKind.Wood]!=count) throw new InvalidOperationException("Delivery mismatch");
            if (depositedAt<0) depositedAt=lastFrame;
            if (lastFrame-depositedAt<24) return;
            if(Mode>=2){Debug.Log($"CARRY_CLEARANCE woodFrames={clearance.Frames} over3mm={clearance.DeepFrames} worst_mm={clearance.Worst*1000:F2} near={clearance.Region} widestHandGap_mm={clearance.WidestHandGap*1000:F2}");if((clearance.DeepFrames>0||clearance.WidestHandGap>.009f) && Environment.GetEnvironmentVariable("FARMER_BASELINE")!="1")throw new InvalidOperationException("Carry fit exceeded the penetration or hand-gap tolerance");}
            if(Mode>0){var study=trial.Study;if(study==null||study.GripFrames==0||Mode>=2&&study.LayersApplied==0||Mode==3&&(study.SurfaceHits==0||study.MaxCorrection<.03f||study.MaxCorrection>.26f))throw new InvalidOperationException($"Invalid study result: IK={study?.MaxCorrection} hits={study?.SurfaceHits}"); Debug.Log($"STUDY_RESULT mode={Mode} gripMean={study.GripError/study.GripFrames:F3}m ozzFrames={study.LayersApplied} boardIKFrames={study.SurfaceHits} maxVerticalIK={study.MaxCorrection:F3}m");}
            Debug.Log($"FARMER_TRIAL_PASS targetHeight={trial.Height:F2} wood={count} trips={world.CompletedTrips} frames={frameIndex}"); Finish(0);
        }
        catch (Exception error) {Debug.LogException(error); Finish(1);}
    }

    private static void Finish(int code) {SessionState.EraseBool(Key); EditorApplication.update-=Check; EditorApplication.Exit(code);}
}

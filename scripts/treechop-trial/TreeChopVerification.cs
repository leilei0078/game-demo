using System;
using System.IO;
using System.Linq;
using GatheringDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TreeChopVerification
{
    const string Key="TreeChopTrial.Active";
    static int last,stage,frames,woodBefore;
    static float best=100,deepest=100;
    static int strikes,lastStrike=-1;
    static string folder;
    static double deadline;
    static GatheringWorker worker;
    static GatheringResource tree;
    static TreeChopTrial chop;
    static Transform load;
    static MeshCollider bark;
    static RenderTexture target;
    static Texture2D image;
    public static void Batch(){if(!Application.isBatchMode)throw new Exception("Isolated batch only");EditorSceneManager.OpenScene("Assets/Gathering/Scenes/GatheringDemo.unity");SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();}
    [InitializeOnLoadMethod] static void Resume(){if(Application.isBatchMode&&SessionState.GetBool(Key,false)){deadline=EditorApplication.timeSinceStartup+150;EditorApplication.update+=Tick;}}
    static void Check(bool valid,string reason){if(!valid)throw new InvalidOperationException(reason);}
    static void Finish(int code){SessionState.EraseBool(Key);EditorApplication.update-=Tick;EditorApplication.Exit(code);}
    static void Tick(){try{
        Check(EditorApplication.timeSinceStartup<deadline,"Timeout at stage "+stage);
        if(!EditorApplication.isPlaying||Time.frameCount<5||Time.frameCount==last)return;last=Time.frameCount;
        var world=UnityEngine.Object.FindFirstObjectByType<GatheringWorld>();if(!world||!world.Ready||world.RoutesSettling)return;
        if(stage==0){
            folder=Environment.GetEnvironmentVariable("CHOP_TRIAL_OUTPUT");Directory.CreateDirectory(Path.Combine(folder,"frames"));
            Directory.CreateDirectory(Path.Combine(folder,"camp-frames"));
            worker=world.Workers.Single(item=>item.Id==1);chop=worker.GetComponent<TreeChopTrial>();Check(chop&&chop.Edge,"Trial rig or axe missing");
            load=worker.GetComponentsInChildren<Transform>(true).Single(part=>part.name=="Wood load");
            tree=world.Resources.First(item=>item.Work==WorkKind.Chop);woodBefore=tree.Stock;
            var trunk=tree.GetComponentsInChildren<MeshFilter>().First(part=>part.name.EndsWith("_Bark",StringComparison.Ordinal));
            bark=trunk.gameObject.AddComponent<MeshCollider>();bark.sharedMesh=trunk.sharedMesh;bark.isTrigger=false;
            Time.captureFramerate=24;Check(worker.Work(tree,once:true).Accepted,"Order rejected");stage=1;return;
        }
        var active=chop.Edge.gameObject.activeInHierarchy;
        Check(!active||(worker.Phase==WorkerPhase.Working&&worker.Source==tree&&worker.BatchCount>0&&!load.gameObject.activeInHierarchy),"Axe outside cutting phase");
        if(active){
            var direction=Vector3.ProjectOnPlane(tree.transform.position-worker.transform.position,Vector3.up).normalized;var end=chop.Edge.position;
            if(bark.Raycast(new Ray(end-direction*1.5f,direction),out var hit,3)){float gap=hit.distance-1.5f;best=Mathf.Min(best,Mathf.Abs(gap));deepest=Mathf.Min(deepest,gap);if(chop.Cycle>.22f&&chop.Cycle<.40f&&Mathf.Abs(gap)<.045f&&(lastStrike<0||frames-lastStrike>18)){strikes++;lastStrike=frames;}if(frames%4==0)Debug.Log($"AXE_CONTACT frame={frames} phase={chop.Cycle:F2} gap={gap:F3}");}
            var cam=world.View;var focus=worker.transform.position+direction*.98f+Vector3.up*1.05f;
            var forward=(Vector3.Cross(Vector3.up,direction)*.94f+direction*.28f+Vector3.down*.24f).normalized;
            Capture(cam,focus-forward*11,Quaternion.LookRotation(forward),1.34f,Path.Combine(folder,"frames",$"{frames:D3}.jpg"));
            Capture(cam,cam.transform.position,cam.transform.rotation,cam.orthographicSize,Path.Combine(folder,"camp-frames",$"{frames:D3}.jpg"));
            if(frames==7)Capture(cam,cam.transform.position,cam.transform.rotation,cam.orthographicSize,Path.Combine(folder,"camp.jpg"));
            frames++;
        }
        if(stage==1&&worker.Phase==WorkerPhase.ToDepot&&worker.CargoCount>0){Check(!active&&load.gameObject.activeInHierarchy&&frames>=28&&worker.CargoCount==6,"Invalid chopping/carry transition");stage=2;}
        if(stage==2&&world.CompletedTrips>0){Check(!active&&world.Inventory[0]==6&&tree.Stock==woodBefore-6,"Wood count mismatch");Check(strikes>=2&&deepest>-.14f,$"Blade contact strikes={strikes} deepest={deepest:F3}");Debug.Log($"TREE_TRIAL_PASS frames={frames} strikes={strikes} closest={best:F3} deepest={deepest:F3} wood=6 trips={world.CompletedTrips}");Finish(0);}
    }catch(Exception error){Debug.LogException(error);Finish(1);}}
    static void Capture(Camera camera,Vector3 position,Quaternion rotation,float zoom,string path){
        if(!target){target=new RenderTexture(960,540,24);image=new Texture2D(960,540,TextureFormat.RGB24,false);}
        var at=camera.transform.position;var rot=camera.transform.rotation;float original=camera.orthographicSize;var dest=camera.targetTexture;var active=RenderTexture.active;
        try{camera.transform.SetPositionAndRotation(position,rotation);camera.orthographicSize=zoom;camera.targetTexture=target;camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToJPG(91));}
        finally{camera.targetTexture=dest;RenderTexture.active=active;camera.transform.SetPositionAndRotation(at,rot);camera.orthographicSize=original;}
    }
}

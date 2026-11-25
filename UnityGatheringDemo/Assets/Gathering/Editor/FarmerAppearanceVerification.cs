using System;
using System.Collections;
using System.IO;
using System.Linq;
using GatheringDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FarmerAppearanceVerification
{
    private const string Key="Gathering.FarmerAppearanceTest";
    private static IEnumerator run;
    private static double deadline;
    private static string error;
    public static void Batch()
    {
        Require(Application.isBatchMode,"Batch only");EditorSceneManager.OpenScene("Assets/Gathering/Scenes/GatheringDemo.unity");
        SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod] private static void Resume()
    {
        if(!Application.isBatchMode||!SessionState.GetBool(Key,false))return;
        deadline=EditorApplication.timeSinceStartup+100;EditorApplication.update+=Tick;
        Application.logMessageReceived+=(message,stack,type)=>{if(type==LogType.Exception||type==LogType.Error)error=message;};
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static void Tick()
    {
        try{
            Require(error==null,error);Require(EditorApplication.timeSinceStartup<deadline,"Farmer test timeout");
            if(!EditorApplication.isPlaying||Time.frameCount<4)return;
            run??=Verify();if(run.MoveNext())return;Finish(0);
        }catch(Exception exception){Debug.LogException(exception);Finish(1);}
    }
    private static void Finish(int code){SessionState.EraseBool(Key);EditorApplication.update-=Tick;EditorApplication.Exit(code);}
    private static IEnumerator Verify()
    {
        var world=UnityEngine.Object.FindFirstObjectByType<GatheringWorld>();while(!world.Ready||world.RoutesSettling)yield return null;
        var worker=world.Workers.Single(item=>item.Id==1);var skin=worker.GetComponentInChildren<SkinnedMeshRenderer>();
        Require(skin&&skin.sharedMaterials.All(mat=>mat&&mat.mainTexture)&&world.Workers.Count==12,"Appearance/textures or roster missing");
        Require(world.Workers.Where(item=>item.Id!=1).All(item=>!item.GetComponent<FarmerAppearance>()&&item.transform.Find("Body").GetComponent<Renderer>().enabled),"Other workers changed");
        Require(worker.Phase==WorkerPhase.Idle&&worker.transform.Find("Body").GetComponent<Collider>().enabled,"Worker not idle or selectable");
        worker.Selected=true;Require(worker.transform.Find("Selection").gameObject.activeSelf,"Selection missing");
        var frame=worker.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Firewood frame");
        var wood=frame.Find("Wood load").gameObject;var marker=worker.transform.Find("Cargo").GetComponent<Renderer>();
        Require(!frame.gameObject.activeSelf&&frame.parent.name=="Char_Farmer_001 Spine3"&&frame.GetComponentsInChildren<Collider>(true).Length==0,"Wood carrier must be a visual attached to the back");
        var saves=world.GetComponent<GatheringSaveService>();string folder=Path.GetFullPath(Application.dataPath+"/../../output/unity-farmer-main");Directory.CreateDirectory(folder);
        if(Resources.Load<GameObject>("Survivor/Survivor_LightOlive")){
            Require(skin.name=="Survivor_Worker_Skin"&&skin.sharedMaterials.Length==12,"Survivor appearance missing");
            folder=Path.GetFullPath(Application.dataPath+"/../../output/unity-survivor-main");Directory.CreateDirectory(folder);
            var baked=new Mesh();skin.BakeMesh(baked);
            var heights=baked.vertices.Select(v=>(skin.transform.position+skin.transform.rotation*v).y).ToArray();UnityEngine.Object.Destroy(baked);
            Require(heights.Max()-heights.Min()>1.5f&&heights.Max()-heights.Min()<1.85f&&Mathf.Abs(heights.Min())<.12f,"Survivor size or ground contact is incorrect");
            Snapshot(world.View,worker.transform.position,Path.Combine(folder,"idle-camp.png"),true);
            Snapshot(world.View,worker.transform.position,Path.Combine(folder,"idle-close.png"));
        }
        string testSave=Path.Combine(folder,"in-transit.json");
        Require(worker.Work(world.Resources.First(item=>item.Work==WorkKind.Chop),once:true).Accepted,"Wood order failed");
        var origin=worker.transform.position;
        while(Vector3.Distance(origin,worker.transform.position)<.5f)yield return null;
        Require(frame.gameObject.activeInHierarchy&&!wood.activeSelf,"Expected an empty frame on the way to gather wood");
        Snapshot(world.View,worker.transform.position,Path.Combine(folder,"walking.png"));
        WoodPortrait(world.View,worker.transform,folder,"wood-frame-empty",new Vector3(-.6f,-.25f,1));
        while(worker.Phase!=WorkerPhase.ToDepot||!wood.activeInHierarchy)yield return null;
        int count=worker.CargoCount;Require(count>0&&!marker.enabled,"Incorrect wood visual");Require(saves.Save(testSave).Accepted,"Temporary save failed");
        var rack=world.GetComponentInChildren<GatheringDepot>().transform.Find("Timber Rack/Timber Storage");
        if(rack)
        {
            var stored=rack.GetComponentsInChildren<MeshFilter>(true);
            var logs=wood.GetComponentsInChildren<MeshFilter>().Where(part=>part.name!="Hemp lashing").ToArray();
            Require(logs.Length==6&&logs.All(log=>stored.Any(item=>item.sharedMesh==log.sharedMesh&&
                item.GetComponent<Renderer>().sharedMaterials.SequenceEqual(log.GetComponent<Renderer>().sharedMaterials))),"Wood carrier should use the warehouse logs and materials");
        }
        var harvestedAt=worker.transform.position;
        while(Vector3.Distance(harvestedAt,worker.transform.position)<1.12f&&worker.Phase==WorkerPhase.ToDepot)yield return null;
        Snapshot(world.View,worker.transform.position,Path.Combine(folder,"farmer-in-game.png"));
        WoodPortrait(world.View,worker.transform,folder,"wood-frame-loaded",new Vector3(-.69f,-.26f,1));
        WoodPortrait(world.View,worker.transform,folder,"wood-frame-front",new Vector3(-.26f,-.16f,-1));
        WoodPortrait(world.View,worker.transform,folder,"wood-frame-side",new Vector3(-1,-.2f,.13f));
        while(world.CompletedTrips<1)yield return null;
        Require(!frame.gameObject.activeInHierarchy&&!wood.activeSelf&&world.Inventory[0]==count,"Unload failed");Require(saves.Load(testSave).Accepted,"Load rejected");
        while(saves.Loading||!world.Ready||!wood.activeInHierarchy)yield return null;
        Require(worker.CargoCount==count&&world.CompletedTrips==0&&worker.GetComponentsInChildren<SkinnedMeshRenderer>().Length==1,"Duplicate or lost restored farmer/cargo");
        while(world.CompletedTrips<1)yield return null;Require(world.Inventory[0]==count&&!wood.activeInHierarchy,"Restored delivery failed");
        Require(worker.Work(world.Resources.First(item=>item.Kind==ResourceKind.Ore),once:true).Accepted,"Ore order failed");
        var basket=worker.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Ore basket").gameObject;
        var stones=basket.transform.Find("Stone load").gameObject;
        if(Resources.Load<GameObject>("WorkerGear/MarketP019/P019_BackBasket")){
            var shell=basket.transform.Find("P019 wicker shell");Require(shell,"P019 shell missing");
            var mat=shell.GetComponent<MeshRenderer>().sharedMaterial;
            Require(mat.mainTexture&&mat.mainTexture.name=="P019_Wicker_diffuse"&&mat.GetTexture("_BumpMap"),"P019 wicker maps missing");
        }
        while(worker.Phase!=WorkerPhase.Working)yield return null;
        Require(basket.activeInHierarchy&&!stones.activeSelf,"Expected empty basket at ore site");
        while(worker.Phase!=WorkerPhase.ToDepot||!stones.activeInHierarchy)yield return null;
        int ore=worker.CargoCount,prior=world.Inventory[(int)ResourceKind.Ore];
        Require(ore>0&&!frame.gameObject.activeSelf&&!marker.enabled&&basket.transform.parent.name=="Char_Farmer_001 Spine3","Loaded stone carrier mismatch");
        string gearFolder=Path.GetFullPath(Application.dataPath+"/../../output/ore-pannier");Directory.CreateDirectory(gearFolder);
        var start=worker.transform.position;while(Vector3.Distance(start,worker.transform.position)<.28f)yield return null;
        var cam=world.View;var rotation=cam.transform.rotation;var at=cam.transform.position;var size=cam.orthographicSize;
        try{
            var forward=worker.transform.rotation*new Vector3(-.72f,-.33f,.93f).normalized;
            cam.transform.rotation=Quaternion.LookRotation(forward,Vector3.up);cam.orthographicSize=1.06f;cam.transform.position=worker.transform.position+Vector3.up*.88f-forward*12;
            Snapshot(cam,worker.transform.position,Path.Combine(gearFolder,"loaded-back.png"),true);
            cam.transform.rotation=Quaternion.LookRotation(worker.transform.rotation*new Vector3(-1,-.26f,-.34f).normalized,Vector3.up);
            cam.transform.position=worker.transform.position+Vector3.up*.88f-cam.transform.forward*12;
            Snapshot(cam,worker.transform.position,Path.Combine(gearFolder,"loaded-side.png"),true);
        }finally{cam.transform.SetPositionAndRotation(at,rotation);cam.orthographicSize=size;}
        var oreSave=Path.Combine(gearFolder,"ore-in-transit.json");Require(saves.Save(oreSave).Accepted,"Ore save failed");
        while(worker.CargoCount>0)yield return null;yield return null;
        Require(world.Inventory[(int)ResourceKind.Ore]==prior+ore&&!stones.activeInHierarchy,"Stone unloading failed");
        var depot=world.GetComponentInChildren<GatheringDepot>();
        var storedStone=depot.GetComponentsInChildren<Transform>(true).Single(item=>item.name=="Stored stone 1");
        Require(storedStone.gameObject.activeInHierarchy,"Delivered ore is missing from the warehouse display");
        Require(saves.Load(oreSave).Accepted,"Ore restore rejected");while(saves.Loading||!world.Ready||!stones.activeInHierarchy)yield return null;
        Require(!marker.enabled&&worker.CargoCount==ore,"Restored stone basket missing");
        while(worker.CargoCount>0)yield return null;Require(world.Inventory[(int)ResourceKind.Ore]==prior+ore,"Restored ore mismatch");
        Debug.Log($"ORE_BASKET_PASS ore={ore} stock={world.Inventory[(int)ResourceKind.Ore]}");
        Debug.Log($"WOOD_FRAME_PASS wood={count} stored={world.Inventory[(int)ResourceKind.Wood]}");
        Debug.Log($"MAIN_FARMER_PASS roster=12 wood={count} restoredWood={world.Inventory[0]} ore={worker.CargoCount}");
    }
    private static void WoodPortrait(Camera camera,Transform worker,string folder,string name,Vector3 angle)
    {
        var position=camera.transform.position;var rotation=camera.transform.rotation;float zoom=camera.orthographicSize;
        try
        {
            var forward=worker.rotation*angle.normalized;
            camera.transform.rotation=Quaternion.LookRotation(forward,Vector3.up);camera.orthographicSize=1.04f;
            camera.transform.position=worker.position+Vector3.up*.91f-forward*12;
            Snapshot(camera,worker.position,Path.Combine(folder,name+".png"),true);
        }
        finally{camera.transform.SetPositionAndRotation(position,rotation);camera.orthographicSize=zoom;}
    }
    private static void Snapshot(Camera camera,Vector3 focus,string path,bool camp=false)
    {
        int width=camp?1600:960,height=camp?900:640;var target=new RenderTexture(width,height,24);var image=new Texture2D(width,height,TextureFormat.RGB24,false);
        var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;var position=camera.transform.position;float zoom=camera.orthographicSize;
        try{if(!camp){camera.transform.position=focus+Vector3.up*.9f-camera.transform.forward*30;camera.orthographicSize=2.5f;}camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
        finally{camera.targetTexture=oldTarget;RenderTexture.active=oldActive;camera.transform.position=position;camera.orthographicSize=zoom;target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(image);}
    }
}

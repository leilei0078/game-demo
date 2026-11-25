using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using GatheringDemo;

public static class ConstructionPolishVerification
{
    [Serializable] private sealed class Report{public string status;public string[] checks;public int[] accounted;public int buildings,bays;}
    private static string outputFile="integrated-verification.json";
    private static IEnumerator run;private static GatheringWorld world;private static readonly List<string> checks=new List<string>();
    public static string Status {get;private set;}="not started";
    private static string DirectoryPath=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../output/unity-polish"));
    private static void Require(bool ok,string text){if(!ok)throw new Exception(text);}
    public static string Start()
    {
        Require(EditorApplication.isPlaying&&run==null,"Start once in Play Mode");world=UnityEngine.Object.FindFirstObjectByType<GatheringWorld>();
        Require(world.Buildings.Count==0&&world.Inventory.Sum()==0,"Use a fresh world");checks.Clear();outputFile="integrated-verification.json";Status="RUNNING";run=Execute();EditorApplication.update+=Tick;return Status;
    }
    public static string RestoreAfterRestart()
    {
        Require(EditorApplication.isPlaying&&run==null,"Start once after reentering Play Mode");world=UnityEngine.Object.FindFirstObjectByType<GatheringWorld>();
        Require(world.Buildings.Count==0,"Restart must begin with a fresh runtime");checks.Clear();outputFile="restart-verification.json";Status="RUNNING";run=Restart();EditorApplication.update+=Tick;return Status;
    }
    private static IEnumerator Restart()
    {
        var service=world.GetComponent<GatheringSaveService>();
        Require(service.Load(Path.Combine(DirectoryPath,"three-buildings-checkpoint.json")).Accepted,"Disk load after restart rejected");while(service.Loading)yield return null;
        Require(world.Paused&&world.Buildings.Count==3&&world.Buildings.All(b=>!b.Complete),"Restart did not restore partial world");
        Require(world.CameraFocus==new Vector3(11,0,6)&&world.View.orthographicSize==13,"Restart camera mismatch");
        checks.Add("fresh runtime restores v2 disk checkpoint with three incomplete sites, camera and carried materials");world.Paused=false;
        var wait=Until(()=>world.Buildings.All(b=>b.Complete),180,"continue three sites after actual Play Mode exit/reentry");while(wait.MoveNext())yield return null;
        Require(world.DepotBayCount==9,"Duplicate/missing depot bays after restart");checks.Add("all three finish after restart, nine bays, per-frame conservation and supply/constructor caps");
        foreach(var worker in world.Workers)worker.FinishTrip();wait=Until(()=>world.Workers.All(w=>w.Phase==WorkerPhase.Idle&&w.CargoCount==0),50,"settle restart demo");while(wait.MoveNext())yield return null;
        world.Paused=true;world.SetCameraFocus(new Vector3(5,0,1));world.View.orthographicSize=14;
    }
    private static void Tick()
    {
        try
        {
            Require(EditorApplication.isPlaying,"Play Mode stopped");
            if(world.Ready)foreach(ResourceKind kind in Enum.GetValues(typeof(ResourceKind)))Require(world.AccountedResources(kind)==world.InitialResources(kind),"Conservation "+kind);
            if(world.Ready)foreach(var b in world.Buildings){Require(b.Constructing<=2,"Builder cap");for(int k=0;k<2;k++)Require(b.Delivered[k]+b.InTransit((ResourceKind)k)<=ConstructionSite.Cost((ResourceKind)k),"Supply cap");}
            if(run.MoveNext())return;Finish("PASS");
        }
        catch(Exception e){Debug.LogError("POLISH "+e);Finish("FAIL: "+e.Message);}
    }
    private static void Finish(string status)
    {
        EditorApplication.update-=Tick;run=null;Status=status;System.IO.Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(Path.Combine(DirectoryPath,outputFile),JsonUtility.ToJson(new Report{status=status,checks=checks.ToArray(),accounted=Enumerable.Range(0,3).Select(k=>world.AccountedResources((ResourceKind)k)).ToArray(),buildings=world.Buildings.Count,bays=world.DepotBayCount},true));
        world.GetComponent<GatheringController>().enabled=true;Debug.Log("POLISH "+status);
    }
    private static IEnumerator Until(Func<bool> condition,float seconds,string title)
    {Status="RUNNING: "+title;float end=Time.realtimeSinceStartup+seconds;while(!condition()){Require(Time.realtimeSinceStartup<end,"Timeout "+title);yield return null;}}
    private static IEnumerator Execute()
    {
        while(world.RoutesSettling)yield return null;
        var controller=world.GetComponent<GatheringController>();controller.enabled=false;
        var initial=world.CaptureSave();
        // Read the real v1 checkpoint without changing the user's file.
        var saves=world.GetComponent<GatheringSaveService>();var old=GatheringSaveStore.Read(saves.SavePath,out _);Require(old.version==1,"Expected the existing v1 fixture");
        Require(saves.Load().Accepted,"Legacy load rejected");while(saves.Loading)yield return null;
        Require(world.CaptureSave().buildings[0].labour==old.buildings[0].labour,"Legacy labour changed");
        Require(world.CameraFocus==Vector3.zero,"Legacy camera default");
        string initialPath=Path.Combine(DirectoryPath,"initial-v2.json");GatheringSaveStore.Write(initialPath,initial);Require(saves.Load(initialPath).Accepted,"Reset failed");while(saves.Loading||world.RoutesSettling)yield return null;
        checks.Add("v1 real user checkpoint loads in expanded world without changing its file; v2 starts clean");
        var points=new[]{new Vector3(11,0,-7),new Vector3(11,0,0),new Vector3(11,0,8)};
        for(int i=0;i<3;i++)
        {
            Require(world.PlaceBuilding(points[i],i,out var site).Accepted,"Three blueprint placement "+i);
            while(world.RoutesSettling)yield return null;
            Require(site.AssignIdle(i==0?1:2).Accepted,"Quick idle assignment");
            Require(site.Assigned==(i==0?1:2),"Wrong idle assignment count");
        }
        Require(world.Buildings.Count==3&&world.Workers.Count(w=>w.BuildTarget)>0,"Planning failed");
        Require(world.Buildings.All(b=>b.SupplyStatus.Contains("仓库还缺")),"Shortage not explained");
        Require(!world.PlaceBuilding(points[0],0,out _).Accepted&&!world.PlaceBuilding(new Vector3(18,0,12),0,out _).Accepted,"Overlap/bounds accepted");
        checks.Add("three expanded-map blueprints, rotated entrances, 1/2 idle assignment, shortage reasons, overlap/bounds rejection");
        // Looking at a site must not steal selected gatherers.
        var gatherers=world.Workers.Where(w=>w.Phase==WorkerPhase.Idle).ToArray();
        for(int i=0;i<gatherers.Length;i++)Require(gatherers[i].Work(world.Resources[i<4?0:2]).Accepted,"Gather order");
        controller.Select(new[]{gatherers[0]});var source=gatherers[0].Source;
        controller.Click(world.View.WorldToScreenPoint(world.Buildings[0].transform.position+Vector3.up*.15f),false);
        Require(gatherers[0].Source==source&&gatherers[0].BuildTarget==null,"Inspecting site reassigned selected gatherer");
        checks.Add("left-click site inspection preserves selected gathering task");
        var wait=Until(()=>world.Buildings.Any(b=>b.Delivered.Sum()>0)&&world.Workers.Any(w=>w.CargoSite),110,"real gather / transit / delivery");while(wait.MoveNext())yield return null;
        Require(world.Buildings.Any(b=>b.SupplyStatus.Contains("运输")||b.InTransit(ResourceKind.Wood)+b.InTransit(ResourceKind.Ore)>0),"Transit not tracked");
        world.SetCameraFocus(new Vector3(11,0,6));world.View.orthographicSize=13;
        world.Paused=true;string checkpoint=Path.Combine(DirectoryPath,"three-buildings-checkpoint.json");Require(saves.Save(checkpoint).Accepted,"v2 save failed");
        var saved=GatheringSaveStore.ReadOne(checkpoint);Require(saved.version>=2&&saved.cameraFocus==world.CameraFocus,"v2 camera not stored");
        Require(saves.Load(checkpoint).Accepted,"v2 load failed");while(saves.Loading)yield return null;
        Require(world.Buildings.Count==3&&world.CameraFocus==saved.cameraFocus&&world.View.orthographicSize==13,"Restored camera/buildings mismatch");
        Require(world.Workers.Sum(w=>w.CargoCount)==saved.workers.Sum(w=>w.count),"Transit lost on load");
        // Paused world may inspect placement after real-time carving settles.
        float settle=Time.realtimeSinceStartup+.65f;while(Time.realtimeSinceStartup<settle)yield return null;Require(!world.RoutesSettling,"Paused road settling never ends");
        world.Paused=false;
        checks.Add("v2 partial transit save/load preserves all three sites, camera, materials, tasks; paused roads settle");
        wait=Until(()=>world.Buildings.All(b=>b.Complete),180,"three sites finish with actual gathered materials");while(wait.MoveNext())yield return null;
        Require(world.DepotBayCount==9,"Completed bays mismatch");
        foreach(var b in world.Buildings)b.Work(100);Require(world.DepotBayCount==9,"Duplicate completion");
        checks.Add("all three finish through real hauling and labour; nine reachable shared bays; completion once; per-frame material/supply/builder caps");
        foreach(var worker in world.Workers)worker.FinishTrip();
        wait=Until(()=>world.Workers.All(w=>w.Phase==WorkerPhase.Idle&&w.CargoCount==0),50,"finish all trips");while(wait.MoveNext())yield return null;
        world.Paused=true;world.SetCameraFocus(new Vector3(5,0,1));world.View.orthographicSize=14;
        saves.Save(Path.Combine(DirectoryPath,"three-buildings-complete.json"));
    }
}

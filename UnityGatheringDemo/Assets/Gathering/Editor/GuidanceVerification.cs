using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using GatheringDemo;

public static class GuidanceVerification
{
    private static string reportName="guidance-verification.json";
    private static IEnumerator run;private static GatheringWorld world;private static GatheringController controller;private static readonly List<string> checks=new List<string>();
    public static string Status {get;private set;}="not started";
    private static string Dir=>Path.GetFullPath(Application.dataPath+"/../../output/unity-guidance");
    [Serializable]private sealed class Report{public string status;public string[] checks;public int[] accounted;}
    private static void Require(bool condition,string text){if(!condition)throw new Exception(text);}
    public static string Start(){Require(EditorApplication.isPlaying&&run==null,"Start in Play Mode");world=UnityEngine.Object.FindFirstObjectByType<GatheringWorld>();controller=world.GetComponent<GatheringController>();checks.Clear();reportName="guidance-verification.json";Status="RUNNING";run=Execute();EditorApplication.update+=Tick;return Status;}
    public static string VerifyConstructionOrder()
    {
        Require(EditorApplication.isPlaying&&run==null,"Start once in Play Mode");world=UnityEngine.Object.FindFirstObjectByType<GatheringWorld>();
        checks.Clear();reportName="construction-order.json";Status="RUNNING";run=ConstructionOrder();EditorApplication.update+=Tick;return Status;
    }
    private static IEnumerator ConstructionOrder()
    {
        var load=Load(Path.Combine(Dir,"priority-transit-v3.json"));while(load.MoveNext())yield return null;
        Require(world.Paused,"Expected paused legacy fixture");var original=world.ConstructionQueue();
        var carrier=world.Workers.First(worker=>worker.CargoSite==original[0]);int cargo=carrier.CargoCount;long task=carrier.TaskId;
        var promoted=original[1];int promotedId=promoted.Id,otherId=original[0].Id;
        Require(world.MoveConstruction(promoted,0).Accepted&&world.ConstructionQueue()[0]==promoted,"Paused move to first");
        Require(promoted.Priority==original[0].Priority&&original[0].AvailableSupply(ResourceKind.Wood)==0,"Manual order within same tier must reserve remaining stock");
        Require(carrier.CargoCount==cargo&&carrier.CargoSite==original[0]&&carrier.TaskId==task,"Reorder redirected in-flight delivery");
        Require(!world.MoveConstruction(promoted,-1).Accepted&&!world.MoveConstruction(promoted,0).Accepted,"Queue boundaries");
        string path=Path.Combine(Dir,"manual-construction-order-v4.json");Require(world.GetComponent<GatheringSaveService>().Save(path).Accepted,"Save queue");
        load=Load(path);while(load.MoveNext())yield return null;
        Require(world.ConstructionQueue().Select(site=>site.Id).SequenceEqual(new[]{promotedId,otherId}),"Queue order lost after disk restore");
        Require(world.Workers.Any(worker=>worker.TaskId==task&&worker.CargoSite&&worker.CargoSite.Id==otherId&&worker.CargoCount==cargo),"In-flight allocation lost after restore");
        checks.Add("paused same-tier manual order takes precedence for remaining warehouse stock; in-flight delivery is unchanged; order and cargo survive disk reload");
    }
    public static string RestoreAfterRestart()
    {Require(EditorApplication.isPlaying&&run==null,"Start in fresh Play Mode");world=UnityEngine.Object.FindFirstObjectByType<GatheringWorld>();Require(world.Buildings.Count==0,"Fresh runtime required");controller=world.GetComponent<GatheringController>();checks.Clear();reportName="restart-verification.json";Status="RUNNING";run=Restart();EditorApplication.update+=Tick;return Status;}
    private static IEnumerator Restart()
    {
        var wait=Load(Path.Combine(Dir,"priority-transit-v3.json"));while(wait.MoveNext())yield return null;
        var high=world.Buildings.Single(b=>b.Priority==2);var low=world.Buildings.Single(b=>b.Priority==1);
        Require(world.GuideHidden&&world.GuideStep==5&&high.InTransit(ResourceKind.Wood)>0,"v3 restart guide/transit mismatch");
        world.Paused=false;wait=Until(()=>high.Complete,120,"v3 priority checkpoint after Play exit/reentry");while(wait.MoveNext())yield return null;
        Require(low.Delivered.Sum()==0&&low.InTransit(ResourceKind.Wood)+low.InTransit(ResourceKind.Ore)==0,"Restart lost priority allocation");
        world.Paused=true;checks.Add("actual Play Mode exit/reentry restores v3 priority, hidden guide step, physical transit; high completes before normal; conservation maintained");
        wait=Load(Path.Combine(Dir,"guide-complete-v3.json"));while(wait.MoveNext())yield return null;
        Require(world.GuideStep==7&&world.GuideHidden&&world.Buildings.Single().Complete,"Completed guide lost after restart");
        checks.Add("completed guide remains completed/hidden after disk restore in fresh runtime");
    }
    private static void Tick()
    {
        try
        {
            if(world.Ready){foreach(ResourceKind kind in Enum.GetValues(typeof(ResourceKind)))Require(world.AccountedResources(kind)==world.InitialResources(kind),"Conservation "+kind);foreach(var b in world.Buildings)for(int k=0;k<2;k++)Require(b.Delivered[k]+b.InTransit((ResourceKind)k)<=ConstructionSite.Cost((ResourceKind)k),"Oversupply");}
            if(run.MoveNext())return;Finish("PASS");
        }
        catch(Exception e){Debug.LogError("GUIDANCE "+e);Finish("FAIL: "+e.Message);}
    }
    private static void Finish(string status){EditorApplication.update-=Tick;run=null;Status=status;Directory.CreateDirectory(Dir);File.WriteAllText(Path.Combine(Dir,reportName),JsonUtility.ToJson(new Report{status=status,checks=checks.ToArray(),accounted=Enumerable.Range(0,3).Select(k=>world.AccountedResources((ResourceKind)k)).ToArray()},true));Debug.Log("GUIDANCE "+status);}
    private static IEnumerator Until(Func<bool> condition,float seconds,string label){Status="RUNNING: "+label;float start=world.Elapsed,watch=Time.realtimeSinceStartup;while(!condition()){Require(world.Elapsed-start<seconds&&Time.realtimeSinceStartup-watch<seconds*3,"Timeout "+label);yield return null;}}
    private static IEnumerator Load(string path){var save=world.GetComponent<GatheringSaveService>();Require(save.Load(path).Accepted,"Load "+path);while(save.Loading||world.RoutesSettling)yield return null;}
    private static IEnumerator Frames(int n=3){int end=Time.frameCount+n;while(Time.frameCount<end)yield return null;}
    private static IEnumerator Execute()
    {
        var wait=Load(Path.GetFullPath(Application.dataPath+"/../../output/unity-polish/initial-v2.json"));while(wait.MoveNext())yield return null;
        world.Paused=true;controller.Select(Array.Empty<GatheringWorker>());wait=Frames();while(wait.MoveNext())yield return null;Require(controller.GuideStage==0,"Guide initial step");
        controller.Select(new[]{world.Workers[0]});wait=Frames();while(wait.MoveNext())yield return null;Require(controller.GuideStage==1,"Guide select");
        Require(controller.FocusMaterial(ResourceKind.Wood).Accepted&&world.Workers.All(w=>w.Phase==WorkerPhase.Idle),"Locate unexpectedly assigned tasks");
        world.Paused=false;for(int i=0;i<3;i++)Require(world.Workers[i].Work(world.Resources[0],false,true).Accepted,"Wood assignment");
        wait=Until(()=>world.Inventory[0]>=18,70,"guide actual wood delivery");while(wait.MoveNext())yield return null;
        wait=Frames();while(wait.MoveNext())yield return null;Require(controller.GuideStage==2,"Guide wood not advanced");
        Require(world.Workers[3].Work(world.Resources[2],false,true).Accepted,"Ore assignment");
        wait=Until(()=>world.Inventory[1]>=6,60,"guide actual ore delivery");while(wait.MoveNext())yield return null;
        wait=Frames();while(wait.MoveNext())yield return null;Require(controller.GuideStage==3,"Guide ore not advanced");checks.Add("guide select -> actual wood18 delivery -> actual ore6 delivery; locate changes camera without task assignment");
        Require(world.PlaceBuilding(new Vector3(11,0,-7),0,out var tutorial).Accepted,"Guide blueprint");while(world.RoutesSettling)yield return null;
        Require(controller.GuideStage==4,"Guide place");Require(tutorial.AssignIdle(2).Accepted,"Guide dispatch");wait=Frames();while(wait.MoveNext())yield return null;Require(controller.GuideStage==5,"Guide assigned");
        wait=Until(()=>tutorial.Complete,100,"guide build completion");while(wait.MoveNext())yield return null;wait=Frames();while(wait.MoveNext())yield return null;Require(controller.GuideStage==6,"Guide complete");
        world.Paused=true;world.GuideHidden=true;string guidePath=Path.Combine(Dir,"guide-complete-v3.json");Require(world.GetComponent<GatheringSaveService>().Save(guidePath).Accepted&&controller.GuideStage==7,"Guide save did not complete");
        wait=Load(guidePath);while(wait.MoveNext())yield return null;Require(world.GuideStep==7&&world.GuideHidden&&world.Buildings[0].Complete,"Guide restore");checks.Add("guide blueprint -> dispatch -> physical construction -> save; v3 retains completed/hidden guide");
        wait=Load(Path.GetFullPath(Application.dataPath+"/../../output/unity-polish/initial-v2.json"));while(wait.MoveNext())yield return null;
        Require(!world.GuideHidden&&world.GuideStep<4,"v2 default guide");
        // Stock comes from real one-trip gathering; only one site's materials are available.
        for(int i=0;i<4;i++)Require(world.Workers[i].Work(world.Resources[i<3?0:2],false,true).Accepted,"Priority gathering");
        wait=Until(()=>world.Inventory[0]==18&&world.Inventory[1]==6&&world.Workers.All(w=>w.CargoCount==0&&w.Phase==WorkerPhase.Idle),80,"priority finite stock");while(wait.MoveNext())yield return null;
        Require(world.PlaceBuilding(new Vector3(11,0,-7),0,out var low).Accepted,"Low site");while(world.RoutesSettling)yield return null;
        Require(world.PlaceBuilding(new Vector3(11,0,0),1,out var high).Accepted,"High site");while(world.RoutesSettling)yield return null;
        Require(low.AssignIdle(1).Accepted&&high.SetPriority(2).Accepted,"Priority setup");
        Require(low.AvailableSupply(ResourceKind.Wood)==18,"Unstaffed priority site hoards stock");Require(high.AssignIdle(1).Accepted,"High dispatch");
        Require(low.AvailableSupply(ResourceKind.Wood)==0&&high.AvailableSupply(ResourceKind.Wood)==18,"Priority allocation");
        high.TogglePause();Require(low.AvailableSupply(ResourceKind.Wood)==18,"Paused high site hoards stock");high.TogglePause();
        Require(!high.SetPriority(-1).Accepted&&!high.SetPriority(3).Accepted,"Invalid priority accepted");
        checks.Add("unstaffed/paused high site releases reservation; staffed high site reserves finite stock; invalid priority rejected");
        wait=Until(()=>high.InTransit(ResourceKind.Wood)>0,50,"high-priority dispatch");while(wait.MoveNext())yield return null;
        int inTransit=high.InTransit(ResourceKind.Wood);Require(low.SetPriority(2).Accepted&&high.SetPriority(0).Accepted,"Live reorder");
        Require(high.InTransit(ResourceKind.Wood)==inTransit,"Reorder stole in-transit materials");
        high.SetPriority(2);low.SetPriority(1);
        world.Paused=true;world.GuideHidden=true;string priorityPath=Path.Combine(Dir,"priority-transit-v3.json");Require(world.GetComponent<GatheringSaveService>().Save(priorityPath).Accepted,"Priority save");
        var saved=GatheringSaveStore.ReadOne(priorityPath);Require(saved.version==3&&saved.buildings.Single(b=>b.id==high.Id).priority==2,"Priority missing from v3");
        var invalid=JsonUtility.FromJson<GatheringSaveData>(JsonUtility.ToJson(saved));invalid.buildings[0].priority=9;bool rejected=false;try{invalid.Validate();}catch(InvalidOperationException){rejected=true;}Require(rejected,"Invalid save priority accepted");
        wait=Load(priorityPath);while(wait.MoveNext())yield return null;low=world.Buildings.Single(b=>b.Id==low.Id);high=world.Buildings.Single(b=>b.Id==high.Id);
        Require(high.Priority==2&&low.Priority==1&&high.InTransit(ResourceKind.Wood)==inTransit,"Priority transit restore");
        world.Paused=false;wait=Until(()=>high.Complete,110,"high finishes first");while(wait.MoveNext())yield return null;
        Require(low.Delivered.Sum()==0&&low.InTransit(ResourceKind.Wood)+low.InTransit(ResourceKind.Ore)==0,"Low priority consumed protected stock");
        checks.Add("live priority changes preserve transit; v3 priority/transit load and invalid-save rejection; actual high site finishes while low receives no finite stock");
        world.Paused=true;var v1=GatheringSaveStore.ReadOne(world.GetComponent<GatheringSaveService>().SavePath);Require(v1.version==1,"User legacy fixture changed");wait=Load(world.GetComponent<GatheringSaveService>().SavePath);while(wait.MoveNext())yield return null;
        Require(world.Buildings.All(b=>b.Priority==1),"v1 priority migration default");checks.Add("real v1 user slot restores normal priority; v2 fixture restores default guide; neither legacy file overwritten");
        wait=Load(priorityPath);while(wait.MoveNext())yield return null;world.SetCameraFocus(new Vector3(5,0,0));world.View.orthographicSize=13;world.GuideHidden=false;
    }
}

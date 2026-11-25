using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using GatheringDemo;

public static class InfrastructureVerification
{
    private static IEnumerator run;
    private static GatheringWorld world;
    private static readonly List<string> checks=new List<string>();
    private static float groundSeconds,roadSeconds;
    private static string output="verification.json";
    public static string Status {get;private set;}="not started";
    private static string Dir=>Path.GetFullPath(Application.dataPath+"/../../output/unity-infrastructure");
    [Serializable] private sealed class Report {public string status;public string[] checks;public float groundSeconds,roadSeconds;public int[] accounted;}
    private static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    public static string Start()
    {
        Require(EditorApplication.isPlaying&&run==null,"Start once in Play Mode");world=UnityEngine.Object.FindFirstObjectByType<GatheringWorld>();
        checks.Clear();output="verification.json";Status="RUNNING";run=Execute();EditorApplication.update+=Tick;return Status;
    }
    public static string Restart()
    {
        Require(EditorApplication.isPlaying&&run==null,"Start after actual restart");world=UnityEngine.Object.FindFirstObjectByType<GatheringWorld>();
        checks.Clear();output="restart-verification.json";Status="RUNNING";run=RestartRun();EditorApplication.update+=Tick;return Status;
    }
    public static string VerifyAutoDispatch()
    {
        Require(EditorApplication.isPlaying&&run==null,"Start once in Play Mode");world=UnityEngine.Object.FindFirstObjectByType<GatheringWorld>();
        checks.Clear();output="auto-dispatch.json";Status="RUNNING";run=AutoDispatch();EditorApplication.update+=Tick;return Status;
    }
    private static IEnumerator AutoDispatch()
    {
        var load=Load(Path.GetFullPath(Application.dataPath+"/../../output/unity-polish/initial-v2.json"));while(load.MoveNext())yield return null;
        var controller=world.GetComponent<GatheringController>();controller.enabled=true;world.Paused=false;world.GuideHidden=true;
        var center=new Vector3(11,0,-7);world.SetCameraFocus(center);world.View.orthographicSize=9;
        var wood=world.Resources.First(resource=>resource.Work==WorkKind.Chop);
        foreach(var worker in world.Workers)Require(worker.Work(wood).Accepted,"Gathering fixture");
        var taskIds=world.Workers.Select(worker=>worker.TaskId).ToArray();
        controller.BeginPlacement();yield return null;
        var point=(Vector2)world.View.WorldToScreenPoint(center);var mouse=Send(point);while(mouse.MoveNext())yield return null;
        mouse=Send(point,1);while(mouse.MoveNext())yield return null;mouse=Send(point);while(mouse.MoveNext())yield return null;
        var site=world.Buildings.Single();float deadline=world.Elapsed+1;
        var wait=Until(()=>world.Elapsed>deadline,10,"busy crew not interrupted");while(wait.MoveNext())yield return null;
        Require(site.Status=="等待空闲工人"&&site.Assigned==0&&world.Workers.Select(worker=>worker.TaskId).SequenceEqual(taskIds),"Placement interrupted another task");
        world.Paused=true;string pending=Path.Combine(Dir,"pending-auto-crew-v4.json");Require(world.GetComponent<GatheringSaveService>().Save(pending).Accepted,"Save pending dispatch");
        load=Load(pending);while(load.MoveNext())yield return null;site=world.Buildings.Single();Require(world.Paused&&site.Status=="等待空闲工人","Restore pending dispatch");world.Paused=false;
        var helper=world.Workers.First(worker=>worker.CargoCount==0);Require(helper.Stop().Accepted,"Free one idle worker");
        wait=Until(()=>helper.BuildTarget==site,10,"idle worker takes pending site");while(wait.MoveNext())yield return null;
        Require(site.Assigned==1&&site.SupplyStatus.Contains("缺"),"Single crew and shortage");
        var miner=world.Workers.First(worker=>worker!=helper);Require(miner.Work(world.Resources.First(resource=>resource.Work==WorkKind.Mine),false,true).Accepted,"Supply ore");
        wait=Until(()=>site.Complete,150,"automatic stockyard physically completes");while(wait.MoveNext())yield return null;
        checks.Add("Mouse places stockyard while everyone is gathering; tasks preserved; paused pending request survives disk reload; one newly idle worker accepts and completes after wood/ore arrive");
        var wallStart=new Vector3(11,0,0);var wallEnd=new Vector3(13,0,0);world.SetCameraFocus(wallStart);controller.BeginInfrastructure(BuildingKind.Wall);yield return null;
        var start=(Vector2)world.View.WorldToScreenPoint(wallStart);var end=(Vector2)world.View.WorldToScreenPoint(wallEnd);
        mouse=Send(start);while(mouse.MoveNext())yield return null;mouse=Send(start,1);while(mouse.MoveNext())yield return null;mouse=Send(end,1);while(mouse.MoveNext())yield return null;mouse=Send(end);while(mouse.MoveNext())yield return null;
        var walls=world.Buildings.Where(segment=>segment.Kind==BuildingKind.Wall).ToArray();Require(walls.Length==3,"Mouse wall stroke");
        wait=Until(()=>walls.All(segment=>segment.PlannedBuilders>0||segment.Complete),30,"crew after wall navigation update");while(wait.MoveNext())yield return null;
        wait=Until(()=>walls.All(segment=>segment.Complete),120,"automatic wall stroke physically completes");while(wait.MoveNext())yield return null;
        checks.Add("Mouse releases a three-segment wall; after navigation settles, idle crew queues and builds every segment without a second dispatch button");
    }
    private static void Tick()
    {
        try
        {
            if(world.Ready)
            {
                for(int k=0;k<3;k++)Require(world.AccountedResources((ResourceKind)k)==world.InitialResources((ResourceKind)k),"Material conservation "+k);
                foreach(var site in world.Buildings)
                {
                    Require(site.Constructing<=2,"Construction cap");
                    for(int k=0;k<3;k++)Require(site.Delivered[k]+site.InTransit((ResourceKind)k)<=site.MaterialCost((ResourceKind)k),"Per-kind supply cap");
                }
            }
            if(run.MoveNext())return;Finish("PASS");
        }
        catch(Exception e){Debug.LogError("INFRA "+e);Finish("FAIL: "+e.Message);}
    }
    private static void Finish(string status)
    {
        run=null;EditorApplication.update-=Tick;Status=status;Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir,output),JsonUtility.ToJson(new Report{status=status,checks=checks.ToArray(),groundSeconds=groundSeconds,roadSeconds=roadSeconds,accounted=Enumerable.Range(0,3).Select(k=>world.AccountedResources((ResourceKind)k)).ToArray()},true));
        world.GetComponent<GatheringController>().enabled=true;Debug.Log("INFRA "+status);
    }
    private static IEnumerator Until(Func<bool> done,float seconds,string message)
    {Status="RUNNING: "+message;float deadline=Time.realtimeSinceStartup+seconds;while(!done()){Require(Time.realtimeSinceStartup<deadline,"Timeout "+message);yield return null;}}
    private static IEnumerator Load(string file)
    {
        var service=world.GetComponent<GatheringSaveService>();Require(service.Load(file).Accepted,"Load "+file);
        while(service.Loading||world.RoutesSettling)yield return null;
    }
    private static IEnumerator Send(Vector2 p,ushort buttons=0)
    {InputSystem.QueueStateEvent(Mouse.current,new MouseState{position=p,buttons=buttons});int end=Time.frameCount+3;while(Time.frameCount<end)yield return null;}
    private static IEnumerator Execute()
    {
        var load=Load(Path.GetFullPath(Application.dataPath+"/../../output/unity-polish/initial-v2.json"));while(load.MoveNext())yield return null;
        var controller=world.GetComponent<GatheringController>();controller.enabled=false;world.GuideHidden=true;world.Paused=false;
        foreach(var file in new[]{world.GetComponent<GatheringSaveService>().SavePath,Path.Combine(Dir,"before-edit.json")})
        {
            var old=GatheringSaveStore.ReadOne(file);Require(old.buildings.All(b=>b.kind==0),"Legacy kind default");
        }
        checks.Add("real legacy v1 and v3 files validate with stockyard default; official save untouched");
        // Same straight walk, same empty-handed worker, no artificial speed/time scaling.
        var worker=world.Workers[0];var agent=worker.GetComponent<NavMeshAgent>();
        Vector3 start=new Vector3(9,0,-7),end=new Vector3(15,0,-7);
        Require(agent.Warp(start),"Warp ground start");Require(worker.Move(end).Accepted,"Ground move");float began=Time.time;
        var wait=Until(()=>worker.Phase==WorkerPhase.Idle,20,"baseline walking");while(wait.MoveNext())yield return null;groundSeconds=Time.time-began;
        Require(world.PlaceInfrastructure(start,end,BuildingKind.Road,0,out var roads).Accepted&&roads.Count==7,"Road stroke");
        Require(world.SurfaceSpeed(start)==1,"Blueprint speed must be unchanged");
        int count=world.Buildings.Count;
        Require(!world.PlaceInfrastructure(start,end,BuildingKind.Road,0,out _).Accepted&&world.Buildings.Count==count,"Duplicate road stroke charged");
        Require(!world.PlaceInfrastructure(new Vector3(16,0,-7),new Vector3(20,0,-7),BuildingKind.Road,0,out _).Accepted&&world.Buildings.Count==count,"Invalid stroke partially placed");
        Require(roads[0].SupplyStatus.Contains("缺"),"Missing materials explanation");
        // Supply from an actual one-trip gathering job, preserving the original resource budget.
        var gatherer=world.Workers[5];Require(gatherer.Work(world.Resources.First(r=>r.Work==WorkKind.Chop),false,true).Accepted,"Gather road material");
        Require(gatherer.FinishTrip().Accepted,"Finish gathering trip");
        wait=Until(()=>world.Inventory[0]>=6,65,"real wood gathering");while(wait.MoveNext())yield return null;
        Require(gatherer.Work(world.Resources.First(r=>r.Work==WorkKind.Chop),false,true).Accepted,"Second gather trip");
        wait=Until(()=>world.Inventory[0]>=12,65,"second real wood trip");while(wait.MoveNext())yield return null;
        checks.Add("7-cell road stroke; blueprint has no speed bonus; duplicate and out-of-bounds strokes have no partial mutation; actual wood gathered");
        foreach(var builder in world.Workers.Take(2))foreach(var road in roads)Require(builder.Build(road,builder.BuildTarget!=null).Accepted,"Queue road construction");
        wait=Until(()=>world.Workers.Any(w=>w.CargoSite),30,"road transit");while(wait.MoveNext())yield return null;
        world.Paused=true;var saves=world.GetComponent<GatheringSaveService>();string transit=Path.Combine(Dir,"road-transit-v4.json");Require(saves.Save(transit).Accepted,"Save road transit");
        var before=GatheringSaveStore.ReadOne(transit);Require(before.version==4&&before.buildings.All(b=>b.kind==(int)BuildingKind.Road),"Save kinds");
        for(int i=0;i<2;i++){load=Load(transit);while(load.MoveNext())yield return null;Require(world.Workers.Sum(w=>w.CargoCount)==before.workers.Sum(w=>w.count),"Transit restored twice");}
        roads=world.Buildings.Where(b=>b.IsRoad).ToList();world.Paused=false;
        wait=Until(()=>roads.All(r=>r.Complete)&&!world.RoutesSettling,150,"queued roads finish");while(wait.MoveNext())yield return null;
        Require(world.DepotBayCount==3,"Roads must not create depot bays");
        foreach(var w in world.Workers)w.Stop();
        Require(agent.Warp(start),"Warp road start");Require(worker.Move(end).Accepted,"Road move");began=Time.time;
        wait=Until(()=>worker.Phase==WorkerPhase.Idle,20,"road walking");while(wait.MoveNext())yield return null;roadSeconds=Time.time-began;
        Require(roadSeconds<groundSeconds&&roadSeconds>groundSeconds*.75f,"Limited real walking improvement");
        Require(world.SurfaceSpeed(start)>1&&world.SurfaceSpeed(new Vector3(8,0,-7))==1,"Localized road speed");
        Require(NavMesh.SamplePosition(start,out var hit,.2f,NavMesh.AllAreas)&&(hit.mask&(1<<GatheringWorld.RoadArea))!=0,"Completed road navigation area");
        checks.Add("physical haul/delivery/labour completes seven queued roads; v4 transit restores twice; no extra depot bays; actual same-route walking improves modestly with road area");
        // Wall blocks navigation before and after completion, while preserving existing entrances.
        Require(world.PlaceInfrastructure(new Vector3(11,0,-3),new Vector3(15,0,-3),BuildingKind.Wall,0,out var walls).Accepted,"Wall stroke");
        while(world.RoutesSettling)yield return null;
        Require(NavMesh.Raycast(new Vector3(13,0,-4),new Vector3(13,0,-2),out _,NavMesh.AllAreas),"Wall does not block direct navigation");
        Require(!world.PlaceInfrastructure(start,start,BuildingKind.Wall,0,out _).Accepted,"Wall over road accepted");
        foreach(var builder in world.Workers.Take(2))foreach(var wall in walls)Require(builder.Build(wall,builder.BuildTarget!=null).Accepted,"Queue wall construction");
        Require(gatherer.Work(world.Resources.First(r=>r.Work==WorkKind.Chop),false,true).Accepted,"Wall supplies");
        wait=Until(()=>walls.Any(w=>w.Labour>0&&!w.Complete),100,"partial wall labour");while(wait.MoveNext())yield return null;
        world.Paused=true;string partial=Path.Combine(Dir,"mixed-partial-v4.json");Require(saves.Save(partial).Accepted,"Save mixed partial");
        load=Load(partial);while(load.MoveNext())yield return null;walls=world.Buildings.Where(b=>b.Kind==BuildingKind.Wall).ToList();world.Paused=false;
        wait=Until(()=>walls.All(w=>w.Complete),150,"queued walls finish");while(wait.MoveNext())yield return null;
        while(world.RoutesSettling)yield return null;
        Require(world.DepotBayCount==3&&NavMesh.Raycast(new Vector3(13,0,-4),new Vector3(13,0,-2),out _,NavMesh.AllAreas),"Completed walls effect");
        checks.Add("five wall segments block actual navigation; road overlap rejected; mixed road+partial-wall save restores and actual queued work finishes, preserving materials and depot bays");
        var tail=Tail();while(tail.MoveNext())yield return null;
    }
    public static string ContinueTail()
    {
        Require(EditorApplication.isPlaying&&run==null,"Continue once");world=UnityEngine.Object.FindFirstObjectByType<GatheringWorld>();
        var previous=JsonUtility.FromJson<Report>(File.ReadAllText(Path.Combine(Dir,"verification.json")));
        checks.Clear();checks.AddRange(previous.checks);groundSeconds=previous.groundSeconds;roadSeconds=previous.roadSeconds;output="verification.json";
        run=TailLoad();Status="RUNNING";EditorApplication.update+=Tick;return Status;
    }
    private static IEnumerator TailLoad()
    {
        var load=Load(Path.Combine(Dir,"finished-before-corner.json"));while(load.MoveNext())yield return null;
        world.Paused=false;var tail=Tail();while(tail.MoveNext())yield return null;
    }
    private static IEnumerator Tail()
    {
        var controller=world.GetComponent<GatheringController>();controller.enabled=false;
        var saves=world.GetComponent<GatheringSaveService>();var gatherer=world.Workers[5];
        foreach(var w in world.Workers){w.Stop();w.Move(world.Home(w.Id));}
        var wait=Until(()=>world.Workers.All(w=>w.Phase==WorkerPhase.Idle),35,"workers clear future corner");while(wait.MoveNext())yield return null;
        Require(world.PlaceInfrastructure(new Vector3(15,0,-3),new Vector3(15,0,0),BuildingKind.Wall,1,out var corner).Accepted&&corner.Count==3,"Wall corner and shared endpoint");
        while(world.RoutesSettling)yield return null;
        Require(NavMesh.Raycast(new Vector3(14,0,-1),new Vector3(16,0,-1),out _,NavMesh.AllAreas),"Vertical corner blocking");
        foreach(var segment in corner)Require(segment.Cancel().Accepted,"Cancel corner blueprint");
        while(world.RoutesSettling)yield return null;
        checks.Add("wall turns through reused endpoint without duplicate cost; vertical continuation blocks passage and cancelling blueprints reopens it");
        // Cancel a staffed road after materials arrive, without synthesizing inventory.
        foreach(var w in world.Workers)w.Stop();
        Require(world.PlaceInfrastructure(new Vector3(9,0,0),new Vector3(9,0,0),BuildingKind.Road,0,out var cancel).Accepted,"Cancel road place");
        Require(cancel[0].AssignIdle(1).Accepted,"Cancel road staffing");
        wait=Until(()=>cancel[0].Delivered[0]>0,60,"road delivery before cancel");while(wait.MoveNext())yield return null;
        Require(cancel[0].TogglePause().Accepted,"Road pause");float labour=cancel[0].Labour;
        yield return null;Require(cancel[0].Labour==labour,"Paused labour changed");Require(cancel[0].Cancel().Accepted&&!cancel[0].Cancel().Accepted,"Cancel twice");
        Require(world.SurfaceSpeed(new Vector3(9,0,0))==1&&world.Resources.Any(r=>r.IsRefund&&r.Stock>0),"Road cancellation refund/speed");
        while(world.RoutesSettling)yield return null;
        checks.Add("paused road retains labour; cancellation creates real refund crate once and never enables road bonus");
        // Genuine Input System press/drag/release; UI crossing and secondary cancellation.
        controller.enabled=true;world.SetCameraFocus(new Vector3(9,0,-1));world.View.orthographicSize=12;world.GuideHidden=true;
        yield return null;controller.BeginInfrastructure(BuildingKind.Road);
        Vector2 Screen(Vector3 p)=>world.View.WorldToScreenPoint(p);
        var send=Send(Screen(new Vector3(9,0,2)));while(send.MoveNext())yield return null;
        send=Send(Screen(new Vector3(9,0,2)),1);while(send.MoveNext())yield return null;
        send=Send(Screen(new Vector3(12,0,2)),1);while(send.MoveNext())yield return null;
        send=Send(Screen(new Vector3(12,0,2)));while(send.MoveNext())yield return null;
        Require(world.Buildings.Count(b=>b.IsRoad&&!b.Cancelled)==11,"Mouse road stroke");
        var newRoads=world.Buildings.Skip(world.Buildings.Count-4).ToArray();
        wait=Until(()=>newRoads.All(site=>site.PlannedBuilders==2),10,"automatic crew for mouse road stroke");while(wait.MoveNext())yield return null;
        Require(!controller.AssignRecentStroke().Accepted,"Duplicate crew accepted for fully staffed road stroke");
        int beforeCancel=world.Buildings.Count;
        controller.BeginInfrastructure(BuildingKind.Wall);send=Send(Screen(new Vector3(9,0,5)));while(send.MoveNext())yield return null;
        send=Send(Screen(new Vector3(9,0,5)),2);while(send.MoveNext())yield return null;
        send=Send(Screen(new Vector3(9,0,5)));while(send.MoveNext())yield return null;
        Require(world.Buildings.Count==beforeCancel,"Right cancel creates wall");
        controller.BeginInfrastructure(BuildingKind.Road);send=Send(Screen(new Vector3(9,0,5)));while(send.MoveNext())yield return null;
        send=Send(Screen(new Vector3(9,0,5)),1);while(send.MoveNext())yield return null;
        send=Send(new Vector2(35,world.View.pixelHeight-40),1);while(send.MoveNext())yield return null;
        send=Send(new Vector2(35,world.View.pixelHeight-40));while(send.MoveNext())yield return null;
        Require(world.Buildings.Count==beforeCancel,"UI release created road");
        checks.Add("Input System drag builds four road cells and automatically queues two idle builders; duplicate dispatch is rejected; right cancel and UI release create nothing");
        world.Paused=true;Require(saves.Save(Path.Combine(Dir,"final-v4.json")).Accepted,"Final save");
        var invalid=world.CaptureSave();invalid.buildings[0].kind=99;bool rejected=false;try{invalid.Validate();}catch(Exception){rejected=true;}Require(rejected,"Invalid facility kind accepted");
        checks.Add("unknown infrastructure type is rejected before restore; per-frame material conservation and per-site caps throughout");
    }
    private static IEnumerator RestartRun()
    {
        var load=Load(Path.Combine(Dir,"mixed-partial-v4.json"));while(load.MoveNext())yield return null;
        Require(world.Buildings.Any(b=>b.IsRoad&&b.Complete)&&world.Buildings.Any(b=>b.Kind==BuildingKind.Wall&&!b.Complete),"Restart mixed types");
        world.Paused=false;var wait=Until(()=>world.Buildings.Where(b=>b.Kind==BuildingKind.Wall).All(b=>b.Complete),150,"real restart continues wall work");while(wait.MoveNext())yield return null;
        Require(world.DepotBayCount==3&&world.SurfaceSpeed(new Vector3(11,0,-7))>1,"Restart effects");
        world.Paused=true;checks.Add("actual Play Mode exit/reentry loads v4 completed road and incomplete wall, continues hauling and labour to completion, keeps road effect and exactly three original depot bays");
    }
}

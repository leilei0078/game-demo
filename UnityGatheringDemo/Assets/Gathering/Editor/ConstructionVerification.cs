using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using GatheringDemo;

public static class ConstructionVerification
{
    [Serializable] private sealed class Report {public string status,recordedUtc;public List<string> checks;public int[] accounted,initial;public int buildings,openBays,harvestedItems;}
    public static string Status {get;private set;}="not started";
    private static readonly List<string> checks=new List<string>();
    private static IEnumerator run;
    private static GatheringWorld world;
    private static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    public static string Start()
    {
        Require(EditorApplication.isPlaying&&run==null,"Start once in Play Mode");world=UnityEngine.Object.FindFirstObjectByType<GatheringWorld>();
        Require(world&&world.Buildings.Count==0&&world.Inventory.Sum()==0,"Use a fresh scene");
        checks.Clear();Status="RUNNING";run=Execute();EditorApplication.update+=Tick;return Status;
    }
    private static void Tick()
    {
        try{Require(EditorApplication.isPlaying,"Play Mode ended");Conservation();if(run.MoveNext())return;Finish("PASS");}
        catch(Exception ex){Debug.LogError("CONSTRUCTION FAIL: "+ex);Finish("FAIL: "+ex.Message);}
    }
    private static void Finish(string status)
    {
        EditorApplication.update-=Tick;run=null;Status=status;
        var directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../output/unity-construction"));Directory.CreateDirectory(directory);
        var report=new Report{status=status,recordedUtc=DateTime.UtcNow.ToString("O"),checks=new List<string>(checks),accounted=Enumerable.Range(0,3).Select(k=>world.AccountedResources((ResourceKind)k)).ToArray(),initial=Enumerable.Range(0,3).Select(k=>world.InitialResources((ResourceKind)k)).ToArray(),buildings=world.Buildings.Count,openBays=world.DepotBayCount,harvestedItems=world.Metrics.Items};
        File.WriteAllText(Path.Combine(directory,"construction-verification.json"),JsonUtility.ToJson(report,true));Debug.Log("CONSTRUCTION "+status);
    }
    private static void Conservation()
    {
        foreach(ResourceKind kind in Enum.GetValues(typeof(ResourceKind)))Require(world.AccountedResources(kind)==world.InitialResources(kind),kind+" not conserved");
        foreach(var b in world.Buildings)
        {
            for(int k=0;k<2;k++)Require(b.Delivered[k]+b.InTransit((ResourceKind)k)<=ConstructionSite.Cost((ResourceKind)k),"Site over-supplied");
            Require(world.Workers.Count(w=>w.BuildTarget==b&&w.Phase==WorkerPhase.Constructing)<=2,"Too many constructors");
        }
        foreach(var w in world.Workers)Require(w.CargoCount<=world.Settings.MaxItems&&w.CargoWeight<=world.Settings.MaxWeight+.001f,"Cargo exceeds capacity");
        var m=world.Metrics;Require(Math.Abs(m.Aggregate().Total-m.Elapsed*world.Workers.Count)<.08,"Worker seconds missing from metrics");
    }
    private static IEnumerator Until(Func<bool> condition,float timeout,string title)
    {
        float start=world.Elapsed;Status="RUNNING: "+title;
        while(!condition()){Require(world.Elapsed-start<timeout,"Timed out: "+title);yield return null;}
    }
    private static IEnumerator Execute()
    {
        while(world.RoutesSettling)yield return null;
        var controller=world.GetComponent<GatheringController>();controller.enabled=false;
        Require(!world.PlaceBuilding(new Vector3(-6,0,-2),0,out _).Accepted&&!world.PlaceBuilding(new Vector3(GatheringWorld.HalfWidth,0,GatheringWorld.HalfDepth),1,out _).Accepted,"Invalid placement accepted");
        Require(world.Inventory.Sum()==0&&world.Buildings.Count==0,"Invalid placement mutated economy");
        Require(world.PlaceBuilding(new Vector3(7,0,-5),0,out var cancelled).Accepted,"Valid blueprint rejected");
        while(world.RoutesSettling)yield return null;
        var worker=world.Workers[0];Require(worker.Build(cancelled).Accepted,"Cannot assign builder");
        float emptyUntil=world.Elapsed+1;while(world.Elapsed<emptyUntil)yield return null;
        Require(cancelled.Labour==0&&cancelled.Delivered.Sum()==0&&worker.Phase==WorkerPhase.WaitingBuild,"Empty stock created work/material");
        checks.Add("placement rejection, empty blueprint and missing-material wait");

        for(int i=1;i<=4;i++)Require(world.Workers[i].Work(world.Resources[i==4?2:0],false,true).Accepted,"Gather assignment rejected");
        var wait=Until(()=>cancelled.Delivered.Sum()>0&&worker.CargoSite==cancelled,65,"real gathering and delivery");while(wait.MoveNext())yield return wait.Current;
        int carried=worker.CargoCount;float labour=cancelled.Labour;worker.Stop();
        Require(worker.CargoCount==carried&&worker.CargoSite==null&&worker.Phase==WorkerPhase.Idle,"Builder stop lost cargo/assignment");
        Require(worker.Build(cancelled).Accepted,"Cannot reassign stopped builder");
        wait=Until(()=>worker.CargoSite==cancelled&&worker.CargoCount>0,40,"reassign returns old material before fresh supply");while(wait.MoveNext())yield return wait.Current;
        Require(cancelled.TogglePause().Accepted,"Cannot pause site");
        wait=Until(()=>worker.CargoCount==0&&worker.Phase==WorkerPhase.WaitingBuild,30,"paused supply returns to warehouse");while(wait.MoveNext())yield return wait.Current;
        Require(cancelled.Labour==labour&&cancelled.Paused,"Paused site advanced labour");
        cancelled.TogglePause();
        wait=Until(()=>worker.CargoSite==cancelled,25,"resumed supply");while(wait.MoveNext())yield return wait.Current;
        int delivered=cancelled.Delivered.Sum(),harvested=world.Metrics.Items;
        Require(delivered>0&&cancelled.Cancel().Accepted&&!cancelled.Cancel().Accepted,"Cancel failed or duplicate cancel accepted");
        Require(cancelled.Delivered.Sum()==0&&world.Resources.Where(r=>r.IsRefund).Sum(r=>r.Stock)==delivered,"Delivered materials not refunded physically");
        wait=Until(()=>world.Workers.All(w=>w.CargoCount==0&&w.Phase==WorkerPhase.Idle),45,"cancelled cargo returns");while(wait.MoveNext())yield return wait.Current;
        foreach(var refund in world.Resources.Where(r=>r.IsRefund).ToArray())
        {
            Require(worker.Work(refund,false,true).Accepted,"Cannot collect refund");
            wait=Until(()=>worker.Phase==WorkerPhase.Idle&&worker.CargoCount==0,35,"physical refund collection");while(wait.MoveNext())yield return wait.Current;
        }
        Require(world.Metrics.Items==harvested&&world.Inventory.Sum()==24,"Refund created harvest income or lost materials");
        checks.Add("stop/reassign preserves cargo, pause returns transit, resume, cancel and physical refund without duplicate income");

        while(world.RoutesSettling)yield return null;
        Require(world.PlaceBuilding(new Vector3(7,0,-5),0,out var final).Accepted,"Cannot rebuild cancelled site");
        while(world.RoutesSettling)yield return null;
        for(int i=0;i<4;i++)Require(world.Workers[i].Build(final).Accepted,"Concurrent builder assignment rejected");
        Require(worker.Move(new Vector3(8,0,0),true).Accepted,"Cannot queue after construction");
        wait=Until(()=>world.Workers.Take(4).Any(w=>w.CargoSite==final),20,"material dispatched");while(wait.MoveNext())yield return wait.Current;
        foreach(var w in world.Workers.Where(w=>Mathf.Abs(w.transform.position.x+2)<.65f))w.Stop();
        // No worker should yet be in the narrow crossing immediately after loading.
        world.ToggleRoadblock();Require(world.Roadblock,"Could not place route barrier");
        wait=Until(()=>world.Workers.Take(4).Any(w=>w.Phase==WorkerPhase.Blocked),20,"construction route interrupted");while(wait.MoveNext())yield return wait.Current;
        world.ToggleRoadblock();Require(!world.Roadblock,"Could not reopen route");
        wait=Until(()=>world.Workers.Any(w=>w.Phase==WorkerPhase.Constructing),60,"route recovery and full material delivery");while(wait.MoveNext())yield return wait.Current;
        Require(final.Supplied,"Construction began without material");
        final.TogglePause();float before=final.Labour;float until=world.Elapsed+1;while(world.Elapsed<until)yield return null;
        Require(final.Labour==before,"Site pause advanced construction");final.TogglePause();
        world.Paused=true;float pausedLabour=final.Labour;double metricTime=world.Metrics.Elapsed;float realtime=Time.realtimeSinceStartup+.3f;
        while(Time.realtimeSinceStartup<realtime)yield return null;
        Require(final.Labour==pausedLabour&&world.Metrics.Elapsed==metricTime,"Global pause advanced state");world.Paused=false;
        wait=Until(()=>final.Complete&&worker.Phase==WorkerPhase.Idle,45,"construction completes and hands off queued move");while(wait.MoveNext())yield return wait.Current;
        Require(world.DepotBayCount==5&&final.Delivered[0]==18&&final.Delivered[1]==6&&Mathf.Approximately(final.Labour,24),"Completion/cost mismatch");
        Require(Vector3.Distance(worker.transform.position,new Vector3(8,0,0))<.3f,"Completion did not hand off queue");
        final.Work(100);Require(world.DepotBayCount==5&&!final.Cancel().Accepted,"Completion applied twice");
        checks.Add("concurrent supply caps, blocked route recovery, site/global pause, labour accounting, completion once and queued handoff");

        var carrier=world.Workers[5];Require(carrier.Work(world.Resources[1],false,true).Accepted,"Cannot gather for new stockyard");
        bool usedNewBay=false;float started=world.Elapsed;int income=world.Metrics.Items;
        while(carrier.Phase!=WorkerPhase.Idle){Require(world.Elapsed-started<40,"New depot delivery timed out");usedNewBay|=carrier.Bay>=3;yield return null;}
        Require(usedNewBay&&world.Metrics.Items==income+6&&world.Inventory[0]==6,"Completed stockyard not used for actual deposit");
        checks.Add("finished stockyard receives real harvested cargo through a new unloading bay");

        var rotatedCenter=PlacementGrid.Center(new Vector3(11,0,3),PlacementGrid.BuildingStep);
        Require(ConstructionSite.PlacementReason(world,rotatedCenter,1)=="","Rotated test location unavailable");
        Require(world.PlaceBuilding(rotatedCenter,1,out var rotated).Accepted,"Rotated blueprint rejected");
        while(world.RoutesSettling)yield return null;
        Require(Mathf.Approximately(ConstructionSite.Size(rotated.Rotation).x,2)&&Mathf.Abs(rotated.WorkPoint(0).x-rotatedCenter.x-1.9f)<.001f,"Rotated footprint/entrance mismatch");
        var queued=world.Workers[6];Require(queued.Work(world.Resources[0],false,true).Accepted&&queued.Build(rotated,true).Accepted,"Cannot queue construction after gathering");
        Require(queued.QueueCount==1&&queued.Source==world.Resources[0],"Queued build interrupted harvest");
        Require(world.Workers[7].Work(world.Resources[0],false,true).Accepted&&world.Workers[8].Work(world.Resources[2],false,true).Accepted,"Cannot gather variant materials");
        wait=Until(()=>queued.BuildTarget==rotated&&queued.QueueCount==0,40,"gather delivery hands off queued construction");while(wait.MoveNext())yield return wait.Current;
        wait=Until(()=>queued.Phase==WorkerPhase.Constructing&&rotated.Labour>0,70,"rotated construction starts");while(wait.MoveNext())yield return wait.Current;
        Require(rotated.Supplied&&!rotated.Complete,"Rotated site missing materials or already complete");
        int beforeCancel=world.Metrics.Items;rotated.Cancel();
        Require(world.DepotBayCount==5&&world.Resources.Where(r=>r.IsRefund).Sum(r=>r.Stock)==24,"Mid-construction cancel lost material or opened depot");
        wait=Until(()=>queued.Phase==WorkerPhase.Idle,10,"cancelled constructor releases work slot");while(wait.MoveNext())yield return wait.Current;
        Require(world.Metrics.Items==beforeCancel,"Cancelling labour created harvest income");
        checks.Add("rotated footprint/entrance, queued gathering-to-build handoff, partial-labour cancellation preserves all material");
        controller.enabled=true;controller.Select(new[]{worker});world.Notice="建造专项验证通过；可打开建造面板查看完成的堆场";
    }
}

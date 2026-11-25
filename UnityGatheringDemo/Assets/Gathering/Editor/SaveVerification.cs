using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using GatheringDemo;

public static class SaveVerification
{
    [Serializable] private sealed class Report {public string status,recordedUtc;public List<string> checks;public string savePath;public int[] accounted;}
    public static string Status {get;private set;}="not started";
    private static IEnumerator run;
    private static GatheringWorld world;
    private static GatheringSaveService saves;
    private static string path;
    private static bool restartRun;
    private static readonly List<string> checks=new List<string>();
    private static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
    private static string DirectoryPath=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../output/unity-save"));
    public static string Start()
    {
        Check(EditorApplication.isPlaying&&run==null,"Start in Play Mode");world=UnityEngine.Object.FindFirstObjectByType<GatheringWorld>();saves=world.GetComponent<GatheringSaveService>();
        Check(world.Buildings.Count==0&&world.Inventory.Sum()==0,"Use a fresh world");
        Directory.CreateDirectory(DirectoryPath);path=Path.Combine(DirectoryPath,"checkpoint-"+Guid.NewGuid().ToString("N")+".json");restartRun=false;checks.Clear();Status="RUNNING";run=Execute();EditorApplication.update+=Tick;return Status;
    }
    private static void Tick()
    {
        try
        {
            Check(EditorApplication.isPlaying,"Play Mode ended unexpectedly");
            if(world.Ready)foreach(ResourceKind kind in Enum.GetValues(typeof(ResourceKind)))Check(world.AccountedResources(kind)==world.InitialResources(kind),"Resource conservation: "+kind);
            if(run.MoveNext())return;Finish("PASS");
        }
        catch(Exception ex){Debug.LogError("SAVE FAIL: "+ex);Finish("FAIL: "+ex.Message);}
    }
    private static void Finish(string status)
    {
        EditorApplication.update-=Tick;run=null;Status=status;
        var report=new Report{status=status,recordedUtc=DateTime.UtcNow.ToString("O"),checks=new List<string>(checks),savePath=path,accounted=Enumerable.Range(0,3).Select(k=>world.AccountedResources((ResourceKind)k)).ToArray()};
        File.WriteAllText(Path.Combine(DirectoryPath,restartRun?"restart-verification.json":"save-verification.json"),JsonUtility.ToJson(report,true));Debug.Log("SAVE "+status);
    }
    private static IEnumerator Until(Func<bool> condition,float seconds,string title)
    {
        Status="RUNNING: "+title;float start=Time.realtimeSinceStartup;
        while(!condition()){Check(Time.realtimeSinceStartup-start<seconds,"Timed out: "+title+"; "+saves.Status);yield return null;}
    }
    private static void SameEconomy(GatheringSaveData expected)
    {
        var actual=world.CaptureSave();
        Check(expected.inventory.SequenceEqual(actual.inventory)&&expected.initial.SequenceEqual(actual.initial),"Inventory changed on load");
        Check(expected.resources.Length==actual.resources.Length&&expected.buildings.Length==actual.buildings.Length,"Entity count changed");
        foreach(var r in expected.resources){var a=actual.resources.Single(v=>v.id==r.id);Check(a.stock==r.stock&&a.initial==r.initial&&a.kind==r.kind&&a.refund==r.refund,"Resource changed: "+r.id);}
        foreach(var b in expected.buildings){var a=actual.buildings.Single(v=>v.id==b.id);Check(a.delivered.SequenceEqual(b.delivered)&&a.labour==b.labour&&a.complete==b.complete&&a.cancelled==b.cancelled&&a.paused==b.paused,"Building changed: "+b.id);}
        foreach(var w in expected.workers)
        {
            var a=actual.workers.Single(v=>v.id==w.id);
            Check(a.count==w.count&&a.kind==w.kind&&a.unitWeight==w.unitWeight&&a.returningMaterials==w.returningMaterials&&a.cargoSite==w.cargoSite,"Cargo changed: "+w.id);
            Check(a.taskId==w.taskId&&a.source==w.source&&a.building==w.building&&a.lastSource==w.lastSource&&a.finishAfterTrip==w.finishAfterTrip&&a.deliverBeforeWork==w.deliverBeforeWork,"Task changed: "+w.id);
            Check(JsonUtility.ToJson(new QueueWrapper{orders=a.queue})==JsonUtility.ToJson(new QueueWrapper{orders=w.queue}),"Queue changed: "+w.id);
        }
        Check(expected.roadblock==actual.roadblock&&world.DepotBayCount==3+expected.buildings.Count(b=>b.complete)*2,"Barrier or completed depots duplicated");
        Check(world.Resources.All(r=>r.Reserved==0),"Unharvested reservations restored");
        Check(world.Metrics.Elapsed==0&&world.Metrics.Items==0,"Old observation window carried across load");
    }
    [Serializable] private sealed class QueueWrapper {public OrderSave[] orders;}
    private static IEnumerator RoundTrip(string title)
    {
        world.Paused=true;Check(saves.Save(path).Accepted,"Cannot save "+title+": "+saves.Status);var saved=GatheringSaveStore.ReadOne(path);
        for(int repeat=0;repeat<2;repeat++)
        {
            Check(saves.Load(path).Accepted,"Cannot load "+title);
            var wait=Until(()=>!saves.Loading,10,"restore "+title);while(wait.MoveNext())yield return wait.Current;
            Check(world.Ready&&saves.Status.Contains("已恢复"),"Restore failed: "+saves.Status);SameEconomy(saved);
        }
        world.Paused=false;checks.Add(title+"; repeated restore preserves economy, IDs and queued orders");
    }
    private static IEnumerator Execute()
    {
        while(world.RoutesSettling)yield return null;
        world.GetComponent<GatheringController>().enabled=false;
        var fresh=world.CaptureSave();string file=Path.Combine(DirectoryPath,"storage-"+Guid.NewGuid().ToString("N")+".json");
        GatheringSaveStore.Write(file,fresh);fresh.elapsed+=1;GatheringSaveStore.Write(file,fresh);
        Check(File.Exists(file+".bak"),"Backup missing");File.WriteAllText(file+".tmp","interrupted write");
        Check(GatheringSaveStore.Read(file,out bool fallback).elapsed==fresh.elapsed&&!fallback,"Temporary file replaced committed data");
        File.WriteAllText(file,"broken");var previous=GatheringSaveStore.Read(file,out fallback);
        Check(fallback&&previous.elapsed==fresh.elapsed-1,"Did not fall back to previous generation");
        GatheringSaveStore.Write(file,fresh);Check(GatheringSaveStore.ReadOne(file+".bak").elapsed==previous.elapsed,"Save overwrote good backup with corrupt primary");
        File.WriteAllText(file,"broken");File.WriteAllText(file+".bak","broken");world.Paused=true;
        Check(!saves.Load(file).Accepted&&world.Ready&&world.Inventory.SequenceEqual(fresh.inventory),"Corrupt load changed world");world.Paused=false;
        fresh.version=99;bool rejected=false;try{GatheringSaveStore.Write(file,fresh);}catch(Exception){rejected=true;}Check(rejected,"Unknown version accepted");fresh.version=1;
        fresh.workers[0].source=99999;rejected=false;try{fresh.Validate();}catch(Exception){rejected=true;}Check(rejected,"Missing reference accepted");
        checks.Add("atomic replacement, previous backup, interrupted temporary ignored, corrupt primary recovery, corrupt load leaves world, incompatible version and missing reference rejected");

        Check(world.PlaceBuilding(new Vector3(7,0,-5),0,out _).Accepted,"Cannot place test blueprint");
        while(world.RoutesSettling)yield return null;
        var worker=world.Workers[0];Check(worker.Work(world.Resources[0],false,true).Accepted&&worker.Build(world.Buildings[0],true).Accepted,"Cannot queue build");
        var waitStep=Until(()=>worker.BatchCount>0&&worker.Progress>0,25,"reserved harvest");while(waitStep.MoveNext())yield return waitStep.Current;
        var round=RoundTrip("unharvested batch restarts, source not deducted, gather-to-build queue");while(round.MoveNext())yield return round.Current;
        Check(worker.BatchCount==0&&worker.Progress==0,"Unharvested progress survived");
        waitStep=Until(()=>worker.CargoCount>0&&worker.Source,25,"harvested cargo in transit");while(waitStep.MoveNext())yield return waitStep.Current;
        round=RoundTrip("harvested cargo travelling to depot");while(round.MoveNext())yield return round.Current;
        waitStep=Until(()=>worker.Phase==WorkerPhase.Unloading&&worker.Progress>0,25,"partial warehouse unloading");while(waitStep.MoveNext())yield return waitStep.Current;
        round=RoundTrip("partial unloading restarts without duplicate warehouse deposit");while(round.MoveNext())yield return round.Current;
        for(int i=1;i<=3;i++)Check(world.Workers[i].Work(world.Resources[i==3?2:0],false,true).Accepted,"Cannot gather building supply");
        waitStep=Until(()=>worker.BuildTarget&&worker.CargoSite,50,"building materials in transit");while(waitStep.MoveNext())yield return waitStep.Current;
        round=RoundTrip("construction material cargo retains allocation and locked weight");while(round.MoveNext())yield return round.Current;
        waitStep=Until(()=>world.Workers.Skip(1).Take(3).All(w=>w.Phase==WorkerPhase.Idle&&w.CargoCount==0),40,"finish supply harvest before reassignment");while(waitStep.MoveNext())yield return waitStep.Current;
        while(world.RoutesSettling)yield return null;
        for(int i=1;i<3;i++)Check(world.Workers[i].Build(world.Buildings[0]).Accepted,"Cannot add builders");
        waitStep=Until(()=>world.Buildings[0].Labour>2&&world.Buildings[0].Labour<22,65,"partial construction");while(waitStep.MoveNext())yield return waitStep.Current;
        round=RoundTrip("half-built site retains delivered materials and labour");while(round.MoveNext())yield return round.Current;
        world.Paused=true;Check(saves.Save(Path.Combine(DirectoryPath,"half-built-restart.json")).Accepted,"Cannot save half-built restart checkpoint");world.Paused=false;
        world.Buildings[0].TogglePause();
        round=RoundTrip("paused site retains labour and workers");while(round.MoveNext())yield return round.Current;
        Check(world.Buildings[0].Paused,"Site pause missing");world.Buildings[0].TogglePause();
        waitStep=Until(()=>world.Buildings[0].Complete,35,"finish restored building");while(waitStep.MoveNext())yield return waitStep.Current;
        // Allow finished workers to hand off before snapshotting.
        waitStep=Until(()=>world.Workers.All(w=>w.BuildTarget==null),10,"completion handoff");while(waitStep.MoveNext())yield return waitStep.Current;
        round=RoundTrip("completed depot reconstructs exactly two unloading bays");while(round.MoveNext())yield return round.Current;

        while(world.RoutesSettling)yield return null;
        var blocked=world.Workers[10];Check(blocked.Work(world.Resources[0],false,true).Accepted,"Cannot start blocked scenario");
        world.ToggleRoadblock();Check(world.Roadblock,"Cannot block road");
        waitStep=Until(()=>blocked.Phase==WorkerPhase.Blocked,20,"blocked gathering task");while(waitStep.MoveNext())yield return waitStep.Current;
        round=RoundTrip("closed road and blocked task retain target");while(round.MoveNext())yield return round.Current;
        while(world.RoutesSettling)yield return null;
        waitStep=Until(()=>blocked.Phase==WorkerPhase.Blocked,15,"recomputed blocked route");while(waitStep.MoveNext())yield return waitStep.Current;
        world.ToggleRoadblock();
        waitStep=Until(()=>blocked.Phase==WorkerPhase.Idle,40,"reopened saved road recovers task");while(waitStep.MoveNext())yield return waitStep.Current;

        while(world.RoutesSettling)yield return null;
        Check(world.PlaceBuilding(new Vector3(3,0,1),0,out var refundSite).Accepted,"Cannot place refund site");
        while(world.RoutesSettling)yield return null;
        Check(worker.Work(world.Resources[0],false,true).Accepted&&worker.Build(refundSite,true).Accepted,"Cannot start refund scenario");
        for(int i=4;i<=6;i++)Check(world.Workers[i].Work(world.Resources[i==6?2:0],false,true).Accepted,"Cannot gather refund material");
        waitStep=Until(()=>refundSite.Delivered.Sum()>0&&worker.CargoSite==refundSite,70,"delivered and in-transit refund");while(waitStep.MoveNext())yield return waitStep.Current;
        Check(refundSite.Cancel().Accepted,"Cannot cancel refund site");
        round=RoundTrip("cancelled site restores refund boxes and returning cargo without respawning refund");while(round.MoveNext())yield return round.Current;
        waitStep=Until(()=>worker.CargoCount==0,25,"returned cancelled cargo");while(waitStep.MoveNext())yield return waitStep.Current;
        var refund=world.Resources.First(r=>r.IsRefund&&r.Stock>0);Check(worker.Work(refund,false,true).Accepted,"Cannot collect refund");
        waitStep=Until(()=>worker.CargoCount>0,25,"refund box cargo");while(waitStep.MoveNext())yield return waitStep.Current;
        worker.Stop();
        round=RoundTrip("stopped worker retains refund cargo without automatic deposit");while(round.MoveNext())yield return round.Current;
        Check(worker.Phase==WorkerPhase.Idle&&worker.CargoCount>0,"Stopped cargo restarted unexpectedly");
        worker.Deliver();waitStep=Until(()=>worker.CargoCount==0,25,"manual refund return");while(waitStep.MoveNext())yield return waitStep.Current;
        world.Paused=true;Check(saves.Save(Path.Combine(DirectoryPath,"restart-checkpoint.json")).Accepted,"Cannot save restart checkpoint");
        world.GetComponent<GatheringController>().enabled=true;
    }

    public static string RestoreAfterRestart()
    {
        Check(EditorApplication.isPlaying&&run==null,"Enter a fresh Play session");world=UnityEngine.Object.FindFirstObjectByType<GatheringWorld>();saves=world.GetComponent<GatheringSaveService>();
        path=Path.Combine(DirectoryPath,"restart-checkpoint.json");restartRun=true;checks.Clear();Status="RUNNING";run=CheckRestart();EditorApplication.update+=Tick;return Status;
    }
    private static IEnumerator CheckRestart()
    {
        var expected=GatheringSaveStore.ReadOne(path);Check(world.Buildings.Count==0,"Restart did not create fresh world");
        Check(saves.Load(path).Accepted,"Disk checkpoint unavailable after restart");var wait=Until(()=>!saves.Loading,10,"restart restore");while(wait.MoveNext())yield return wait.Current;
        SameEconomy(expected);Check(world.Buildings.Any(b=>b.Complete)&&world.Buildings.Any(b=>b.Cancelled),"Restart lost building records");
        checks.Add("actual Play Mode exit/re-entry restores disk checkpoint, completed/cancelled buildings, refund and task IDs");
        var partialPath=Path.Combine(DirectoryPath,"half-built-restart.json");var partial=GatheringSaveStore.ReadOne(partialPath);
        Check(saves.Load(partialPath).Accepted,"Cannot restore half-built restart checkpoint");wait=Until(()=>!saves.Loading,10,"restart half-built restore");while(wait.MoveNext())yield return wait.Current;
        SameEconomy(partial);Check(world.Buildings[0].Labour>0&&!world.Buildings[0].Complete,"Half-built checkpoint invalid");world.Paused=false;
        wait=Until(()=>world.Buildings[0].Complete,40,"half-built checkpoint completes after actual restart");while(wait.MoveNext())yield return wait.Current;
        Check(world.DepotBayCount==5,"Restart completion duplicated depot");checks.Add("half-built checkpoint resumes labour after actual Play Mode restart and completes once");
    }
}

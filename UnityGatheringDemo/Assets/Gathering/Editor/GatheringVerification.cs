using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using GatheringDemo;

public static class GatheringVerification
{
    public static string SuiteStatus { get; private set; }="not started";
    private static readonly List<string> results=new List<string>();
    private static bool running;
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    public static string SettingsCheck()
    {
        var s=ScriptableObject.CreateInstance<GatheringSettings>();
        try
        {
            Require(s.ValidateSettings()==""&&s.Batch(ResourceKind.Wood,100)==6&&s.Batch(ResourceKind.Ore,100)==6&&s.Batch(ResourceKind.Food,2)==2,"Batch/capacity mismatch");
            Require(s.Speed(0)>s.Speed(3)&&s.Speed(3)>s.Speed(12)&&s.Speed(12)>s.Speed(18),"Weight/speed mismatch");
            Require(Mathf.Abs(s.Speed(18)-2.03f)<.001f&&Mathf.Abs(s.GatherTime(WorkKind.Chop,6)-2.2f)<.001f,"Web rule mismatch");
            var serialized=new SerializedObject(s);serialized.FindProperty("maxWeight").floatValue=5;serialized.ApplyModifiedPropertiesWithoutUndo();
            Require(s.Batch(ResourceKind.Ore,100)==1&&s.Batch(ResourceKind.Wood,100)==2&&s.Batch(ResourceKind.Food,100)==6,"Weight cap vs item cap mismatch");
            return "PASS default rules, partial batch, item/weight caps, weight/speed and gather time";
        }
        finally {UnityEngine.Object.DestroyImmediate(s);}
    }
    public static string Snapshot()
    {
        var w=UnityEngine.Object.FindFirstObjectByType<GatheringWorld>();if(!w)return "No running world";
        CheckConservation(w);
        return $"t={w.Elapsed:0.00}; workers={w.Workers.Count}; remaining={w.Resources.Sum(s=>s.Stock)}; cargo={w.Workers.Sum(u=>u.CargoCount)}; inventory={w.Inventory.Sum()}; blocked={w.Workers.Count(u=>u.Phase==WorkerPhase.Blocked)}; conserved=true";
    }
    private static void CheckConservation(GatheringWorld w)
    {
        foreach(ResourceKind kind in Enum.GetValues(typeof(ResourceKind)))
        {
            Require(w.AccountedResources(kind)==w.InitialResources(kind),$"{kind} conservation failed");
        }
        foreach(var r in w.Resources)Require(r.Reserved>=0&&r.Reserved<=r.Stock,$"Invalid reservations: {r.DisplayName}");
    }
    public static string StartSuite()
    {
        Require(EditorApplication.isPlaying,"Enter Play mode first");Require(!running,"Suite already running");
        var world=UnityEngine.Object.FindFirstObjectByType<GatheringWorld>();
        Require(world&&world.Buildings.Count==0&&world.Inventory.Sum()==0&&world.Resources.All(r=>r.Stock==r.Initial)&&world.Workers.All(w=>w.CargoCount==0&&w.Phase==WorkerPhase.Idle),"Run suite from an untouched world");
        results.Clear();running=true;SuiteStatus="RUNNING";world.StartCoroutine(Guarded(Run(world)));return SuiteStatus;
    }
    private static IEnumerator Guarded(IEnumerator steps)
    {
        while(true)
        {
            object next;
            try {if(!steps.MoveNext())break;next=steps.Current;}
            catch(Exception ex){SuiteStatus="FAIL: "+ex.Message;Debug.LogError(SuiteStatus+"\n"+ex);running=false;yield break;}
            yield return next;
        }
        running=false;SuiteStatus="PASS: "+string.Join(" | ",results);Debug.Log(SuiteStatus);
    }
    // Flatten nested test enumerators so exceptions are caught by Guarded as well.
    private static IEnumerator Until(GatheringWorld world,Func<bool> condition,float timeout,string title)
    {
        float started=world.Elapsed,watchdog=Time.realtimeSinceStartup;
        while(!condition())
        {
            CheckConservation(world);CheckMetrics(world);Require(world.Elapsed-started<timeout&&Time.realtimeSinceStartup-watchdog<timeout*3,"Timed out: "+title);yield return null;
        }
        CheckConservation(world);CheckMetrics(world);
    }
    private static void CheckMetrics(GatheringWorld world)
    {
        // The suite starts fresh and never resets the observation window.
        if(world.Buildings.Count==0)Require(world.Metrics.Items==world.Inventory.Sum(),"Diagnostic deposits and world inventory diverged");
        Require(world.Metrics.Trips==world.CompletedTrips,"Diagnostic completed trips diverged");
    }
    private static IEnumerator Run(GatheringWorld world)
    {
        results.Add(SettingsCheck());
        var isolated=new GatheringMetrics();
        isolated.Deposit(1,ResourceKind.Wood,6,12);
        isolated.Tick(10,Array.Empty<GatheringWorker>(),false);
        Require(Math.Abs(isolated.RecentItemsPerMinute-36)<.01,"Warmup throughput denominator mismatch");
        isolated.Tick(5,Array.Empty<GatheringWorker>(),true);
        Require(isolated.Elapsed==10,"Paused diagnostics advanced");
        isolated.Tick(20,Array.Empty<GatheringWorker>(),false);
        Require(isolated.RecentItemsPerMinute==0&&isolated.Items==6,"Rolling window lost all-time data or retained expired delivery");
        results.Add("metrics rolling window, startup denominator and pause exclusion");
        while(world.RoutesSettling)yield return null;
        var c=world.GetComponent<GatheringController>();var w=world.Workers[0];var other=world.Workers[1];var wood=world.Resources[0];
        foreach(var worker in world.Workers)worker.Stop();
        c.Select(new[]{w,other});w.Work(wood);other.Work(wood);
        c.OrderMove(new Vector3(50,0,50));Require(c.LastAccepted==0&&c.LastRejected==2&&w.Source==wood&&other.Source==wood,"Rejected move changed task or feedback counts");
        other.Stop();w.Stop();c.Select(Array.Empty<GatheringWorker>());c.OrderMove(Vector3.zero);Require(c.Feedback.Contains("没有选中"),"No-selection hint missing");
        results.Add("rejected orders preserve task + accurate counts + no-selection guidance");
        int inventory=world.Inventory.Sum();Require(w.Work(wood).Accepted,"Cannot start continuous gathering");
        var target=new Vector3(-7.5f,0,4.7f);Require(w.Move(target,true).Accepted,"Cannot queue movement");
        Require(w.Move(new Vector3(-6,0,4),true).Accepted&&w.RemoveOrder(1).Accepted&&w.QueueCount==1,"Cancel queued order failed");
        var wait=Until(world,()=>w.Phase==WorkerPhase.Idle&&w.QueueCount==0,35,"continuous gather -> queued move");while(wait.MoveNext())yield return wait.Current;
        Require(world.Inventory.Sum()==inventory+6&&Vector3.Distance(w.transform.position,target)<.3f,"Continuous gathering did not hand off after one delivery");
        results.Add("continuous gather hands off after one delivery; individual queue cancel");
        inventory=world.Inventory.Sum();Require(w.Work(wood,false,true).Accepted,"Cannot start one-trip gathering");
        wait=Until(world,()=>w.CargoCount>0,25,"harvest cargo");while(wait.MoveNext())yield return wait.Current;
        int cargo=w.CargoCount;float weight=w.CargoWeight;w.Stop();
        Require(w.CargoCount==cargo&&Mathf.Approximately(w.CargoWeight,weight)&&w.Phase==WorkerPhase.Idle,"Stop lost cargo");
        Require(w.Work(world.Resources[2],false,true).Accepted,"Cargo reassignment rejected");
        wait=Until(world,()=>w.Phase==WorkerPhase.Idle&&w.CargoCount==0,40,"cargo reassignment before one-trip mining");while(wait.MoveNext())yield return wait.Current;
        Require(world.Inventory.Sum()==inventory+cargo+6,"Reassignment skipped the new one-trip task");
        results.Add("cargo stop and reassignment preserve goods; one-trip finishes exactly once");
        w.Work(wood);w.Move(new Vector3(-6,0,4),true);w.ClearOrders();Require(w.Source==wood&&w.QueueCount==0,"Clearing queue cancelled current work");
        w.FinishTrip();
        wait=Until(world,()=>w.Phase==WorkerPhase.Idle&&w.CargoCount==0,35,"finish trip");while(wait.MoveNext())yield return wait.Current;
        results.Add("clear future queue preserves current work; finish-trip returns and stops");
        w.Work(wood,false,true);
        wait=Until(world,()=>w.CargoCount>0&&w.transform.position.x>1,30,"cargo on resource side");while(wait.MoveNext())yield return wait.Current;
        w.Stop();cargo=w.CargoCount;world.ToggleRoadblock();Require(world.Roadblock,"Could not create roadblock");
        yield return new WaitForSeconds(.7f);w.Deliver();
        wait=Until(world,()=>w.Phase==WorkerPhase.Blocked,8,"delivery blocked");while(wait.MoveNext())yield return wait.Current;
        Require(w.CargoCount==cargo&&w.BlockReason.Contains("仓库"),"Blocked delivery lost cargo or reason");
        yield return new WaitForSeconds(10.5f);Require(w.RetryCount==3&&w.Phase==WorkerPhase.Blocked,"Expected 3 automatic retries");
        world.ToggleRoadblock();
        wait=Until(world,()=>w.Phase==WorkerPhase.Idle&&w.CargoCount==0,30,"automatic recovery after exhausted retries");while(wait.MoveNext())yield return wait.Current;
        results.Add("blocked cargo retained; three retries; route change automatically recovers");
        int before=world.Inventory.Sum();bool sawWait=false;
        foreach(var worker in world.Workers){worker.ContinueSameKind=false;Require(worker.Work(wood,false,true).Accepted,"Multiworker assignment failed");}
        wait=Until(world,()=>world.Workers.All(u=>u.Phase==WorkerPhase.Idle&&u.CargoCount==0),65,"12 workers one trip");
        while(wait.MoveNext()){sawWait|=world.Workers.Any(u=>u.Phase==WorkerPhase.WaitingWork||u.Phase==WorkerPhase.WaitingDepot);yield return wait.Current;}
        Require(sawWait&&world.Inventory.Sum()==before+72,"Multiworker queue or delivery count mismatch");
        Require(wood.Reserved==0,"Reserved output leaked");results.Add("12 workers queued and delivered 72 items; per-kind conservation throughout");
        c.Select(new[]{w});world.Notice="流程检查通过，可继续操作";
    }
    public static void StartCollect()
    {
        var world=UnityEngine.Object.FindFirstObjectByType<GatheringWorld>();foreach(var w in world.Workers.Take(4))w.Work(world.Resources[0]);
    }
}

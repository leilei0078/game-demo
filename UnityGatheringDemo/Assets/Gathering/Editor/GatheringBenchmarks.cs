using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using GatheringDemo;

// Runs the real NavMesh/worker state machine in a fresh scene for every case.
public static class GatheringBenchmarks
{
    [Serializable] public sealed class Case
    {
        public string name;
        public int workers=1,resourceIndex=0,expectedBatch=6;
        public float weight=2,walkSpeed=2.9f,gatherRate=1;
    }
    [Serializable] public sealed class Result
    {
        public Case scenario;
        public GatheringMetrics.Report measured;
        public double deliverySeconds,itemsPerMinuteDuringDelivery;
        public float fullLoadSpeed,gatherSecondsPerBatch;
    }
    [Serializable] public sealed class Output
    {
        public string status,recordedUtc;
        public string method="Fresh Unity scene per case; one trip per assigned worker; starts at order, ends at last deposit. No warmup, return-home time or offline income. Same layout/spawns; real NavMesh acceleration/avoidance. Rates compare this finite delivery window, not steady-state throughput.";
        public List<Result> results;
    }
    public static string Status {get;private set;}="not started";
    private static IEnumerator run;
    private static readonly List<Result> results=new List<Result>();
    private static readonly Case[] cases={
        new Case{name="wood-1-default"},new Case{name="wood-1-light",weight=1},new Case{name="wood-1-heavy",weight=3},
        new Case{name="wood-1-weight-cap",weight=6,expectedBatch=3},
        new Case{name="wood-1-fast-gather",gatherRate=2},new Case{name="wood-1-fast-walk",walkSpeed=4},
        new Case{name="wood-4-default",workers=4},new Case{name="wood-12-default",workers=12},
        new Case{name="ore-1-default",resourceIndex=2,weight=3},new Case{name="food-1-default",resourceIndex=3,weight=.5f}
    };
    private static string outputDirectory;
    public static string Start()
    {
        if(run!=null||!EditorApplication.isPlaying)throw new InvalidOperationException("Start in Play Mode with no benchmark running");
        outputDirectory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../output/unity-gathering"));Directory.CreateDirectory(outputDirectory);
        results.Clear();Status="RUNNING";run=Execute();EditorApplication.update+=Tick;return Status;
    }
    private static void Tick()
    {
        try {if(!EditorApplication.isPlaying)throw new Exception("Play Mode ended during benchmark");if(run.MoveNext())return;Finish("PASS");}
        catch(Exception ex){Debug.LogError("BENCHMARK FAIL: "+ex);Finish("FAIL: "+ex.Message);}
    }
    private static void Finish(string state)
    {
        EditorApplication.update-=Tick;run=null;Status=state;
        var report=new Output{status=state,recordedUtc=DateTime.UtcNow.ToString("O"),results=results};
        File.WriteAllText(Path.Combine(outputDirectory,"efficiency-benchmarks.json"),JsonUtility.ToJson(report,true));
        var csv=new StringBuilder("case,workers,batch,weight_kg,full_load_speed_mps,gather_rate,delivery_seconds,items,items_per_min,travel_worker_s,gather_worker_s,work_queue_worker_s,depot_queue_worker_s,unload_worker_s,blocked_worker_s\n");
        foreach(var r in results)
        {
            var m=r.measured;csv.AppendLine(FormattableString.Invariant($"{r.scenario.name},{r.scenario.workers},{r.scenario.expectedBatch},{r.scenario.weight},{r.fullLoadSpeed},{r.scenario.gatherRate},{r.deliverySeconds:F3},{m.deliveredItems},{r.itemsPerMinuteDuringDelivery:F3},{m.travelSeconds:F3},{m.gatherSeconds:F3},{m.workQueueSeconds:F3},{m.depotQueueSeconds:F3},{m.unloadSeconds:F3},{m.blockedSeconds:F3}"));
        }
        File.WriteAllText(Path.Combine(outputDirectory,"efficiency-benchmarks.csv"),csv.ToString());Debug.Log("BENCHMARK "+Status+"; "+results.Count+" cases");
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private static IEnumerator Execute()
    {
        foreach(var scenario in cases)
        {
            Status=$"RUNNING {results.Count+1}/{cases.Length}: {scenario.name}";
            var load=SceneManager.LoadSceneAsync("GatheringDemo",LoadSceneMode.Single);while(!load.isDone)yield return null;
            var world=UnityEngine.Object.FindFirstObjectByType<GatheringWorld>();
            Check(world,"No benchmark world");while(world.RoutesSettling)yield return null;
            var originalSettings=world.Settings;var settings=UnityEngine.Object.Instantiate(originalSettings);world.SetSettings(settings);
            var so=new SerializedObject(settings);so.FindProperty("walkSpeed").floatValue=scenario.walkSpeed;so.FindProperty("gatherRate").floatValue=scenario.gatherRate;
            so.FindProperty(scenario.resourceIndex==2?"oreWeight":scenario.resourceIndex==3?"foodWeight":"woodWeight").floatValue=scenario.weight;so.ApplyModifiedPropertiesWithoutUndo();
            Check(settings.ValidateSettings()=="","Invalid benchmark settings");
            // Only timing and ordering are controlled; movement/queues/harvest/deposit execute normally.
            world.GetComponent<GatheringController>().enabled=false;world.ResetMetrics();
            var source=world.Resources[scenario.resourceIndex];
            foreach(var worker in world.Workers.Take(scenario.workers))Check(worker.Work(source,false,true).Accepted,"Assignment rejected");
            int expected=scenario.workers*scenario.expectedBatch;double start=world.Elapsed;
            while(world.Metrics.Trips<scenario.workers)
            {
                Check(world.Elapsed-start<75,"Delivery timed out: "+scenario.name);
                foreach(ResourceKind kind in Enum.GetValues(typeof(ResourceKind)))
                    Check(world.Resources.Where(r=>r.Kind==kind).Sum(r=>r.Stock)+world.Workers.Where(w=>w.CargoKind==kind).Sum(w=>w.CargoCount)+world.Inventory[(int)kind]==world.Resources.Where(r=>r.Kind==kind).Sum(r=>r.Initial),"Resource conservation failed");
                yield return null;
            }
            var measured=world.Metrics.Snapshot(world.Workers.Sum(w=>w.CargoCount));
            Check(measured.deliveredItems==expected&&world.Inventory.Sum()==expected,"Unexpected delivery count");
            Check(Math.Abs(measured.deliveredKg-expected*scenario.weight)<.01,"Delivered weight mismatch");
            Check(measured.deliveredByKind[(int)source.Kind]==expected,"Wrong resource credited");
            Check(Math.Abs(measured.gatherSeconds-scenario.workers*settings.GatherTime(source.Work,scenario.expectedBatch))<.1,"Observed gather time diverged from configured batch time");
            double sum=measured.travelSeconds+measured.gatherSeconds+measured.workQueueSeconds+measured.depotQueueSeconds+measured.unloadSeconds+measured.blockedSeconds+measured.idleSeconds;
            Check(Math.Abs(sum-measured.elapsedSeconds*12)<.03,"Worker time accounting mismatch");
            Check(measured.blockedSeconds==0,"Unexpected blocked route in unobstructed benchmark");
            results.Add(new Result{scenario=scenario,measured=measured,deliverySeconds=measured.elapsedSeconds,
                itemsPerMinuteDuringDelivery=measured.itemsPerMinute,fullLoadSpeed=settings.Speed(scenario.expectedBatch*scenario.weight),gatherSecondsPerBatch=settings.GatherTime(source.Work,scenario.expectedBatch)});
            // Pause accounting and reset must not mutate game inventory or carry.
            world.Paused=true;double frozen=world.Metrics.Elapsed;float until=Time.realtimeSinceStartup+.25f;
            while(Time.realtimeSinceStartup<until)yield return null;
            Check(world.Metrics.Elapsed==frozen,"Pause counted as production time");
            world.ResetMetrics();Check(world.Metrics.Items==0&&world.Metrics.Elapsed==0&&world.Inventory.Sum()==expected,"Reset modified stock or failed to clear window");
            world.Paused=false;world.SetSettings(originalSettings);UnityEngine.Object.Destroy(settings);
        }
        var baseline=results[0];
        Check(results[1].deliverySeconds<results[2].deliverySeconds,"Lighter cargo did not finish faster");
        Check(results[4].deliverySeconds<baseline.deliverySeconds,"Faster gathering did not reduce trip time");
        Check(results[5].deliverySeconds<baseline.deliverySeconds,"Faster walking did not reduce trip time");
        Check(results[7].measured.workQueueSeconds>results[0].measured.workQueueSeconds,"Crowd did not produce work queue waiting");
        var reset=SceneManager.LoadSceneAsync("GatheringDemo",LoadSceneMode.Single);while(!reset.isDone)yield return null;
    }
}

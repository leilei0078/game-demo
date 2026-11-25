using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GatheringDemo;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public static class ConstructionApproachVerification
{
    public static string Status {get;private set;}="not started";
    private static GatheringWorld world;
    private static IEnumerator run;
    private static readonly List<string> checks=new List<string>();
    private static string Dir=>Path.GetFullPath(Application.dataPath+"/../../output/unity-construction-approach");
    [Serializable]private sealed class Report{public string status;public string[] checks;public int[] accounted;}
    private static void Require(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
    public static string Start()
    {
        Require(EditorApplication.isPlaying&&run==null,"Start once in Play Mode");world=UnityEngine.Object.FindFirstObjectByType<GatheringWorld>();
        checks.Clear();Status="RUNNING";run=Execute();EditorApplication.update+=Tick;return Status;
    }
    private static void Tick()
    {
        try
        {
            Require(EditorApplication.isPlaying,"Play Mode ended");
            if(world.Ready)
            {
                foreach(ResourceKind kind in Enum.GetValues(typeof(ResourceKind)))Require(world.AccountedResources(kind)==world.InitialResources(kind),"Conservation "+kind);
                foreach(var site in world.Buildings)
                {
                    Require(site.Constructing<=2,"Builder cap");
                    for(int k=0;k<2;k++)Require(site.Delivered[k]+site.InTransit((ResourceKind)k)<=site.MaterialCost((ResourceKind)k),"Oversupply");
                }
            }
            if(run.MoveNext())return;Finish("PASS");
        }
        catch(Exception ex){Debug.LogError("APPROACH "+ex);Finish("FAIL: "+ex.Message);}
    }
    private static void Finish(string status)
    {
        EditorApplication.update-=Tick;run=null;Status=status;
        Directory.CreateDirectory(Dir);File.WriteAllText(Path.Combine(Dir,"verification.json"),JsonUtility.ToJson(new Report{status=status,checks=checks.ToArray(),accounted=Enumerable.Range(0,3).Select(k=>world.AccountedResources((ResourceKind)k)).ToArray()},true));
        world.GetComponent<GatheringController>().enabled=true;Debug.Log("APPROACH "+status);
    }
    private static IEnumerator Load(string path)
    {
        Require(world.GetComponent<GatheringSaveService>().Load(path).Accepted,"Load "+path);
        while(!world.Ready||world.GetComponent<GatheringSaveService>().Loading||world.RoutesSettling)yield return null;
        world.GetComponent<GatheringController>().enabled=false;world.GuideHidden=true;world.Paused=false;
    }
    private static IEnumerator Fresh()
    {var wait=Load(Path.GetFullPath(Application.dataPath+"/../../output/unity-polish/initial-v2.json"));while(wait.MoveNext())yield return null;}
    private static IEnumerator Until(Func<bool> condition,float seconds,string label)
    {Status="RUNNING: "+label;float start=world.Elapsed,watch=Time.realtimeSinceStartup;while(!condition()){Require(world.Elapsed-start<seconds&&Time.realtimeSinceStartup-watch<seconds*3,"Timeout "+label);yield return null;}}
    private static void SupplyFixture(ConstructionSite site)
    {
        // Path-selection cases use conserved fixture materials; end-to-end hauling is tested separately below.
        for(int k=0;k<2;k++){int count=site.MaterialCost((ResourceKind)k);if(count==0)continue;world.Resources.First(r=>(int)r.Kind==k&&r.Stock>=count).Harvest(count);site.Receive((ResourceKind)k,count);}
    }
    private static ConstructionSite Place(BuildingKind kind,Vector3 point,int rotation)
    {
        if(kind==BuildingKind.Stockyard){Require(world.PlaceBuilding(point,rotation,out var site).Accepted,"Place stockyard");return site;}
        var result=world.PlaceInfrastructure(point,point,kind,rotation,out var sites);Require(result.Accepted,"Place "+kind+": "+result.Reason);return sites.Single();
    }
    private static void Warp(GatheringWorker worker,Vector3 position)
    {Require(worker.Stop().Accepted,"Stop before warp");Require(worker.GetComponent<NavMeshAgent>().Warp(position),"Warp");}
    private static IEnumerator Execute()
    {
        var wait=Fresh();while(wait.MoveNext())yield return null;
        // Check four approaches for every type, including rotated stockyard footprint.
        foreach(var kind in new[]{BuildingKind.Stockyard,BuildingKind.Road,BuildingKind.Wall})
        {
            wait=Fresh();while(wait.MoveNext())yield return null;var site=Place(kind,new Vector3(11,0,-7),kind==BuildingKind.Stockyard?1:0);var center=site.transform.position;
            while(world.RoutesSettling)yield return null;SupplyFixture(site);world.Paused=true;
            var worker=world.Workers[0];
            foreach(var side in new[]{Vector3.forward,Vector3.back,Vector3.left,Vector3.right})
            {
                world.Paused=false;Warp(worker,center+side*3.8f);Require(worker.Build(site).Accepted,"Assign "+kind);site.TryWorker(worker);world.Paused=true;
                Require(worker.Phase==WorkerPhase.ToSite,"No selected approach "+kind);
                var destination=worker.GetComponent<NavMeshAgent>().destination;
                Require(Vector3.Dot(destination-center,side)>.5f,"Unnecessary far-side detour "+kind+" "+side+" -> "+destination);
            }
            world.Paused=false;worker.Stop();checks.Add(kind+": north/south/east/west approach chooses the same accessible near side (stockyard rotated 90 degrees)");
        }
        wait=Fresh();while(wait.MoveNext())yield return null;
        var stockyard=Place(BuildingKind.Stockyard,new Vector3(11,0,-7),0);while(world.RoutesSettling)yield return null;
        var a=world.Workers[0];Warp(a,new Vector3(11,0,-11));Require(a.Build(stockyard).Accepted,"Missing material assign");stockyard.TryWorker(a);world.Paused=true;
        Require(a.GetComponent<NavMeshAgent>().destination.z<stockyard.transform.position.z,"Missing materials sent worker behind site");
        world.Paused=false;SupplyFixture(stockyard);a.Stop();
        for(int i=0;i<3;i++){var w=world.Workers[i];Warp(w,new Vector3(10+i,0,-11));Require(w.Build(stockyard).Accepted,"Three builder assign");stockyard.TryWorker(w);}
        Require(world.Workers.Count(w=>w.BuildTarget==stockyard&&w.Phase==WorkerPhase.ToSite)==2,"Two-person approach cap");
        wait=Until(()=>world.Workers.Count(w=>w.Phase==WorkerPhase.Constructing)==2,15,"two workers physically reach near side");while(wait.MoveNext())yield return null;
        Require(world.Workers.Where(w=>w.Phase==WorkerPhase.Constructing).All(w=>w.transform.position.z<stockyard.transform.position.z),"Builder physically on far side");
        checks.Add("missing-material waiting stays near approach; three assigned workers reserve only two work positions and physically construct on near side");
        // Block the closest side after assignment. A new task must find another side rather than reject or clip through.
        foreach(var w in world.Workers.Take(3))w.Stop();
        var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);blocker.name="Approach verification temporary obstacle";blocker.transform.position=stockyard.transform.position+new Vector3(0,.5f,-2.5f);blocker.transform.localScale=new Vector3(4,1,1.2f);
        var obstacle=blocker.AddComponent<NavMeshObstacle>();obstacle.shape=NavMeshObstacleShape.Box;obstacle.size=Vector3.one;obstacle.carving=true;obstacle.carveOnlyStationary=false;world.RoutesChanged();while(world.RoutesSettling)yield return null;
        Warp(a,new Vector3(11,0,-11.5f));Require(a.Build(stockyard).Accepted,"Fallback assignment");stockyard.TryWorker(a);world.Paused=true;
        Require(a.Phase==WorkerPhase.ToSite&&Mathf.Abs(a.GetComponent<NavMeshAgent>().destination.z-stockyard.BuildPoint(2).z)>.2f,"Blocked nearest side still selected");
        checks.Add("blocked nearest side selects another reachable construction position");
        UnityEngine.Object.Destroy(blocker);world.Paused=false;a.Stop();
        wait=Fresh();while(wait.MoveNext())yield return null;
        // Actual warehouse -> carried materials -> near-side delivery -> construction, including save/load in transit.
        var wood=world.Resources.First(r=>r.Work==WorkKind.Chop);Require(world.Workers[5].Work(wood,false,true).Accepted,"Gather haul materials");
        wait=Until(()=>world.Inventory[0]>=6,70,"real wood delivery");while(wait.MoveNext())yield return null;
        var result=world.PlaceInfrastructure(new Vector3(-13,0,8),new Vector3(-9,0,8),BuildingKind.Wall,0,out var wall);Require(result.Accepted,result.Reason);while(world.RoutesSettling)yield return null;
        var builder=world.Workers[0];foreach(var segment in wall.Take(3))Require(builder.Build(segment,builder.BuildTarget!=null).Accepted,"Queue walls");
        wait=Until(()=>builder.Phase==WorkerPhase.ToSite&&builder.CargoCount>0,30,"wall cargo approach");while(wait.MoveNext())yield return null;
        Require(builder.GetComponent<NavMeshAgent>().destination.z<8,"Warehouse side delivery detour");world.Paused=true;
        Directory.CreateDirectory(Dir);string checkpoint=Path.Combine(Dir,"near-side-transit-v4.json");Require(world.GetComponent<GatheringSaveService>().Save(checkpoint).Accepted,"Save transit");
        wait=Load(checkpoint);while(wait.MoveNext())yield return null;world.Paused=false;builder=world.Workers[0];
        wait=Until(()=>builder.Phase==WorkerPhase.ToSite,5,"restored approach");while(wait.MoveNext())yield return null;Require(builder.GetComponent<NavMeshAgent>().destination.z<8,"Restored side detour");
        wait=Until(()=>world.Buildings.Take(3).All(b=>b.Complete),85,"three contiguous wall segments finish");while(wait.MoveNext())yield return null;
        Require(world.DepotBayCount==3,"Wall created depot entrance");checks.Add("actual harvested wood hauled from warehouse to near side; transit save/load selects near side; three queued contiguous wall segments physically complete with conservation");
        world.Paused=true;world.SetCameraFocus(new Vector3(-11,0,6));world.View.orthographicSize=7;
    }
}

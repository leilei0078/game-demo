using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using GatheringDemo;

public static class MouseInteractionVerification
{
    private static IEnumerator run;private static GatheringWorld world;private static GatheringController controller;private static Mouse mouse;private static readonly List<string> checks=new List<string>();
    public static string Status {get;private set;}="not started";
    private static void Require(bool ok,string text){if(!ok)throw new Exception(text);}
    public static string Start()
    {
        Require(EditorApplication.isPlaying&&run==null,"Start once in Play Mode");world=UnityEngine.Object.FindFirstObjectByType<GatheringWorld>();controller=world.GetComponent<GatheringController>();
        mouse=Mouse.current;Require(mouse!=null,"No mouse");checks.Clear();run=Execute();Status="RUNNING";EditorApplication.update+=Tick;return Status;
    }
    private static void Tick(){try{if(run.MoveNext())return;Finish("PASS");}catch(Exception e){Debug.LogError("MOUSE "+e);Finish("FAIL: "+e.Message);}}
    private static void Finish(string status)
    {EditorApplication.update-=Tick;run=null;Status=status;var dir=Path.GetFullPath(Application.dataPath+"/../../output/unity-right-drag");Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"mouse-verification.json"),JsonUtility.ToJson(new Report{status=status,checks=checks.ToArray()},true));Debug.Log("MOUSE "+status);}
    [Serializable]private sealed class Report{public string status;public string[] checks;}
    private static IEnumerator Send(Vector2 p,ushort buttons=0,float scroll=0)
    {InputSystem.QueueStateEvent(mouse,new MouseState{position=p,buttons=buttons,scroll=new Vector2(0,scroll)});int until=Time.frameCount+3;while(Time.frameCount<until)yield return null;}
    private static IEnumerator Execute()
    {
        world.Paused=true;controller.enabled=true;
        var load=world.GetComponent<GatheringSaveService>();Require(load.Load(Path.GetFullPath(Application.dataPath+"/../../output/unity-polish/initial-v2.json")).Accepted,"Initial load");while(load.Loading||world.RoutesSettling)yield return null;
        world.Paused=true;world.GuideHidden=true;world.SetCameraFocus(Vector3.zero);world.View.orthographicSize=10.5f;
        var wait=Send(new Vector2(world.View.pixelWidth*.6f,world.View.pixelHeight*.5f));while(wait.MoveNext())yield return null;
        Vector2 Screen(Vector3 p)=>world.View.WorldToScreenPoint(p);
        // A real Input System mouse event stream, distinct from direct command calls.
        var worker=world.Workers[0];var p=Screen(worker.transform.position+Vector3.up*.6f);
        wait=Send(p,1);while(wait.MoveNext())yield return null;wait=Send(p);while(wait.MoveNext())yield return null;
        Require(controller.Selected.Count==1&&controller.Selected[0]==worker,"Point select");checks.Add("mouse down/up selects the visible worker");
        var min=world.Workers.Select(w=>Screen(w.transform.position+Vector3.up*.6f)).Aggregate(Vector2.Min)-Vector2.one*20;
        var max=world.Workers.Select(w=>Screen(w.transform.position+Vector3.up*.6f)).Aggregate(Vector2.Max)+Vector2.one*20;
        wait=Send(min,1);while(wait.MoveNext())yield return null;wait=Send(max,1);while(wait.MoveNext())yield return null;wait=Send(max);while(wait.MoveNext())yield return null;
        Require(controller.Selected.Count==12,"Box selection");checks.Add("box drag selects all twelve workers");
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        var appendToggle=typeof(GatheringController).GetField("appendMode",flags);
        var empty=Screen(new Vector3(-12,0,0));
        foreach(bool queueAppend in new[]{false,true})
        {
            appendToggle.SetValue(controller,queueAppend);
            wait=Send(empty,1);while(wait.MoveNext())yield return null;wait=Send(empty);while(wait.MoveNext())yield return null;
            Require(controller.Selected.Count==0,"Paused empty click did not clear selection");
            wait=Send(min,1);while(wait.MoveNext())yield return null;wait=Send(max,1);while(wait.MoveNext())yield return null;wait=Send(max);while(wait.MoveNext())yield return null;
            Require(controller.Selected.Count==12,"Queue toggle affected box selection");
            wait=Send(p,1);while(wait.MoveNext())yield return null;wait=Send(p);while(wait.MoveNext())yield return null;
            Require(controller.Selected.Count==1&&controller.Selected[0]==worker,"Queue toggle affected single selection");
            controller.Select(world.Workers);
        }
        checks.Add("paused box selection clears on left-click empty ground and becomes one worker on left-click model, with queue toggle off or on");
        // Use the actual label rect after its overlap avoidance, rather than guessing from the worker's world position.
        var labelWorker=world.Workers[3];
        var labels=(Dictionary<GatheringWorker,Rect>)typeof(GatheringController).GetField("workerLabels",flags).GetValue(controller);
        Require(labels.ContainsKey(labelWorker),"Worker label not drawn");
        var label=labels[labelWorker];float scale=(float)typeof(GatheringController).GetField("uiScale",flags).GetValue(controller);
        var labelPoint=new Vector2(label.center.x*scale,world.View.pixelHeight-label.center.y*scale);
        wait=Send(labelPoint,1);while(wait.MoveNext())yield return null;wait=Send(labelPoint);while(wait.MoveNext())yield return null;
        Require(controller.Selected.Count==1&&controller.Selected[0]==labelWorker,"Head label selected wrong worker");
        checks.Add("left-click the displayed head label selects its worker, including an avoided/moved label");
        world.Paused=false;
        Require(worker.Work(world.Resources[0]).Accepted&&worker.Move(new Vector3(-12,0,0),true).Accepted,"Task-preservation fixture");
        var task=worker.TaskId;var queued=worker.Orders[0].taskId;var taskSource=worker.Source;
        controller.Select(world.Workers);
        wait=Send(empty,1);while(wait.MoveNext())yield return null;wait=Send(empty);while(wait.MoveNext())yield return null;
        Require(controller.Selected.Count==0&&worker.TaskId==task&&worker.Source==taskSource&&worker.Orders[0].taskId==queued,"Running empty click changed task or queue");
        controller.Select(world.Workers);
        var other=world.Workers[1];var otherPoint=Screen(other.transform.position+Vector3.up*.6f);
        wait=Send(otherPoint,1);while(wait.MoveNext())yield return null;wait=Send(otherPoint);while(wait.MoveNext())yield return null;
        Require(controller.Selected.Count==1&&controller.Selected[0]==other&&worker.TaskId==task&&worker.Source==taskSource&&worker.Orders[0].taskId==queued,"Running single-select changed another worker's task");
        checks.Add("running deselect/single-select preserve the gatherer's current task ID, source and queued task ID");
        // Right-click remains the explicit movement action; the mouse queue toggle still appends orders.
        controller.Select(new[]{worker});
        var movePoint=Screen(new Vector3(-12,0,-4));
        wait=Send(movePoint,2);while(wait.MoveNext())yield return null;
        Require(worker.TaskId==task&&worker.QueueCount==1,"Right press issued an order before release");
        wait=Send(movePoint+Vector2.one);while(wait.MoveNext())yield return null;
        Require(worker.TaskId==task&&worker.QueueCount==2,"Right-click queue append regressed");
        appendToggle.SetValue(controller,false);
        wait=Send(movePoint,2);while(wait.MoveNext())yield return null;wait=Send(movePoint);while(wait.MoveNext())yield return null;
        Require(worker.Phase==WorkerPhase.Moving&&worker.QueueCount==0&&worker.TaskId!=task,"Right-click move regressed");
        checks.Add("right-click ground moves; queue toggle appends movement without changing selection semantics");
        // Recreate a running task and queue; dragging must not move workers, even after returning to the starting pixel.
        Require(worker.Work(world.Resources[0]).Accepted&&worker.Move(new Vector3(-12,0,0),true).Accepted,"Right drag task fixture");
        task=worker.TaskId;queued=worker.Orders[0].taskId;taskSource=worker.Source;
        var dragOrigin=Screen(new Vector3(-12,0,0));var dragEnd=dragOrigin+new Vector2(-140,40);var dragFocus=world.CameraFocus;
        wait=Send(dragOrigin,2);while(wait.MoveNext())yield return null;
        Require(worker.TaskId==task&&world.CameraFocus==dragFocus,"Right press changed task/camera");
        wait=Send(dragEnd,2);while(wait.MoveNext())yield return null;
        Require(Vector3.Distance(world.CameraFocus,dragFocus)>1,"Right drag did not pan");
        wait=Send(dragOrigin,2);while(wait.MoveNext())yield return null;wait=Send(dragOrigin);while(wait.MoveNext())yield return null;
        Require(worker.TaskId==task&&worker.Source==taskSource&&worker.QueueCount==1&&worker.Orders[0].taskId==queued&&controller.Selected.Count==1&&controller.Selected[0]==worker,"Right drag changed selection/task/queue");
        checks.Add("right drag pans while running, preserves selection/task/queue and never orders when dragged back to its starting point");
        world.Paused=true;controller.Select(Array.Empty<GatheringWorker>());dragFocus=world.CameraFocus;
        wait=Send(dragOrigin,2);while(wait.MoveNext())yield return null;
        // A very fast drag can arrive as a moved release without an intermediate held event.
        wait=Send(dragEnd);while(wait.MoveNext())yield return null;
        Require(Vector3.Distance(world.CameraFocus,dragFocus)>1&&controller.Selected.Count==0&&worker.TaskId==task,"Fast paused right drag failed");
        checks.Add("right drag works paused without selected workers, including movement first observed on release");
        controller.Select(new[]{worker});var uiPoint=new Vector2(100,world.View.pixelHeight-30);dragFocus=world.CameraFocus;
        wait=Send(uiPoint,2);while(wait.MoveNext())yield return null;wait=Send(dragOrigin,2);while(wait.MoveNext())yield return null;wait=Send(dragOrigin);while(wait.MoveNext())yield return null;
        Require(world.CameraFocus==dragFocus&&worker.TaskId==task,"UI-origin right drag leaked to map");
        wait=Send(dragOrigin,2);while(wait.MoveNext())yield return null;wait=Send(dragEnd,2);while(wait.MoveNext())yield return null;
        typeof(GatheringController).GetMethod("OnApplicationFocus",flags).Invoke(controller,new object[]{false});dragFocus=world.CameraFocus;
        wait=Send(dragOrigin);while(wait.MoveNext())yield return null;
        Require(world.CameraFocus==dragFocus&&worker.TaskId==task,"Focus-lost right release issued a command/pan");
        checks.Add("right gestures begun over UI are ignored, and losing focus cancels a pending drag/release");
        world.Paused=true;
        Require(load.Load(Path.GetFullPath(Application.dataPath+"/../../output/unity-polish/initial-v2.json")).Accepted,"Reset selection fixture");while(load.Loading||world.RoutesSettling)yield return null;
        world.Paused=true;world.GuideHidden=true;controller.Select(world.Workers);
        world.GuideHidden=false;
        var guidePoint=new Vector2(world.View.pixelWidth-90,world.View.pixelHeight-220);
        wait=Send(guidePoint,1);while(wait.MoveNext())yield return null;wait=Send(guidePoint);while(wait.MoveNext())yield return null;
        Require(controller.Selected.Count==12,"Guide panel click leaked to map");world.GuideHidden=true;checks.Add("guide panel consumes mouse clicks without changing selected workers");
        var previous=controller.Selected.Select(w=>w.Id).ToArray();
        wait=Send(p,1);while(wait.MoveNext())yield return null;wait=Send(new Vector2(100,world.View.pixelHeight-30),1);while(wait.MoveNext())yield return null;wait=Send(new Vector2(100,world.View.pixelHeight-30));while(wait.MoveNext())yield return null;
        Require(controller.Selected.Select(w=>w.Id).SequenceEqual(previous),"Drag to UI changed selection");checks.Add("release over interface cancels map selection/order");
        var center=new Vector2(world.View.pixelWidth*.55f,world.View.pixelHeight*.5f);var focus=world.CameraFocus;
        wait=Send(center,4);while(wait.MoveNext())yield return null;wait=Send(center+new Vector2(160,80),4);while(wait.MoveNext())yield return null;wait=Send(center+new Vector2(160,80));while(wait.MoveNext())yield return null;
        Require(Vector3.Distance(focus,world.CameraFocus)>1,"Middle pan");Require(controller.Selected.Count==12,"Pan changed selection");checks.Add("middle mouse pans map and preserves selection");
        typeof(GatheringController).GetField("mapDragMode",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(controller,true);
        focus=world.CameraFocus;wait=Send(center,1);while(wait.MoveNext())yield return null;wait=Send(center+new Vector2(-140,50),1);while(wait.MoveNext())yield return null;wait=Send(center+new Vector2(-140,50));while(wait.MoveNext())yield return null;
        Require(Vector3.Distance(focus,world.CameraFocus)>1&&controller.Selected.Count==12,"Left pan mode");
        wait=Send(center,2);while(wait.MoveNext())yield return null;wait=Send(center);while(wait.MoveNext())yield return null;
        Require(!(bool)typeof(GatheringController).GetField("mapDragMode",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(controller),"Right cancel drag mode");checks.Add("drag-map mode accepts left-button dragging and exits on right click without orders");
        float zoom=world.View.orthographicSize;wait=Send(new Vector2(100,world.View.pixelHeight-30),0,120);while(wait.MoveNext())yield return null;
        Require(world.View.orthographicSize==zoom,"Wheel over UI leaked");
        controller.BeginPlacement();wait=Send(center);while(wait.MoveNext())yield return null;wait=Send(center,0,120);while(wait.MoveNext())yield return null;
        Require(world.View.orthographicSize<zoom,"Placement wheel zoom");
        wait=Send(center,2);while(wait.MoveNext())yield return null;wait=Send(center);while(wait.MoveNext())yield return null;
        Require(world.Buildings.Count==0&&controller.Selected.Count==12,"Right cancel issued order/blueprint");checks.Add("wheel over UI is consumed; blueprint mode zooms; right cancel creates no blueprint or task");
        world.SetCameraFocus(new Vector3(7,0,-5));world.View.orthographicSize=10.5f;
        wait=Send(center);while(wait.MoveNext())yield return null;
        world.Paused=false;
        while(world.RoutesSettling)yield return null;
        Require(world.PlaceBuilding(new Vector3(7,0,-5),0,out var site).Accepted,"Site fixture");while(world.RoutesSettling)yield return null;
        controller.Select(new[]{worker});Require(worker.Work(world.Resources[0]).Accepted,"Gather fixture");world.Paused=true;
        p=Screen(site.transform.position+Vector3.up*.15f);var source=worker.Source;
        wait=Send(p,1);while(wait.MoveNext())yield return null;wait=Send(p);while(wait.MoveNext())yield return null;
        Require(controller.Feedback.StartsWith("已查看")&&worker.Source==source&&worker.BuildTarget==null,"Site inspection steals task or did not hit site");checks.Add("left click site preserves selected gatherer's current task");
        world.Paused=false;wait=Send(p,2);while(wait.MoveNext())yield return null;wait=Send(p);while(wait.MoveNext())yield return null;
        Require(worker.BuildTarget==site,"Right-click build");world.Paused=true;checks.Add("right click explicitly reassigns selected worker to construction");
    }
}

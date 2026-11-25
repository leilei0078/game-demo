using System;
using System.Collections;
using System.IO;
using System.Linq;
using GatheringDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class MapGridVerification
{
    private const string RunKey="Gathering.MapGridVerification";
    private static IEnumerator run;
    private static double deadline;
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}

    public static void Batch()
    {
        Require(Application.isBatchMode,"Use an isolated batch project");
        EditorSceneManager.OpenScene("Assets/Gathering/Scenes/GatheringDemo.unity");
        SessionState.SetBool(RunKey,true);EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod] private static void Resume()
    {
        if(!Application.isBatchMode||!SessionState.GetBool(RunKey,false))return;
        deadline=EditorApplication.timeSinceStartup+90;EditorApplication.update+=Tick;
    }

    private static void Tick()
    {
        try
        {
            Require(EditorApplication.timeSinceStartup<deadline,"Timed out waiting for Play Mode or navigation");
            if(!EditorApplication.isPlaying||Time.frameCount<3)return;
            run??=Verify();if(run.MoveNext())return;
            Debug.Log("MAP_GRID_PASS");Finish(0);
        }
        catch(Exception error){Debug.LogException(error);Finish(1);}
    }

    private static void Finish(int code){EditorApplication.update-=Tick;SessionState.EraseBool(RunKey);EditorApplication.Exit(code);}

    private static IEnumerator MouseAt(Mouse mouse,Vector2 point,ushort buttons=0,float scroll=0)
    {
        InputSystem.QueueStateEvent(mouse,new MouseState{position=point,buttons=buttons,scroll=new Vector2(0,scroll)});
        int frame=Time.frameCount+3;while(Time.frameCount<frame)yield return null;
    }

    private static void Capture(Camera camera,string name)
    {
        var previous=RenderTexture.active;camera.Render();RenderTexture.active=camera.targetTexture;
        var image=new Texture2D(camera.pixelWidth,camera.pixelHeight,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,image.width,image.height),0,0);image.Apply();
        string directory=Path.GetFullPath(Application.dataPath+"/../../output/unity-map-grid");Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory,name+".png"),image.EncodeToPNG());
        RenderTexture.active=previous;UnityEngine.Object.Destroy(image);
    }

    private static IEnumerator VerifyMarketRack(GatheringWorld world,Camera camera,Mouse mouse)
    {
        var depot=world.GetComponentInChildren<GatheringDepot>();var rack=depot.transform.Find("Timber Rack");
        if(!AssetDatabase.IsValidFolder("Assets/Gathering/Resources/MarketRack"))
        {
            Require(!rack&&depot.GetComponent<Renderer>().enabled,"Missing local pack should retain depot placeholder");yield break;
        }
        var timberZone=rack.Find("Timber Storage");var stoneZone=rack.Find("Stone Storage");
        int ShownLogs()=>timberZone.Cast<Transform>().Count(part=>part.gameObject.activeSelf);
        int ShownStones()=>stoneZone.Cast<Transform>().Count(part=>part.gameObject.activeSelf);
        Require(timberZone&&stoneZone&&ShownLogs()==0&&ShownStones()==0&&rack.GetComponentsInChildren<Collider>(true).Length==0,"Expected empty visual-only depot");
        var rackMeshes=rack.GetComponentsInChildren<MeshFilter>(true).Select(part=>AssetDatabase.GetAssetPath(part.sharedMesh)).Distinct().ToArray();
        foreach(string model in new[]{"WoodenBarrier_A","WoodenBarrier_B","WoodenDeck","WodenLog_A","WodenLog_B","WodenLog_C"})
            Require(rackMeshes.Contains("Assets/Gathering/Resources/MarketRack/"+model+".obj"),"Missing original market mesh: "+model);
        foreach(var material in rack.GetComponentsInChildren<MeshRenderer>(true).Where(part=>!part.transform.IsChildOf(stoneZone)).SelectMany(part=>part.sharedMaterials).Distinct())
            Require(material&&material.mainTexture&&material.mainTexture.name.StartsWith("KB3D_")&&material.GetTexture("_BumpMap"),"Missing market texture on "+material);
        var deckBounds=rack.Find("WoodenDeck").GetComponentInChildren<MeshRenderer>().bounds;
        Require(deckBounds.min.y>=-.01f&&deckBounds.max.y>=.12f,"Market deck buried: "+deckBounds);
        var oldFocus=world.CameraFocus;float oldZoom=camera.orthographicSize;
        world.SetCameraFocus(depot.transform.position);camera.orthographicSize=2.55f;
        var settleRack=MouseAt(mouse,Vector2.zero);while(settleRack.MoveNext())yield return null;
        Capture(camera,"00-timber-rack-empty");
        int wood=(int)ResourceKind.Wood;world.Inventory[wood]=108;
        settleRack=MouseAt(mouse,Vector2.zero);while(settleRack.MoveNext())yield return null;
        Require(ShownLogs()==18&&world.DepotBayCount==3&&depot.GetComponent<Collider>().Raycast(new Ray(depot.transform.position+Vector3.up*4,Vector3.down),out _,6),"Rack fill or depot click target changed");
        Capture(camera,"00-timber-rack-full");
        int ore=(int)ResourceKind.Ore;world.Inventory[ore]=6;
        settleRack=MouseAt(mouse,Vector2.zero);while(settleRack.MoveNext())yield return null;
        Require(ShownLogs()==18&&ShownStones()==1,"First stone delivery did not show independently of timber");
        Capture(camera,"00-depot-first-stone-delivery");
        float oldTimberWidth=timberZone.localScale.x,oldStoneWidth=stoneZone.localScale.x;
        world.SetDepotWoodPercent(40);world.Inventory[ore]=36;
        settleRack=MouseAt(mouse,Vector2.zero);while(settleRack.MoveNext())yield return null;
        Require(timberZone.localScale.x<oldTimberWidth&&stoneZone.localScale.x>oldStoneWidth&&ShownStones()==6&&world.Inventory[wood]==108&&world.Inventory[ore]==36,"Allocation did not resize both zones with the same inventory");
        Capture(camera,"00-depot-wood-40-stone-60");
        world.Inventory[wood]=world.Inventory[ore]=0;
        world.SetCameraFocus(oldFocus);camera.orthographicSize=oldZoom;
        settleRack=MouseAt(mouse,Vector2.zero);while(settleRack.MoveNext())yield return null;
        Require(ShownLogs()==0&&ShownStones()==0,"Depot stayed full after material removal");
    }

    private static IEnumerator Verify()
    {
        var world=UnityEngine.Object.FindFirstObjectByType<GatheringWorld>();
        while(!world.Ready||world.RoutesSettling)yield return null;
        var controller=world.GetComponent<GatheringController>();var camera=world.View;
        var target=new RenderTexture(1100,760,24);camera.targetTexture=target;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        var mouse=InputSystem.AddDevice<Mouse>();world.GuideHidden=true;
        var trees=world.Resources.Where(resource=>resource.Work==WorkKind.Chop).ToArray();
        Require(trees.Length==2,"Expected two wood sources");
        Require(world.Resources.Count==4&&world.InitialResources(ResourceKind.Wood)==252&&!world.Resources.Any(resource=>resource.Work==WorkKind.Pickup),"New maps must not spawn a wood crate");
        foreach(var tree in trees)
        {
            var visual=tree.transform.Find("Visual");var deadwood=visual.Find("Deadwood");
            Require(deadwood&&deadwood.GetComponentsInChildren<MeshRenderer>().Length==3,"Deadwood failed to load");
            Require(deadwood.GetComponentsInChildren<Collider>().Length==0&&!visual.GetComponent<Renderer>().enabled,"Deadwood must replace only the visible mesh");
            var collider=visual.GetComponent<Collider>();
            Require(collider.enabled&&collider.Raycast(new Ray(collider.bounds.center+Vector3.up*4,Vector3.down),out _,6),"Wood source lost its click target");
        }
        Capture(camera,"00-default-map");
        var verifyRack=VerifyMarketRack(world,camera,mouse);while(verifyRack.MoveNext())yield return null;
        var landscape=world.transform.Find("Heathland Details");
        Require(landscape&&landscape.GetComponentsInChildren<Collider>().Length==0,"Heath details should be visual only");
        var renderers=landscape.GetComponentsInChildren<MeshRenderer>();
        var ore=world.Resources.Single(resource=>resource.Work==WorkKind.Mine);
        var oreVisual=ore.transform.Find("Visual");
        var reference=renderers.First(renderer=>renderer.sharedMaterial.IsKeywordEnabled("HEATH_ROCK"));
        var oreParts=oreVisual.GetComponentsInChildren<MeshRenderer>().Where(renderer=>renderer.enabled).ToArray();
        Require(!oreVisual.GetComponent<Renderer>().enabled&&oreParts.Length==2&&oreParts.All(part=>part.sharedMaterial==reference.sharedMaterial&&part.GetComponent<MeshFilter>().sharedMesh==reference.GetComponent<MeshFilter>().sharedMesh),"Ore should match the scanned boulders");
        var oreTarget=oreVisual.GetComponent<Collider>();
        Require(oreVisual.GetComponentsInChildren<Collider>().Length==1&&oreTarget.Raycast(new Ray(ore.transform.position+Vector3.up*4,Vector3.down),out var oreHit,6)&&oreHit.collider.GetComponentInParent<GatheringResource>()==ore,"Lost ore click target");
        Require(renderers.Any(renderer=>renderer.enabled&&renderer.sharedMaterial.IsKeywordEnabled("HEATH_GRASS"))&&
                renderers.Any(renderer=>renderer.enabled&&renderer.sharedMaterial.IsKeywordEnabled("HEATH_ROCK")),"Missing grass or rock details");
        var ground=GameObject.Find("Walkable Ground").GetComponent<Renderer>();
        Require(ground.sharedMaterial.shader.name=="Gathering/Heathland"&&ground.sharedMaterial.GetTexture("_BaseNormal"),"Ground scan did not load");
        world.SetCameraFocus(new Vector3(3,0,-3));camera.orthographicSize=5.3f;
        var closeup=MouseAt(mouse,Vector2.zero);while(closeup.MoveNext())yield return null;
        Capture(camera,"00-heath-close");
        world.SetCameraFocus(new Vector3(11,0,-7));camera.orthographicSize=6;
        var settle=MouseAt(mouse,Vector2.zero);while(settle.MoveNext())yield return null;
        Vector2 Screen(Vector3 point)=>camera.WorldToScreenPoint(point);
        var focus=world.CameraFocus;var start=Screen(focus);var end=start+new Vector2(60,30);
        var edgeX=Screen(focus+Vector3.right)-start;var edgeZ=Screen(focus+Vector3.forward)-start;
        Require(edgeX.x*edgeZ.x<0&&Mathf.Abs(edgeX.x+edgeZ.x)<.05f&&Mathf.Abs(edgeX.y-edgeZ.y)<.05f&&Mathf.Abs(edgeX.y/edgeX.x)>.49f&&Mathf.Abs(edgeX.y/edgeX.x)<.51f,"Expected symmetric 2:1 diamond");
        var gesture=MouseAt(mouse,start,4);while(gesture.MoveNext())yield return null;
        gesture=MouseAt(mouse,end,4);while(gesture.MoveNext())yield return null;
        gesture=MouseAt(mouse,end);while(gesture.MoveNext())yield return null;
        Require(Vector2.Distance(Screen(focus),end)<1,"Dragged map did not follow pointer");
        float initialZoom=camera.orthographicSize;
        gesture=MouseAt(mouse,end,0,80);while(gesture.MoveNext())yield return null;
        Require(camera.orthographicSize<initialZoom&&Vector2.Distance(Screen(focus),end)<1,"Zoom lost pointer anchor");
        world.SetCameraFocus(new Vector3(-GatheringWorld.HalfWidth,0,-GatheringWorld.HalfDepth));camera.orthographicSize=20;
        settle=MouseAt(mouse,start);while(settle.MoveNext())yield return null;
        foreach(float cornerX in new[]{-GatheringWorld.HalfWidth,GatheringWorld.HalfWidth})
            foreach(float cornerZ in new[]{-GatheringWorld.HalfDepth,GatheringWorld.HalfDepth})
                foreach(float height in new[]{0f,3f})
                {
                    float depth=camera.WorldToViewportPoint(new Vector3(cornerX,height,cornerZ)).z;
                    Require(depth>camera.nearClipPlane&&depth<camera.farClipPlane,"Map clipped at farthest focus");
                }
        world.SetCameraFocus(focus);camera.orthographicSize=initialZoom;
        settle=MouseAt(mouse,start);while(settle.MoveNext())yield return null;
        var inputs=new[]{new Vector3(11,0,-7),new Vector3(11,0,6),new Vector3(-12,0,-7),new Vector3(-12,0,6)};
        for(int rotation=0;rotation<4;rotation++)
        {
            var center=PlacementGrid.Center(inputs[rotation],PlacementGrid.BuildingStep);
            Require(PlacementGrid.Center(center,PlacementGrid.BuildingStep)==center,"Building snap not idempotent");
            foreach(float offsetX in new[]{-.5f,.5f})foreach(float offsetZ in new[]{-.5f,.5f})
            {
                var tile=center+new Vector3(offsetX,0,offsetZ);
                Require(PlacementGrid.Center(tile)==tile&&PlacementGrid.Center(tile,PlacementGrid.BuildingStep)==center,"Non-nested grids");
            }
            var left=PlacementGrid.EntranceCell(center,rotation,0);var right=PlacementGrid.EntranceCell(center,rotation,1);
            var cells=GatheringWorld.Stroke(left,right);Require(cells.Count==2,"New yard should have adjacent entry cells");
            if(rotation%2==1)Require(world.PlaceInfrastructure(left,right,BuildingKind.Road,0,out _).Accepted,"Road before stockyard");
            ConstructionSite yard;
            if(rotation==0)
            {
                var screen=(Vector2)camera.WorldToScreenPoint(inputs[rotation]);controller.BeginPlacement();
                var wait=MouseAt(mouse,screen);while(wait.MoveNext())yield return null;Capture(camera,"01-building-grid");
                wait=MouseAt(mouse,screen,1);while(wait.MoveNext())yield return null;
                wait=MouseAt(mouse,screen);while(wait.MoveNext())yield return null;
                Require(world.Buildings.Count==1,"Mouse placement: "+controller.Feedback+" screen="+screen+" mouse="+mouse.position.ReadValue());
                yard=world.Buildings.Single();Require(yard.IsStockyard,"Mouse did not place stockyard");
            }
            else{var result=world.PlaceBuilding(inputs[rotation],rotation,out yard);Require(result.Accepted,result.Reason);}
            Require(yard.transform.position==center,"Preview/commit mismatch");
            while(world.RoutesSettling)yield return null;
            if(rotation==0)
            {
                controller.BeginInfrastructure(BuildingKind.Road);
                var first=(Vector2)camera.WorldToScreenPoint(left);var last=(Vector2)camera.WorldToScreenPoint(right);
                var wait=MouseAt(mouse,first);while(wait.MoveNext())yield return null;
                wait=MouseAt(mouse,first,1);while(wait.MoveNext())yield return null;
                wait=MouseAt(mouse,last,1);while(wait.MoveNext())yield return null;Capture(camera,"02-road-grid");
                wait=MouseAt(mouse,last);while(wait.MoveNext())yield return null;Capture(camera,"03-road-placed");
            }
            else if(rotation%2==0)Require(world.PlaceInfrastructure(left,right,BuildingKind.Road,0,out _).Accepted,"Road after stockyard");
            for(int index=0;index<2;index++)Require(world.Buildings.Any(site=>site.IsRoad&&site.transform.position==PlacementGrid.Center(yard.WorkPoint(index))),"Road not under loading bay");
            Require(!world.PlaceInfrastructure(center,center,BuildingKind.Road,0,out _).Accepted,"Road inside building");
            Require(!world.PlaceInfrastructure(left,right,BuildingKind.Road,0,out _).Accepted,"Duplicate road accepted");
        }
        InputSystem.RemoveDevice(mouse);
        world.SetDepotWoodPercent(40);
        var save=world.CaptureSave();Require(save.buildings.Length==12&&save.depotWoodPercent==40,"Wrong building count or depot allocation");
        int stock=trees[0].Stock,treeId=trees[0].Id;
        int oreStock=ore.Stock,oreId=ore.Id;
        ore.Harvest(oreStock);world.Inventory[(int)ResourceKind.Ore]+=oreStock;
        Require(!oreVisual.gameObject.activeInHierarchy,"Depleted ore stayed visible");
        trees[0].Harvest(stock);
        world.Inventory[(int)ResourceKind.Wood]+=stock;
        Require(!trees[0].transform.Find("Visual/Deadwood").gameObject.activeInHierarchy,"Depleted tree stayed visible");
        save.buildings[0].position+=new Vector3(.5f,0,-.5f);
        string path=Path.Combine(Application.temporaryCachePath,"map-grid-"+Guid.NewGuid().ToString("N")+".json");
        GatheringSaveStore.Write(path,save);var loader=world.GetComponent<GatheringSaveService>();world.SetDepotWoodPercent(80);
        Require(loader.Load(path).Accepted,"Legacy coordinate save rejected");while(loader.Loading)yield return null;
        Require(world.DepotWoodPercent==40,"Load did not restore warehouse allocation");
        foreach(var record in save.buildings)Require(world.Buildings.Single(site=>site.Id==record.id).transform.position==record.position,"Load resnapped a building");
        var restoredTree=world.Resources.Single(resource=>resource.Id==treeId);
        var restoredOre=world.Resources.Single(resource=>resource.Id==oreId);
        Require(restoredOre.Stock==oreStock&&restoredOre.transform.Find("Visual/Ore boulder").gameObject.activeInHierarchy,"Reload did not restore ore");
        Require(restoredTree.Stock==stock&&restoredTree.transform.Find("Visual/Deadwood").gameObject.activeInHierarchy,"Reload did not restore the depleted tree");
        File.Delete(path);
        var legacy=world.CaptureSave();legacy.paused=true;legacy.initial[(int)ResourceKind.Wood]+=72;
        var crate=new ResourceSave{id=legacy.resources.Max(resource=>resource.id)+1,kind=(int)ResourceKind.Wood,work=(int)WorkKind.Pickup,
            title="待运木箱",stock=60,initial=72,position=new Vector3(.7f,0,3.5f),radius=.65f};
        legacy.resources=legacy.resources.Append(crate).ToArray();legacy.inventory[(int)ResourceKind.Wood]=6;
        var carrier=legacy.workers.First(worker=>worker.id==1);carrier.kind=(int)ResourceKind.Wood;carrier.count=6;carrier.unitWeight=2;
        carrier.phase=(int)WorkerPhase.WaitingDepot;carrier.source=carrier.lastSource=crate.id;
        carrier.queue=new[]{new OrderSave{source=crate.id,taskId=++legacy.serial}};
        GatheringSaveStore.Write(path,legacy);
        Require(loader.Load(path).Accepted,"Old wood crate save rejected");while(loader.Loading)yield return null;
        var migrated=world.CaptureSave();var movedWorker=migrated.workers.First(worker=>worker.id==1);
        Require(migrated.resources.Length==4&&migrated.resources.Where(resource=>resource.work==(int)WorkKind.Chop).Sum(resource=>resource.stock)==312,"Legacy crate came back or uncollected wood was lost");
        Require(migrated.inventory[(int)ResourceKind.Wood]==6&&movedWorker.count==6&&movedWorker.source==movedWorker.lastSource&&movedWorker.source!=crate.id&&movedWorker.queue.Single().source==movedWorker.source,"Legacy crate migration lost cargo, stock or task references");
        Require(world.AccountedResources(ResourceKind.Wood)==324,"Legacy wood no longer conserved");
        File.Delete(path);world.Paused=false;world.Workers.First(worker=>worker.Id==1).Stop();
        var finishedYard=world.Buildings.First(site=>site.IsStockyard&&site.Rotation==0);
        world.SetCameraFocus(finishedYard.transform.position);camera.orthographicSize=5;
        foreach(var site in world.Buildings.Where(site=>site==finishedYard||site.IsRoad&&Vector3.Distance(site.transform.position,finishedYard.transform.position)<3))
        {
            site.Receive(ResourceKind.Wood,site.MaterialCost(ResourceKind.Wood));
            site.Receive(ResourceKind.Ore,site.MaterialCost(ResourceKind.Ore));
            site.Work(site.LabourRequired);
            Require(site.Complete,"Test fixture failed to finish an object");
        }
        yield return null;
        Capture(camera,"04-completed-road");
        camera.targetTexture=null;UnityEngine.Object.Destroy(target);
    }
}

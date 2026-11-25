using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace GatheringDemo
{
    public sealed partial class GatheringWorld
    {
        private GatheringSettings restoredSettings;
        public GatheringSaveData CaptureSave()
        {
            GatheringSaveData.Require(Ready,"正在恢复世界，请稍后保存");
            var data=new GatheringSaveData{version=4,guideStep=GuideStep,guideHidden=GuideHidden,map="gathering-flat-v2",cameraFocus=CameraFocus,recordedUtc=System.DateTime.UtcNow.ToString("O"),settingsJson=JsonUtility.ToJson(settings),elapsed=Elapsed,zoom=View.orthographicSize,paused=Paused,roadblock=Roadblock,serial=serial,completedTrips=CompletedTrips,inventory=(int[])Inventory.Clone(),initial=(int[])initialResources.Clone(),
                depotWoodPercent=DepotWoodPercent,resources=Resources.Select(r=>new ResourceSave{id=r.Id,kind=(int)r.Kind,work=(int)r.Work,stock=r.Stock,initial=r.Initial,title=r.DisplayName,position=r.transform.position,radius=r.Radius,refund=r.IsRefund}).ToArray(),
                buildings=Buildings.Select(b=>b.CaptureSave()).ToArray(),workers=Workers.Select(w=>w.CaptureSave()).ToArray()};
            data.Validate();return data;
        }
        internal IEnumerator RestoreSave(GatheringSaveData data)
        {
            data.ReplaceLegacyWoodCrate();
            Ready=false;Paused=false;nextCrewCheck=0;
            GetComponent<GatheringController>().ResetAfterLoad();
            foreach(var worker in Workers)worker.Stop();
            foreach(var resource in Resources){resource.gameObject.SetActive(false);Destroy(resource.gameObject);}Resources.Clear();
            foreach(var building in Buildings){building.gameObject.SetActive(false);Destroy(building.gameObject);}Buildings.Clear();
            bays.RemoveRange(3,bays.Count-3);
            foreach(var r in data.resources)
            {var resource=CreateResource(r.title,(ResourceKind)r.kind,(WorkKind)r.work,r.initial,r.position,r.radius);resource.SetStableId(r.id);resource.RestoreStock(r.stock,r.initial);}
            foreach(var b in data.buildings)
            {
                var root=new GameObject("Stockyard "+b.id);root.transform.SetParent(transform);root.transform.position=b.position;
                var building=root.AddComponent<ConstructionSite>();building.Initialize(this,b.id,b.rotation,data.version>=4?(BuildingKind)b.kind:BuildingKind.Stockyard);Buildings.Add(building);building.RestoreSave(b,data.version);
                if(b.complete&&building.IsStockyard)OpenDepot(building);
            }
            var copy=ScriptableObject.CreateInstance<GatheringSettings>();JsonUtility.FromJsonOverwrite(data.settingsJson,copy);
            if(restoredSettings)Destroy(restoredSettings);settings=restoredSettings=copy;
            System.Array.Copy(data.inventory,Inventory,3);System.Array.Copy(data.initial,initialResources,3);
            SetDepotWoodPercent(data.depotWoodPercent==0?GatheringDepot.DefaultWoodPercent:data.depotWoodPercent);
            GuideStep=data.version>=3?data.guideStep:0;GuideHidden=data.version>=3&&data.guideHidden;
            Elapsed=data.elapsed;CompletedTrips=data.completedTrips;roadblock.SetActive(data.roadblock);View.orthographicSize=data.zoom;SetCameraFocus(data.version==1?Vector3.zero:data.cameraFocus);
            RefreshHeathClearances();
            // Let obstacle carving and deferred destruction finish before rebinding agents.
            yield return null;yield return null;yield return RestoreRoadNavigation();yield return new WaitForSecondsRealtime(.25f);
            serial=System.Math.Max(serial,data.serial);
            foreach(var w in data.workers)Workers.First(worker=>worker.Id==w.id).RestoreSave(w);
            RoadRevision++;routesReadyAt=Time.unscaledTime+.5f;Paused=data.paused;
            Metrics.Reset(Workers);Ready=true;
        }
    }
    public sealed partial class ConstructionSite
    {
        internal BuildingSave CaptureSave()=>new BuildingSave{kind=(int)Kind,id=Id,rotation=Rotation,priority=Priority,priorityPosition=PriorityPosition,autoCrewGroup=AutoCrewGroup,position=transform.position,delivered=(int[])Delivered.Clone(),labour=Labour,paused=Paused,complete=Complete,cancelled=Cancelled};
        internal void RestoreSave(BuildingSave data,int version)
        {
            Priority=version>=3?data.priority:1;
            PriorityPosition=data.priorityPosition;
            AutoCrewGroup=version>=4?data.autoCrewGroup:0;
            System.Array.Copy(data.delivered,Delivered,3);Labour=data.labour;Paused=data.paused;Complete=data.complete;Cancelled=data.cancelled;
            if(Complete){ShowComplete();if(IsRoad)EnableRoadArea();}
            if(Cancelled){if(obstacle)obstacle.enabled=false;footprint.enabled=false;body.SetActive(false);}
        }
    }
    public sealed partial class GatheringWorker
    {
        internal WorkerSave CaptureSave()=>new WorkerSave{id=Id,taskId=TaskId,phase=(int)Phase,blockedPhase=(int)blockedPhase,source=Source?Source.Id:0,lastSource=LastSource?LastSource.Id:0,building=BuildTarget?BuildTarget.Id:0,cargoSite=CargoSite?CargoSite.Id:0,kind=(int)CargoKind,count=CargoCount,unitWeight=unitWeight,position=transform.position,destination=destination,returningMaterials=ReturningMaterials,finishAfterTrip=FinishAfterTrip,continueSameKind=ContinueSameKind,deliverBeforeWork=deliverBeforeWork,
            queue=orders.Select(o=>new OrderSave{taskId=o.taskId,source=o.source?o.source.Id:0,building=o.building?o.building.Id:0,point=o.point,transport=o.transport,once=o.once,autoBuild=o.autoBuild}).ToArray()};
        internal void RestoreSave(WorkerSave data)
        {
            GatheringResource Resource(int id)=>world.Resources.FirstOrDefault(r=>r.Id==id);
            ConstructionSite Building(int id)=>world.Buildings.FirstOrDefault(b=>b.Id==id);
            ResetTask();orders.Clear();
            agent.enabled=false;transform.position=data.position;agent.enabled=true;
            if(NavMesh.SamplePosition(data.position,out var hit,1.5f,NavMesh.AllAreas))agent.Warp(hit.position);
            else throw new System.InvalidOperationException("工人位置无法恢复到可行走区域");
            Source=Resource(data.source);LastSource=Resource(data.lastSource);BuildTarget=Building(data.building);CargoSite=Building(data.cargoSite);
            CargoKind=(ResourceKind)data.kind;CargoCount=data.count;unitWeight=data.unitWeight;ReturningMaterials=data.returningMaterials;cargoVisual.SetActive(CargoCount>0);
            FinishAfterTrip=data.finishAfterTrip;ContinueSameKind=data.continueSameKind;deliverBeforeWork=data.deliverBeforeWork;destination=data.destination;
            foreach(var q in data.queue)orders.Add(new Order{taskId=q.taskId,source=Resource(q.source),building=Building(q.building),point=q.point,transport=q.transport,once=q.once,autoBuild=q.autoBuild});
            var saved=(WorkerPhase)data.phase;TaskId=data.taskId;
            // Material transfers are already reflected in the checkpoint. Restart only the unfinished action.
            if(saved==WorkerPhase.Idle){Phase=WorkerPhase.Idle;agent.ResetPath();}
            else if(BuildTarget){if(CargoCount>0&&CargoSite!=BuildTarget)BeginDelivery();else BeginBuild();}
            else if(CargoCount>0&&(saved==WorkerPhase.WaitingDepot||saved==WorkerPhase.ToDepot||saved==WorkerPhase.Unloading||saved==WorkerPhase.Blocked&&((WorkerPhase)data.blockedPhase==WorkerPhase.WaitingDepot||(WorkerPhase)data.blockedPhase==WorkerPhase.ToDepot)))BeginDelivery();
            else if(saved==WorkerPhase.Moving||saved==WorkerPhase.Blocked&&Source==null)Navigate(destination,WorkerPhase.Moving);
            else if(Source){if(CargoCount>0)BeginDelivery();else BeginWork();}
            else if(CargoCount>0)BeginDelivery();
            else {Phase=WorkerPhase.Idle;if(orders.Count>0)NextOrHome();}
        }
    }
}

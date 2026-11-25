using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GatheringDemo
{
    [Serializable] public sealed class GatheringSaveData
    {
        public int version;
        public string map,recordedUtc,settingsJson;
        public float elapsed,zoom;
        public Vector3 cameraFocus;
        public bool paused,roadblock;
        public long serial;
        public int completedTrips,guideStep,depotWoodPercent;
        public bool guideHidden;
        public int[] inventory,initial;
        public ResourceSave[] resources;
        public BuildingSave[] buildings;
        public WorkerSave[] workers;

        public static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        public static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        public static bool Position(Vector3 p)=>Finite(p.x)&&Finite(p.y)&&Finite(p.z)&&Mathf.Abs(p.x)<=GatheringWorld.HalfWidth&&Mathf.Abs(p.z)<=GatheringWorld.HalfDepth&&Mathf.Abs(p.y)<2;
        private static void Counts(int[] counts)=>Require(counts!=null&&counts.Length==3&&counts.All(n=>n>=0&&n<=1000000),"材料数量无效");
        public void Validate()
        {
            Require(version==1&&map=="gathering-flat-v1"||(version>=2&&version<=4)&&map=="gathering-flat-v2","存档版本或地图不兼容");
            Require(Finite(elapsed)&&elapsed>=0&&Finite(zoom)&&zoom>=5&&zoom<=20&&serial>=0&&completedTrips>=0,"世界参数无效");
            Require(version==1||Position(cameraFocus)&&cameraFocus.y==0,"镜头位置无效");
            Require(version<3||guideStep>=0&&guideStep<=7,"引导进度无效");
            Require(depotWoodPercent==0||depotWoodPercent>=GatheringDepot.MinWoodPercent&&depotWoodPercent<=GatheringDepot.MaxWoodPercent,"仓库存放占比无效");
            Counts(inventory);Counts(initial);Require((initial[0]==252||initial[0]==324)&&initial[1]==144&&initial[2]==90,"地图资源总量不匹配");
            Require(resources!=null&&resources.Length>=4&&resources.Length<=1000&&buildings!=null&&buildings.Length<=500&&workers!=null&&workers.Length==12,"实体数量无效");
            Require(resources.All(r=>r!=null)&&buildings.All(b=>b!=null)&&workers.All(w=>w!=null),"实体记录缺失");
            var resourceIds=new HashSet<int>(resources.Select(r=>r.id));var buildingIds=new HashSet<int>(buildings.Select(b=>b.id));
            Require(resourceIds.Count==resources.Length&&resourceIds.All(i=>i>0)&&buildingIds.Count==buildings.Length&&buildingIds.All(i=>i>0),"实体ID重复或无效");
            Require(workers.Select(w=>w.id).OrderBy(i=>i).SequenceEqual(Enumerable.Range(1,12)),"工人ID无效");
            bool ResourceRef(int id)=>id==0||resourceIds.Contains(id);
            bool BuildingRef(int id)=>id==0||buildingIds.Contains(id);
            foreach(var r in resources)
            {
                Require(!string.IsNullOrEmpty(r.title)&&r.title.Length<200&&Position(r.position)&&r.kind>=0&&r.kind<3&&r.work>=0&&r.work<4&&r.stock>=0&&r.stock<=r.initial&&r.initial<=1000000&&Finite(r.radius)&&r.radius>=.1f&&r.radius<=2,"资源记录无效");
                Require(r.refund==r.title.StartsWith("退料箱")&&(!r.refund||r.work==(int)WorkKind.Pickup),"退料标记无效");
            }
            var positions=new HashSet<int>();
            foreach(var b in buildings)
            {
                Require(version<3||b.priority>=0&&b.priority<=2,"工地优先级无效");
                Require(b.priorityPosition>=0&&b.priorityPosition<=500,"工地排位无效");
                Require(b.cancelled||b.complete||positions.Add(b.priorityPosition==0?b.id:b.priorityPosition),"未完工工地排位重复");
                Require(b.autoCrewGroup==0||version>=4&&b.autoCrewGroup<=b.id&&buildingIds.Contains(b.autoCrewGroup),"待派工批次无效");
                Require(version>=4?Enum.IsDefined(typeof(BuildingKind),b.kind):b.kind==0,"设施类型无效");
                var kind=version>=4?(BuildingKind)b.kind:BuildingKind.Stockyard;
                float labour=ConstructionSite.LabourFor(kind);bool supplied=Enumerable.Range(0,3).All(k=>b.delivered!=null&&b.delivered.Length==3&&b.delivered[k]==ConstructionSite.CostFor(kind,(ResourceKind)k));
                Counts(b.delivered);Require(Position(b.position)&&b.rotation>=0&&b.rotation<4&&Finite(b.labour)&&b.labour>=0&&b.labour<=labour,"建筑记录无效");
                for(int k=0;k<3;k++)Require(b.delivered[k]<=ConstructionSite.CostFor(kind,(ResourceKind)k),"建筑收到超额材料");
                Require(!b.complete||!b.cancelled&&b.labour==labour&&supplied,"完工状态无效");
                Require(b.cancelled?b.delivered.Sum()==0:b.labour==0||supplied,"施工状态与材料不符");

            }
            var taskIds=new HashSet<long>();
            foreach(var w in workers)
            {
                Require(Position(w.position)&&Position(w.destination)&&Enum.IsDefined(typeof(WorkerPhase),w.phase)&&Enum.IsDefined(typeof(WorkerPhase),w.blockedPhase)&&w.kind>=0&&w.kind<3&&w.count>=0&&w.count<=24&&Finite(w.unitWeight)&&w.unitWeight>=0&&(w.count==0||w.unitWeight>0),"工人携货或位置无效");
                Require(ResourceRef(w.source)&&ResourceRef(w.lastSource)&&BuildingRef(w.building)&&BuildingRef(w.cargoSite)&&w.queue!=null&&w.queue.Length<=16,"任务目标缺失");
                Require(w.taskId>=0&&w.taskId<=serial&&(w.taskId==0||taskIds.Add(w.taskId)),"当前任务ID无效");
                Require(w.building==0||w.source==0,"工人存在冲突任务");
                Require(w.cargoSite==0||w.cargoSite==w.building&&w.count>0&&w.returningMaterials&&!buildings.First(b=>b.id==w.cargoSite).cancelled,"在途建材分配无效");
                var phase=(WorkerPhase)w.phase;
                Require(phase<WorkerPhase.WaitingBuild||w.building>0,"建造阶段缺少工地");
                Require(phase!=WorkerPhase.Working&&phase!=WorkerPhase.ToWork&&phase!=WorkerPhase.WaitingWork||w.source>0,"采集阶段缺少资源");
                foreach(var q in w.queue)
                    Require(q!=null&&ResourceRef(q.source)&&BuildingRef(q.building)&&Position(q.point)&&q.taskId>0&&q.taskId<=serial&&taskIds.Add(q.taskId)&&!(q.source>0&&q.building>0)&&(!q.autoBuild||q.building>0&&!q.transport),"后续任务无效");
            }
            for(int k=0;k<3;k++)
            {
                long total=resources.Where(r=>r.kind==k).Sum(r=>(long)r.stock)+workers.Where(w=>w.kind==k).Sum(w=>(long)w.count)+inventory[k]+buildings.Sum(b=>(long)b.delivered[k]);
                Require(total==initial[k],"存档材料不守恒："+(ResourceKind)k);
                foreach(var b in buildings)Require(b.delivered[k]+workers.Where(w=>w.cargoSite==b.id&&w.kind==k).Sum(w=>w.count)<=ConstructionSite.CostFor(version>=4?(BuildingKind)b.kind:BuildingKind.Stockyard,(ResourceKind)k),"工地在途建材超额");
            }
            Require(!string.IsNullOrEmpty(settingsJson)&&settingsJson.Length<5000,"物流规则缺失");
            var settings=ScriptableObject.CreateInstance<GatheringSettings>();
            try{JsonUtility.FromJsonOverwrite(settingsJson,settings);Require(settings.ValidateSettings()=="","物流规则无效");}
            finally{UnityEngine.Object.Destroy(settings);}
        }

        internal void ReplaceLegacyWoodCrate()
        {
            var crate=resources.FirstOrDefault(resource=>!resource.refund&&resource.kind==(int)ResourceKind.Wood&&resource.work==(int)WorkKind.Pickup&&resource.title=="待运木箱");
            if(crate==null)return;
            var tree=resources.Where(resource=>resource.work==(int)WorkKind.Chop&&resource.kind==(int)ResourceKind.Wood)
                .OrderBy(resource=>(resource.position-crate.position).sqrMagnitude).FirstOrDefault();
            Require(tree!=null,"旧木箱缺少可迁移的树木点");
            tree.initial+=crate.initial;tree.stock+=crate.stock;
            foreach(var worker in workers)
            {
                if(worker.source==crate.id)worker.source=tree.id;
                if(worker.lastSource==crate.id)worker.lastSource=tree.id;
                foreach(var order in worker.queue)if(order.source==crate.id)order.source=tree.id;
            }
            resources=resources.Where(resource=>resource!=crate).ToArray();
            Validate();
        }
    }
    [Serializable] public sealed class ResourceSave
    {public int id,kind,work,stock,initial;public string title;public Vector3 position;public float radius;public bool refund;}
    [Serializable] public sealed class BuildingSave
    {public int id,rotation,priority,priorityPosition,kind,autoCrewGroup;public Vector3 position;public int[] delivered;public float labour;public bool paused,complete,cancelled;}
    [Serializable] public sealed class OrderSave
    {public int source,building;public Vector3 point;public bool transport,once,autoBuild;public long taskId;}
    [Serializable] public sealed class WorkerSave
    {
        public int id,phase,blockedPhase,source,lastSource,building,cargoSite,kind,count;
        public long taskId;
        public float unitWeight;
        public Vector3 position,destination;
        public bool returningMaterials,finishAfterTrip,continueSameKind,deliverBeforeWork;
        public OrderSave[] queue;
    }
}

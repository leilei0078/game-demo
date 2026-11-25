using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

namespace GatheringDemo
{
    public enum BuildingKind { Stockyard=0,Road=1,Wall=2 }

    internal static class InfrastructurePlacement
    {
        public static string Reason(GatheringWorld world,Vector3 point,int rotation,BuildingKind kind)
        {
            var size=ConstructionSite.SizeFor(kind,rotation);float hx=size.x*.5f,hz=size.z*.5f;
            bool Over(Vector3 p,float x,float z)=>Mathf.Abs(p.x-point.x)<hx+x-.001f&&Mathf.Abs(p.z-point.z)<hz+z-.001f;
            if(Mathf.Abs(point.x)+hx>GatheringWorld.HalfWidth-.5f||Mathf.Abs(point.z)+hz>GatheringWorld.HalfDepth-.5f)return "超出可建场地";
            if(Over(new Vector3(-6,0,-2),1.5f,1.05f))return "与初始仓库重叠";
            if(world.Roadblock&&Mathf.Abs(point.x+2)<hx+.3f)return "与练习路障重叠";
            foreach(var r in world.Resources.Where(r=>r.Stock>0))
                if(Over(r.transform.position,r.Radius,r.Radius))return "与资源重叠";
            foreach(var b in world.Buildings.Where(b=>!b.Cancelled))
            {
                var other=b.FootprintSize;
                if(Over(b.transform.position,other.x*.5f,other.z*.5f))return b.Kind==kind?"已有同类设施，不重复建造":"与已有设施重叠";
                if(kind==BuildingKind.Wall&&b.IsStockyard)for(int i=0;i<2;i++)if(Over(b.WorkPoint(i),.5f,.5f))return "需要保留已有堆场入口";
            }
            if(kind==BuildingKind.Wall)
            {
                if(Mathf.Abs(point.x+2)<hx+.55f)return "请保留练习主通道";
                foreach(var w in world.Workers)if(Over(w.transform.position,.5f,.5f)||Over(world.Home(w.Id),.5f,.5f))return "围墙范围有人或待命区";
                foreach(var r in world.Resources.Where(r=>r.Stock>0))for(int i=0;i<4;i++)if(Over(r.Slot(i),.5f,.5f))return "需要保留采集工作位";
            }
            if(!NavMesh.SamplePosition(point,out var hit,.15f,NavMesh.AllAreas))return "没有可铺设地面";
            bool accessible=false;
            for(int i=0;i<ConstructionSite.BuildPointCount;i++)
            {
                var p=ConstructionSite.BuildPointFor(kind,point,rotation,i);
                if(!NavMesh.SamplePosition(p,out var entry,.15f,NavMesh.AllAreas))continue;
                var path=new NavMeshPath();
                if(NavMesh.CalculatePath(new Vector3(-6,0,-.15f),entry.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete){accessible=true;break;}
            }
            return accessible?"":"工地周边没有与仓库连通的施工位置";
        }
    }

    public sealed partial class GatheringWorld
    {
        private float nextCrewCheck;
        public List<ConstructionSite> ConstructionQueue()=>Buildings.Where(site=>!site.Complete&&!site.Cancelled).OrderBy(site=>site.PriorityKey).ThenBy(site=>site.Id).ToList();
        public OrderResult MoveConstruction(ConstructionSite site,int destination)
        {
            var queue=ConstructionQueue();int previous=queue.IndexOf(site);
            if(!Ready||previous<0||destination<0||destination>=queue.Count||destination==previous)return OrderResult.No("工地位置未变");
            site.MatchPriority(queue[destination]);queue.RemoveAt(previous);queue.Insert(destination,site);
            for(int index=0;index<queue.Count;index++)queue[index].PriorityPosition=index+1;
            return PriorityChanged(site);
        }
        internal OrderResult PriorityChanged(ConstructionSite site)
        {
            if(site.AutoCrewGroup==0&&site.PlannedBuilders==0)site.AutoCrewGroup=site.Id;
            nextCrewCheck=0;foreach(var worker in Workers)worker.SortAutoBuildOrders();
            Notice=$"{site.Title} · {site.PriorityName} · 建造顺序第 {ConstructionQueue().IndexOf(site)+1} 位";return OrderResult.Yes(Notice);
        }
        internal string RequestPlacementCrew(IReadOnlyList<ConstructionSite> sites)
        {
            int group=sites[0].Id;
            foreach(var site in sites)site.AutoCrewGroup=group;
            nextCrewCheck=0;
            Notice=$"已放置{sites.Count}处{sites[0].DisplayName}蓝图，空闲工人会自动接单";
            return Notice;
        }
        private void DispatchPlacementCrews()
        {
            if(Paused||RoutesSettling||Elapsed<nextCrewCheck)return;
            nextCrewCheck=Elapsed+.5f;
            var pending=Buildings.Where(site=>site.AutoCrewGroup>0&&!site.Paused&&!site.Complete&&!site.Cancelled).OrderBy(site=>site.PriorityKey);
            foreach(var group in pending.GroupBy(site=>site.AutoCrewGroup))
            {
                var batch=new List<ConstructionSite>();
                foreach(var site in group){if(site.PlannedBuilders==0)batch.Add(site);else site.AutoCrewGroup=0;}
                if(batch.Count>0){var result=AssignIdleCrew(batch);if(result.Accepted)Notice=result.Reason;}
            }
        }
        internal ConstructionSite TakeEarlierPending(GatheringWorker worker,ConstructionSite next)
        {
            var earlier=Buildings.Where(site=>site.AutoCrewGroup>0&&!site.Paused&&!site.Complete&&!site.Cancelled&&site.PriorityKey<next.PriorityKey&&site.PlannedBuilders==0)
                .OrderBy(site=>site.PriorityKey).FirstOrDefault(site=>site.CanBuildFrom(worker));
            if(earlier)earlier.AutoCrewGroup=0;
            return earlier;
        }
        internal OrderResult AssignIdleCrew(IReadOnlyList<ConstructionSite> sites)
        {
            if(Paused||RoutesSettling||sites.Count==0)return OrderResult.No("当前不能派工");
            int crew=0,covered=0;
            sites=sites.OrderBy(site=>site.PriorityKey).ToArray();var staffing=sites.Select(site=>site.PlannedBuilders).ToArray();
            foreach(var worker in Workers.Where(worker=>worker.Phase==WorkerPhase.Idle&&worker.CargoCount==0&&worker.QueueCount==0).OrderBy(worker=>(worker.transform.position-sites[0].transform.position).sqrMagnitude))
            {
                bool started=false;
                for(int index=0;index<sites.Count;index++)
                {
                    var site=sites[index];
                    if(staffing[index]>=2||site.Paused||site.Cancelled||site.Complete)continue;
                    if(!worker.Build(site,started,true).Accepted)continue;
                    started=true;staffing[index]++;site.AutoCrewGroup=0;
                }
                if(started&&++crew==2)break;
            }
            foreach(int count in staffing)if(count>0)covered++;
            if(crew==0)return OrderResult.No(staffing.All(count=>count>=2)?"已有工人承接，无需重复派工":"没有可达的空闲工人");
            return OrderResult.Yes($"已安排{crew}名空闲工人，已有人承接 {covered}/{sites.Count} 处工地");
        }
        public const float BasicRoadMultiplier=1.1f;
        public const int RoadArea=3;
        private NavMeshSurface navigationSurface;
        private bool roadDirty;
        private Coroutine roadNavigation;
        public bool NavigationUpdating=>roadDirty||roadNavigation!=null;
        public float SurfaceSpeed(Vector3 point)=>Buildings.Any(b=>b.IsRoad&&b.Complete&&!b.Cancelled&&Mathf.Abs(point.x-b.transform.position.x)<=.5f&&Mathf.Abs(point.z-b.transform.position.z)<=.5f)?BasicRoadMultiplier:1;
        internal void RoadFinished(ConstructionSite site){site.EnableRoadArea();roadDirty=true;}
        private void UpdateRoadNavigation()
        {
            if(!roadDirty||roadNavigation!=null||!Ready)return;
            roadDirty=false;roadNavigation=StartCoroutine(RebuildRoadNavigation());
        }
        private IEnumerator RebuildRoadNavigation()
        {
            yield return navigationSurface.UpdateNavMesh(navigationSurface.navMeshData);
            roadNavigation=null;RoutesChanged();
        }
        internal IEnumerator RestoreRoadNavigation()
        {
            while(roadNavigation!=null)yield return null;
            roadDirty=false;
            yield return navigationSurface.UpdateNavMesh(navigationSurface.navMeshData);
        }
        public static List<Vector3> Stroke(Vector3 start,Vector3 end)
        {
            start=PlacementGrid.Center(start);end=PlacementGrid.Center(end);
            bool horizontal=Mathf.Abs(end.x-start.x)>=Mathf.Abs(end.z-start.z);int count=Mathf.RoundToInt(horizontal?end.x-start.x:end.z-start.z);
            var cells=new List<Vector3>();for(int i=0;i<=Mathf.Abs(count)&&i<80;i++)cells.Add(start+(horizontal?Vector3.right:Vector3.forward)*(i*Mathf.Sign(count)));
            return cells;
        }
        public string StrokeReason(IReadOnlyList<Vector3> cells,int rotation,BuildingKind kind,out List<Vector3> fresh)
        {
            fresh=new List<Vector3>();
            if(kind!=BuildingKind.Road&&kind!=BuildingKind.Wall)return "设施类型无效";
            if(cells.Count>17)return "每次最多铺17段，请分段规划";
            foreach(var cell in cells.Distinct())
            {
                if(Buildings.Any(b=>!b.Cancelled&&b.Kind==kind&&Vector3.Distance(b.transform.position,cell)<.1f))continue;
                string reason=InfrastructurePlacement.Reason(this,cell,rotation,kind);if(reason!="")return reason;
                fresh.Add(cell);
            }
            if(fresh.Count==0)return "整段已有相同设施，不重复计费";
            return kind==BuildingKind.Wall?PlacementConnectivity.Evaluate(this,fresh[0],rotation,kind,fresh):"";
        }
        public OrderResult PlaceInfrastructure(Vector3 start,Vector3 end,BuildingKind kind,int rotation,out List<ConstructionSite> sites)
        {
            sites=new List<ConstructionSite>();if(Paused)return OrderResult.No("已暂停");if(RoutesSettling)return OrderResult.No("正在更新通行，请稍候");
            var cells=Stroke(start,end);if(cells.Count>1)rotation=(Mathf.Abs(cells[1].x-cells[0].x)>.5f?0:1)+(rotation>=2?2:0);
            string reason=StrokeReason(cells,rotation,kind,out var fresh);if(reason!="")return OrderResult.No(reason);
            if(Buildings.Count+fresh.Count>500)return OrderResult.No("设施数量达到原型上限");
            int id=Buildings.Count==0?1:Buildings.Max(b=>b.Id)+1;
            foreach(var cell in fresh)
            {
                var root=new GameObject(kind+" "+id);root.transform.SetParent(transform);root.transform.position=cell;
                var site=root.AddComponent<ConstructionSite>();site.Initialize(this,id++,rotation,kind);Buildings.Add(site);sites.Add(site);
            }
            RefreshHeathClearances();if(kind==BuildingKind.Wall)RoutesChanged();
            Notice=$"已放置{sites.Count}段{(kind==BuildingKind.Road?"基础路面":"围墙")}；在清单派工，可一次安排整段";return OrderResult.Yes(Notice);
        }
    }

    public sealed partial class ConstructionSite
    {
        private GameObject roadArea;
        internal void EnableRoadArea()
        {
            if(!IsRoad||roadArea)return;
            roadArea=new GameObject("Completed road area");roadArea.transform.SetParent(transform);roadArea.transform.position=transform.position;roadArea.layer=8;
            var volume=roadArea.AddComponent<NavMeshModifierVolume>();volume.center=Vector3.zero;volume.size=new Vector3(1,1,1);volume.area=GatheringWorld.RoadArea;
        }
    }
}

using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace GatheringDemo
{
    public sealed partial class ConstructionSite : MonoBehaviour
    {
        public const float RequiredLabour=24;
        public BuildingKind Kind {get;private set;}
        public bool IsRoad=>Kind==BuildingKind.Road;
        public bool IsStockyard=>Kind==BuildingKind.Stockyard;
        public bool BlocksMovement=>!IsRoad&&!Cancelled;
        public string DisplayName=>IsStockyard?"堆场":IsRoad?"基础路面":"围墙";
        public string Title=>$"{DisplayName} {Id}";
        public float LabourRequired=>LabourFor(Kind);
        public int MaterialCost(ResourceKind kind)=>CostFor(Kind,kind);
        public Vector3 FootprintSize=>SizeFor(Kind,Rotation);
        public static float LabourFor(BuildingKind kind)=>kind==BuildingKind.Stockyard?24:kind==BuildingKind.Road?3:6;
        public static int CostFor(BuildingKind type,ResourceKind kind)=>type==BuildingKind.Stockyard?Cost(kind):kind==ResourceKind.Wood?(type==BuildingKind.Road?1:2):0;
        public static Vector3 SizeFor(BuildingKind kind,int rotation)=>kind==BuildingKind.Stockyard?Size(rotation):kind==BuildingKind.Road?new Vector3(1,.06f,1):new Vector3(1,1.3f,1);
        public static Vector3 PointFor(BuildingKind kind,Vector3 center,int rotation,int index)=>kind==BuildingKind.Stockyard?Point(center,rotation,index):center+Quaternion.Euler(0,rotation*90,0)*new Vector3(index==0?-.3f:.3f,0,kind==BuildingKind.Road?.8f:1.05f);
        // Finished stockyard entrances stay fixed; construction can use any accessible side.
        public const int BuildPointCount=8;
        public static Vector3 BuildPointFor(BuildingKind kind,Vector3 center,int rotation,int index,float extra=0)
        {
            float x=kind==BuildingKind.Stockyard?1.5f:.5f,z=kind==BuildingKind.Stockyard?1:.5f;
            float tangent=(index%2==0?-1:1)*(kind==BuildingKind.Stockyard?.7f:.3f);
            float clearance=(kind==BuildingKind.Stockyard?.9f:kind==BuildingKind.Road?.3f:.55f)+extra;
            var local=index<2?new Vector3(tangent,0,z+clearance):index<4?new Vector3(tangent,0,-z-clearance):index<6?new Vector3(-x-clearance,0,tangent):new Vector3(x+clearance,0,tangent);
            return center+Quaternion.Euler(0,rotation*90,0)*local;
        }
        public Vector3 BuildPoint(int index)=>BuildPointFor(Kind,transform.position,Rotation,index);
        public bool CanBuildFrom(GatheringWorker worker)=>Enumerable.Range(0,BuildPointCount).Any(i=>worker.PathDistance(BuildPoint(i))<float.PositiveInfinity);
        public int Id {get;private set;}
        public int Rotation {get;private set;}
        public int Priority {get;private set;}=1;
        internal int PriorityPosition {get;set;}
        internal long PriorityKey=>((long)(2-Priority)<<32)|(uint)(PriorityPosition>0?PriorityPosition:Id);
        internal int AutoCrewGroup {get;set;}
        public int PlannedBuilders=>World.Workers.Count(worker=>worker.BuildTarget==this||worker.Orders.Any(order=>order.building==this));
        public string PriorityName=>Priority==2?"优先":Priority==0?"稍后":"普通";
        public OrderResult SetPriority(int priority)
        {
            if(priority<0||priority>2)return OrderResult.No("优先级无效");
            if(!World.Ready||Complete||Cancelled)return OrderResult.No("只能调整未完工工地");
            Priority=priority;return World.PriorityChanged(this);
        }
        internal void MatchPriority(ConstructionSite neighbour){Priority=neighbour.Priority;}
        private bool ClaimsSupply=>!Cancelled&&!Complete&&!Paused&&World.Workers.Any(w=>w.BuildTarget==this&&w.Phase!=WorkerPhase.Blocked);
        public ConstructionSite SupplyAhead(ResourceKind kind)=>World.Buildings.Where(b=>b.PriorityKey<PriorityKey&&b.ClaimsSupply&&b.Needed(kind)>0).OrderBy(b=>b.PriorityKey).FirstOrDefault();
        public int AvailableSupply(ResourceKind kind)
        {
            if(Paused||Complete||Cancelled)return 0;
            long rank=PriorityKey;int held=World.Buildings.Where(b=>b.PriorityKey<rank&&b.ClaimsSupply).Sum(b=>b.Needed(kind));
            return Mathf.Min(Needed(kind),Mathf.Max(0,World.Inventory[(int)kind]-held));
        }
        public readonly int[] Delivered=new int[3];
        public float Labour {get;private set;}
        public bool Paused {get;private set;}
        public bool Complete {get;private set;}
        public bool Cancelled {get;private set;}
        public GatheringWorld World {get;private set;}
        private GameObject body;
        private NavMeshObstacle obstacle;
        private Collider footprint;
        public static int Cost(ResourceKind kind)=>kind==ResourceKind.Wood?18:kind==ResourceKind.Ore?6:0;
        public static Vector3 Size(int rotation)=>rotation%2==0?new Vector3(3,.3f,2):new Vector3(2,.3f,3);
        public Vector3 WorkPoint(int index)=>PointFor(Kind,transform.position,Rotation,index);
        public static Vector3 Point(Vector3 center,int rotation,int index)=>center+Quaternion.Euler(0,rotation*90,0)*new Vector3(index==0?-.7f:.7f,0,1.9f);
        public int InTransit(ResourceKind kind)=>World.Workers.Where(w=>w.CargoSite==this&&w.CargoKind==kind).Sum(w=>w.CargoCount);
        public int Needed(ResourceKind kind)=>Mathf.Max(0,MaterialCost(kind)-Delivered[(int)kind]-InTransit(kind));
        public bool Supplied=>Delivered[0]>=MaterialCost(ResourceKind.Wood)&&Delivered[1]>=MaterialCost(ResourceKind.Ore);
        public string Status=>Cancelled?"已取消":Complete?(IsStockyard?"已完工 · 装卸点+2":IsRoad?"已完工 · 行走+10%":"已完工 · 阻挡通行"):Paused?"已暂停":Assigned==0?(PlannedBuilders>0?"已排队施工":AutoCrewGroup>0?"等待空闲工人":"尚未派工"):Supplied?$"施工 {Labour/LabourRequired:P0}":"等待运料";
        public int Hauling=>World.Workers.Count(w=>w.BuildTarget==this&&(w.Phase>=WorkerPhase.ToSupply&&w.Phase<=WorkerPhase.UnloadingSite));
        public int Constructing=>World.Workers.Count(w=>w.BuildTarget==this&&w.Phase==WorkerPhase.Constructing);
        public int Waiting=>Assigned-Hauling-Constructing;
        public string SupplyStatus
        {
            get
            {
                if(Cancelled||Complete||Paused)return Status;
                if(World.Workers.Any(w=>w.BuildTarget==this&&w.Phase==WorkerPhase.Blocked))return "路线受阻：检查工地入口、仓库及路障";
                var ahead=Enumerable.Range(0,2).Select(k=>new{kind=(ResourceKind)k,site=SupplyAhead((ResourceKind)k)}).FirstOrDefault(x=>x.site&&Needed(x.kind)>0&&AvailableSupply(x.kind)==0&&World.Inventory[(int)x.kind]>0);
                if(ahead!=null)return $"等待优先工地 {ahead.site.Id} 领用{GatheringWorld.Name(ahead.kind)}；在途照常送达";
                string staff=Assigned>0?"":PlannedBuilders>0?"工人完成前段后到场；":AutoCrewGroup>0?"等待可达的空闲工人；":"尚未派工，可点派工按钮；";
                if(Supplied)return staff+(Constructing>0?"材料已齐，正在施工":"材料已齐，等待工人施工");
                var missing=Enumerable.Range(0,2).Where(k=>Needed((ResourceKind)k)>World.Inventory[k]).Select(k=>GatheringWorld.Name((ResourceKind)k)+" "+(Needed((ResourceKind)k)-World.Inventory[k]));
                string shortage=string.Join("、",missing);
                return staff+(shortage!=""?"仓库还缺："+shortage+"，请安排采集入库":InTransit(ResourceKind.Wood)+InTransit(ResourceKind.Ore)>0?"建材在运输，送达后自动继续":"仓库有料，等待工人搬运");
            }
        }
        public OrderResult AssignIdle(int count)
        {
            if(World.Paused||World.RoutesSettling||Paused||Complete||Cancelled)return OrderResult.No("当前不能派工，请检查暂停或道路状态");
            int accepted=0;
            foreach(var w in World.Workers.Where(w=>w.Phase==WorkerPhase.Idle&&w.CargoCount==0&&w.QueueCount==0).OrderBy(w=>Vector3.Distance(w.transform.position,WorkPoint(0))))
            {if(w.Build(this).Accepted)accepted++;if(accepted>=count)break;}
            return accepted>0?OrderResult.Yes($"已安排 {accepted}/{count} 名空闲工人前往{Title}"):OrderResult.No("没有可达的空闲空手工人；可选择工人手动派工");
        }
        public int Assigned=>World.Workers.Count(w=>w.BuildTarget==this);
        public void Initialize(GatheringWorld world,int id,int rotation,BuildingKind kind=BuildingKind.Stockyard)
        {
            World=world;Id=id;Rotation=rotation%4;Kind=kind;
            var size=FootprintSize;float height=IsRoad?.045f:.15f;
            body=world.Shape(DisplayName,PrimitiveType.Cube,transform.position+Vector3.up*height,new Vector3(size.x,IsRoad?.06f:.3f,size.z),new Color(.2f,.65f,.85f),transform,true);
            footprint=body.GetComponent<Collider>();
            if(!IsRoad){obstacle=body.AddComponent<NavMeshObstacle>();obstacle.shape=NavMeshObstacleShape.Box;obstacle.size=Vector3.one;obstacle.carving=true;obstacle.carveOnlyStationary=false;}
        }
        public static string PlacementReason(GatheringWorld world,Vector3 center,int rotation,BuildingKind kind=BuildingKind.Stockyard)
        {
            if(kind!=BuildingKind.Stockyard)return InfrastructurePlacement.Reason(world,center,rotation,kind);
            var size=Size(rotation);float hx=size.x*.5f+.65f,hz=size.z*.5f+.65f;
            if(Mathf.Abs(center.x)+hx>GatheringWorld.HalfWidth-.3f||Mathf.Abs(center.z)+hz>GatheringWorld.HalfDepth-.3f)return "建筑或工作入口超出场地边界";
            if(Mathf.Abs(center.x+2)<hx+.5f)return "请保留练习主通道";
            if(Mathf.Abs(center.x+6)<hx+1.5f&&Mathf.Abs(center.z+2)<hz+1.05f)return "与初始仓库或入口重叠";
            foreach(var r in world.Resources.Where(r=>r.Stock>0))
            {
                if(Mathf.Abs(center.x-r.transform.position.x)<hx+r.Radius&&Mathf.Abs(center.z-r.transform.position.z)<hz+r.Radius)return "与资源或采集入口重叠";
                for(int i=0;i<4;i++)if(Inside(r.Slot(i),center,hx,hz))return "需要保留资源工作位";
            }
            foreach(var b in world.Buildings.Where(b=>!b.Cancelled))
            {
                var other=b.FootprintSize;
                if(b.IsRoad)
                {
                    if(Inside(b.transform.position,center,(size.x+other.x)*.5f-.001f,(size.z+other.z)*.5f-.001f))return "与已有路面重叠";
                    continue;
                }
                if(Mathf.Abs(center.x-b.transform.position.x)<hx+other.x*.5f&&Mathf.Abs(center.z-b.transform.position.z)<hz+other.z*.5f)return "与已有建筑重叠";
                for(int i=0;i<2&&b.IsStockyard;i++)if(Inside(b.WorkPoint(i),center,hx,hz))return "需要保留已有建筑入口";
            }
            for(int i=1;i<=world.Workers.Count;i++)if(Inside(world.Home(i),center,hx,hz))return "需要保留工人待命区";
            if(world.Workers.Any(w=>Inside(w.transform.position,center,hx,hz)))return "建筑范围有人，请先移开";
            for(int i=0;i<2;i++)
            {
                var p=Point(center,rotation,i);
                if(Mathf.Abs(p.x)>GatheringWorld.HalfWidth-.5f||Mathf.Abs(p.z)>GatheringWorld.HalfDepth-.5f||!NavMesh.SamplePosition(p,out var hit,.2f,NavMesh.AllAreas))return "工作入口没有可行走地面";
                var path=new NavMeshPath();if(!NavMesh.CalculatePath(new Vector3(-6,0,-.15f),hit.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)return "工作入口与仓库不连通";
                if(world.Buildings.Any(b=>b.BlocksMovement&&Inside(p,b.transform.position,b.FootprintSize.x*.5f+.4f,b.FootprintSize.z*.5f+.4f)))return "工作入口被建筑占用";
            }
            return PlacementConnectivity.Check(world,center,rotation);
        }
        private static bool Inside(Vector3 p,Vector3 center,float hx,float hz)=>Mathf.Abs(p.x-center.x)<hx&&Mathf.Abs(p.z-center.z)<hz;
        private bool UsingBuildPoint(GatheringWorker worker)=>worker.BuildTarget==this&&worker.Slot>=0&&(worker.Phase==WorkerPhase.ToSite||worker.Phase==WorkerPhase.UnloadingSite||worker.Phase==WorkerPhase.Constructing);
        private bool BuildPointOccupied(GatheringWorker worker,Vector3 point)
        {
            foreach(var other in World.Workers)
            {
                if(other==worker)continue;
                if(Vector3.Distance(other.transform.position,point)<.6f)return true;
                if(other.BuildTarget&&other.Slot>=0&&(other.Phase==WorkerPhase.ToSite||other.Phase==WorkerPhase.UnloadingSite||other.Phase==WorkerPhase.Constructing)&&Vector3.Distance(other.BuildTarget.BuildPoint(other.Slot),point)<.6f)return true;
            }
            return false;
        }
        private void WaitNearby(GatheringWorker worker,string reason)
        {
            var point=Enumerable.Range(0,BuildPointCount).Select(i=>BuildPointFor(Kind,transform.position,Rotation,i,.9f))
                .Where(p=>!World.Workers.Any(w=>w!=worker&&Vector3.Distance(w.transform.position,p)<.7f))
                .Select(p=>new{point=p,distance=worker.PathDistance(p)}).OrderBy(p=>p.distance).FirstOrDefault();
            worker.WaitAt(point!=null&&!float.IsPositiveInfinity(point.distance)?point.point:worker.transform.position,reason);
        }
        public void TryWorker(GatheringWorker worker)
        {
            if(World.RoutesSettling)return;
            if(Cancelled||Complete){worker.SiteUnavailable();return;}
            if(Paused){worker.WaitAt(worker.transform.position,"工地已暂停");return;}
            if(worker.CargoSite!=this&&!Supplied)
            {
                bool stock=Enumerable.Range(0,2).Any(k=>AvailableSupply((ResourceKind)k)>0);
                if(stock)World.TryDepotSlot(worker,true);
                else WaitNearby(worker,SupplyStatus);
                return;
            }
            bool reachable=false;
            bool full=World.Workers.Count(w=>w!=worker&&UsingBuildPoint(w))>=2;
            foreach(var candidate in Enumerable.Range(0,BuildPointCount).Select(i=>new{index=i,point=BuildPoint(i),distance=worker.PathDistance(BuildPoint(i))}).OrderBy(p=>p.distance))
            {
                if(float.IsPositiveInfinity(candidate.distance))continue;reachable=true;
                if(full||BuildPointOccupied(worker,candidate.point))continue;
                worker.GoToSite(candidate.index,candidate.point);return;
            }
            if(!reachable){worker.NoRoute("工地周边没有可达施工点，建材保留");return;}
            WaitNearby(worker,"已有两人在运料/施工或近侧站位被占用，等待空位");
        }
        public void Receive(ResourceKind kind,int count)
        {Delivered[(int)kind]+=count;World.Notice=$"{Title}：{GatheringWorld.Name(kind)} +{count} 已到工地";}
        public void Work(float dt)
        {
            if(Complete||Cancelled||Paused||!Supplied)return;
            Labour=Mathf.Min(LabourRequired,Labour+dt);
            if(Labour<LabourRequired)return;
            Complete=true;ShowComplete();
            if(IsStockyard)World.OpenDepot(this);if(IsRoad)World.RoadFinished(this);else World.RoutesChanged();World.Notice=$"{Title} · {Status}";
        }
        private void ShowComplete()
        {
            if(!IsStockyard)
            {
                var shape=FootprintSize;body.transform.position=transform.position+Vector3.up*(IsRoad?.045f:.65f);body.transform.localScale=shape;
                body.GetComponent<Renderer>().sharedMaterial.color=IsRoad?new Color(.62f,.64f,.65f):new Color(.49f,.46f,.44f);
                return;
            }
            body.transform.position=transform.position+Vector3.up*.55f;var size=Size(Rotation);body.transform.localScale=new Vector3(size.x,1.1f,size.z);
            body.GetComponent<Renderer>().sharedMaterial.color=new Color(.44f,.46f,.47f);
            World.Shape("Stockyard timber",PrimitiveType.Cube,transform.position+Vector3.up*1.25f,new Vector3(size.x*.8f,.3f,size.z*.7f),new Color(.54f,.49f,.45f),transform);
        }
        public OrderResult TogglePause()
        {
            if(World.Paused)return OrderResult.No("游戏已暂停");
            if(Complete||Cancelled)return OrderResult.No("工地已经结束");
            Paused=!Paused;return OrderResult.Yes(Paused?"工地暂停；在途材料退库，保留施工进度":"工地继续");
        }
        public OrderResult Cancel()
        {
            if(World.Paused)return OrderResult.No("游戏已暂停");
            if(Complete||Cancelled)return OrderResult.No("工地已经结束，不能重复取消");
            Cancelled=true;if(obstacle)obstacle.enabled=false;footprint.enabled=false;body.SetActive(false);
            for(int k=0;k<3;k++){World.SpawnRefund((ResourceKind)k,Delivered[k],transform.position+Vector3.right*(k*.8f-.4f));Delivered[k]=0;}
            foreach(var w in World.Workers.Where(w=>w.BuildTarget==this).ToArray())w.SiteUnavailable();
            World.RefreshHeathClearances();World.RoutesChanged();World.Notice="工地已取消；已到材料变成退料箱，在途材料退回仓库";return OrderResult.Yes(World.Notice);
        }
    }
}

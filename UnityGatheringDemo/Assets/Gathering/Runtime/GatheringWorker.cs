using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace GatheringDemo
{
    public readonly struct OrderResult
    {
        public readonly bool Accepted;
        public readonly string Reason;
        private OrderResult(bool accepted,string reason){Accepted=accepted;Reason=reason;}
        public static OrderResult Yes(string reason="已接受")=>new OrderResult(true,reason);
        public static OrderResult No(string reason)=>new OrderResult(false,reason);
    }

    public sealed partial class GatheringWorker : MonoBehaviour
    {
        public struct Order
        {
            public GatheringResource source;
            public ConstructionSite building;
            public Vector3 point;
            public bool transport,once,autoBuild;
            public long taskId;
            public string Label=>building?$"建造{building.Title}":source?$"{source.DisplayName} · {(once?"一趟":"持续")}":transport?"送回仓库":$"移动 ({point.x:0.0}, {point.z:0.0})";
        }
        public int Id { get; private set; }
        public long TaskId {get;private set;}
        public WorkerPhase Phase { get; private set; }
        public GatheringResource Source { get; private set; }
        public GatheringResource LastSource { get; private set; }
        public ResourceKind CargoKind { get; private set; }
        public int CargoCount { get; private set; }
        public float CargoWeight=>CargoCount*unitWeight;
        public bool ReturningMaterials {get;private set;}
        public ConstructionSite BuildTarget {get;private set;}
        public ConstructionSite CargoSite {get;private set;}
        public float Progress { get; private set; }
        public bool FinishAfterTrip { get; private set; }
        public bool ContinueSameKind { get; set; }
        public int Slot { get; private set; }=-1;
        public int Bay { get; private set; }=-1;
        public long QueueTicket { get; private set; }
        public int QueueCount=>orders.Count;
        public IReadOnlyList<Order> Orders=>orders;
        public int BatchCount=>batchCount;
        public float WaitingSeconds=>Mathf.Max(0,world.Elapsed-waitingSince);
        public int RetryCount { get; private set; }
        public string BlockReason { get; private set; }="";
        public string WaitReason { get; private set; }="";
        public float Speed=>world.Settings.Speed(CargoWeight)*world.SurfaceSpeed(transform.position);
        public bool Selected { get=>selection.activeSelf; set=>selection.SetActive(value); }
        public Vector3 Destination=>destination;
        private GatheringWorld world;
        private NavMeshAgent agent;
        private GameObject selection,cargoVisual;
        private readonly List<Order> orders=new List<Order>();
        private float unitWeight,workDuration,unloadDuration,stalled,bestDistance,retryTimer,waitingSince,nextSlotCheck;
        private int batchCount;
        private bool deliverBeforeWork;
        private Vector3 destination;
        private WorkerPhase blockedPhase;
        private LineRenderer pathLine;

        public void Initialize(int id,GatheringWorld owner,GameObject ring,GameObject cargo)
        {
            Id=id;world=owner;selection=ring;cargoVisual=cargo;selection.SetActive(false);cargoVisual.SetActive(false);
            agent=gameObject.AddComponent<NavMeshAgent>();agent.radius=.28f;agent.height=1.3f;agent.speed=Speed;agent.acceleration=14;
            agent.angularSpeed=540;agent.stoppingDistance=.1f;agent.obstacleAvoidanceType=ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            agent.SetAreaCost(0,GatheringWorld.BasicRoadMultiplier);agent.SetAreaCost(GatheringWorld.RoadArea,1);
            agent.avoidancePriority=30+id;agent.autoRepath=true;
            pathLine=gameObject.AddComponent<LineRenderer>();pathLine.material=new Material(Shader.Find("Sprites/Default"));pathLine.startColor=pathLine.endColor=new Color(.2f,.6f,.85f,.7f);
            pathLine.startWidth=pathLine.endWidth=.045f;pathLine.useWorldSpace=true;
        }
        public void Tick(float dt)
        {
            if(!agent.isOnNavMesh){if(Phase!=WorkerPhase.Blocked)Block("工人离开可行走区域");return;}
            agent.isStopped=world.Paused;agent.speed=Speed;
            if(world.Paused)return;
            if(BuildTarget&&(BuildTarget.Cancelled||BuildTarget.Complete)){SiteUnavailable();return;}
            if(BuildTarget&&BuildTarget.Paused&&Phase!=WorkerPhase.ToDepot&&Phase!=WorkerPhase.WaitingDepot&&Phase!=WorkerPhase.Unloading)
            {
                CargoSite=null;Slot=Bay=-1;
                if(agent.hasPath)agent.ResetPath();
                if(CargoCount>0)BeginDelivery();else {Phase=WorkerPhase.WaitingBuild;WaitReason="工地已暂停";}
                return;
            }
            if(Phase==WorkerPhase.Blocked){retryTimer-=dt;if(retryTimer<=0&&RetryCount<3)Retry(false);return;}
            if(Phase==WorkerPhase.WaitingBuild)
            {nextSlotCheck-=dt;if(nextSlotCheck<=0){nextSlotCheck=.25f;BuildTarget.TryWorker(this);}return;}
            if(Phase==WorkerPhase.WaitingWork||Phase==WorkerPhase.WaitingDepot)
            {
                nextSlotCheck-=dt;
                if(nextSlotCheck<=0){nextSlotCheck=.25f;if(Phase==WorkerPhase.WaitingWork)world.TryWorkSlot(this);else world.TryDepotSlot(this);}
                return;
            }
            if(Phase==WorkerPhase.Moving||Phase==WorkerPhase.ToWork||Phase==WorkerPhase.ToDepot||Phase==WorkerPhase.ToSupply||Phase==WorkerPhase.ToSite)
            {
                if(agent.pathPending)return;
                if(agent.pathStatus!=NavMeshPathStatus.PathComplete){Block("道路中断，无法到达目标");return;}
                if(agent.remainingDistance<=.16f)
                {
                    stalled=0;RetryCount=0;agent.ResetPath();
                    if(Phase==WorkerPhase.Moving){Idle();return;}
                    Progress=0;
                    if(Phase==WorkerPhase.ToWork)Phase=WorkerPhase.Working;
                    else if(Phase==WorkerPhase.ToSupply){Phase=WorkerPhase.LoadingSupply;unloadDuration=.5f;}
                    else if(Phase==WorkerPhase.ToSite){Phase=CargoCount>0?WorkerPhase.UnloadingSite:WorkerPhase.Constructing;unloadDuration=world.Settings.UnloadTime(CargoCount);}
                    else {Phase=WorkerPhase.Unloading;unloadDuration=world.Settings.UnloadTime(CargoCount);}
                    return;
                }
                if(agent.remainingDistance<bestDistance-.15f){bestDistance=agent.remainingDistance;stalled=0;}else stalled+=dt;
                if(stalled>10)Block("拥堵超过 10 秒，正在尝试重新寻路");return;
            }
            if(Phase==WorkerPhase.Working)
            {
                if(!Source||Source.Stock==0){Depleted();return;}
                if(batchCount==0)
                {
                    batchCount=Source.Reserve(world.Settings.Batch(Source.Kind,Source.Stock));
                    if(batchCount==0){WaitReason="剩余产出已由其他工人预约";return;}
                    unitWeight=world.Settings.Weight(Source.Kind);workDuration=world.Settings.GatherTime(Source.Work,batchCount);
                }
                Progress=Mathf.Min(1,Progress+dt/workDuration);
                if(Progress>=1)
                {
                    CargoKind=Source.Kind;CargoCount=batchCount;ReturningMaterials=Source.IsRefund;Source.Harvest(batchCount);batchCount=0;cargoVisual.SetActive(true);BeginDelivery();
                }
            }
            else if(Phase==WorkerPhase.Unloading)
            {
                Progress=Mathf.Min(1,Progress+dt/unloadDuration);
                if(Progress>=1)
                {
                    world.Deposit(this,CargoKind,CargoCount,CargoWeight);CargoCount=0;ReturningMaterials=false;cargoVisual.SetActive(false);Bay=-1;
                    if(BuildTarget){BeginBuild();return;}
                    if(deliverBeforeWork){deliverBeforeWork=false;BeginWork();return;}
                    bool ended=FinishAfterTrip;FinishAfterTrip=false;
                    // A queued command takes over at the next completed delivery, even for continuous gathering.
                    if(orders.Count>0){Source=null;NextOrHome();}
                    else if(!ended&&Source)BeginWork();
                    else {Source=null;NextOrHome();}
                }
            }
            else if(Phase==WorkerPhase.LoadingSupply)
            {
                Progress=Mathf.Min(1,Progress+dt/unloadDuration);if(Progress<1)return;
                Bay=-1;
                for(int k=0;k<2;k++)
                {
                    var kind=(ResourceKind)k;int count=world.Settings.Batch(kind,BuildTarget.AvailableSupply(kind));if(count==0)continue;
                    world.Inventory[k]-=count;CargoKind=kind;CargoCount=count;unitWeight=world.Settings.Weight(kind);ReturningMaterials=true;CargoSite=BuildTarget;cargoVisual.SetActive(true);break;
                }
                BeginBuild();
            }
            else if(Phase==WorkerPhase.UnloadingSite)
            {
                Progress=Mathf.Min(1,Progress+dt/unloadDuration);if(Progress<1)return;
                BuildTarget.Receive(CargoKind,CargoCount);CargoCount=0;CargoSite=null;ReturningMaterials=false;cargoVisual.SetActive(false);BeginBuild();
            }
            else if(Phase==WorkerPhase.Constructing)BuildTarget.Work(dt);
        }
        public void DrawPath()
        {
            if(!Selected||!agent.isOnNavMesh||!agent.hasPath){pathLine.positionCount=0;return;}
            var corners=agent.path.corners;pathLine.positionCount=corners.Length;
            for(int i=0;i<corners.Length;i++)pathLine.SetPosition(i,corners[i]+Vector3.up*.04f);
        }
        private bool Navigate(Vector3 point,WorkerPhase next)
        {
            destination=point;Phase=next;stalled=0;bestDistance=float.PositiveInfinity;
            if(!agent.isOnNavMesh){Block("工人离开可行走区域");return false;}
            var path=new NavMeshPath();
            if(!agent.CalculatePath(point,path)||path.status!=NavMeshPathStatus.PathComplete){Block("道路中断，无法到达目标");return false;}
            agent.isStopped=false;agent.SetPath(path);return true;
        }
        public bool CanReach(Vector3 point)
        {
            var path=new NavMeshPath();return agent.isOnNavMesh&&agent.CalculatePath(point,path)&&path.status==NavMeshPathStatus.PathComplete;
        }
        public float PathDistance(Vector3 point)
        {
            if(!agent.isOnNavMesh||!NavMesh.SamplePosition(point,out var hit,.15f,agent.areaMask))return float.PositiveInfinity;
            var path=new NavMeshPath();if(!agent.CalculatePath(hit.position,path)||path.status!=NavMeshPathStatus.PathComplete)return float.PositiveInfinity;
            float length=0;var corners=path.corners;for(int i=1;i<corners.Length;i++)length+=Vector3.Distance(corners[i-1],corners[i]);return length;
        }
        public void GoToWork(int slot,Vector3 point){Slot=slot;WaitReason="";Navigate(point,WorkerPhase.ToWork);}
        public void GoToDepot(int bay,Vector3 point){Bay=bay;WaitReason="";Navigate(point,WorkerPhase.ToDepot);}
        public void GoToSupply(int bay,Vector3 point){Bay=bay;WaitReason="";Navigate(point,WorkerPhase.ToSupply);}
        public void GoToSite(int slot,Vector3 point){Slot=slot;WaitReason="";Navigate(point,WorkerPhase.ToSite);}
        private void BeginBuild(){Slot=Bay=-1;Progress=0;QueueTicket=world.NextTicket();waitingSince=world.Elapsed;nextSlotCheck=0;Phase=WorkerPhase.WaitingBuild;}
        public OrderResult Build(ConstructionSite site,bool append=false,bool autoBuild=false)
        {
            if(world.Paused)return OrderResult.No("已暂停");
            if(!site||site.Complete||site.Cancelled)return OrderResult.No("该工地已经结束");
            if(!site.CanBuildFrom(this))return OrderResult.No("工地不可达，原任务保留");
            if(append&&Phase!=WorkerPhase.Idle)return Enqueue(new Order{building=site,autoBuild=autoBuild});
            if(BuildTarget==site)return OrderResult.No("已在此工地工作");
            ResetTask();orders.Clear();Source=null;FinishAfterTrip=false;BuildTarget=site;
            if(CargoCount>0)BeginDelivery();else BeginBuild();return OrderResult.Yes("运料后自动施工，缺料时等待补给");
        }
        public void SiteUnavailable()
        {ResetTask();Source=null;FinishAfterTrip=false;if(CargoCount>0)BeginDelivery();else NextOrHome();}
        public void WaitAt(Vector3 point,string reason)
        {
            WaitReason=reason;
            if(!agent.isOnNavMesh||!NavMesh.SamplePosition(point,out var hit,1,NavMesh.AllAreas))return;
            if(agent.hasPath&&Vector3.Distance(destination,hit.position)<.25f||Vector3.Distance(transform.position,hit.position)<.2f)return;
            var path=new NavMeshPath();
            if(NavMesh.CalculatePath(transform.position,hit.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete)
            {destination=hit.position;agent.SetPath(path);}
        }
        private void CancelBatch(){if(Source&&batchCount>0)Source.Release(batchCount);batchCount=0;Progress=0;}
        private void ResetTask()
        {
            TaskId=world.NextTicket();
            CancelBatch();Slot=Bay=-1;RetryCount=0;stalled=0;deliverBeforeWork=false;WaitReason=BlockReason="";
            BuildTarget=null;CargoSite=null;
            if(agent.isOnNavMesh)agent.ResetPath();
        }
        public OrderResult Work(GatheringResource source,bool append=false,bool once=false)
        {
            if(world.Paused)return OrderResult.No("已暂停");
            if(!source||source.Stock==0)return OrderResult.No("资源已采空");
            if(world.Settings.Batch(source.Kind,source.Stock)==0)return OrderResult.No("载重不足以容纳一件资源");
            if(!ReachResource(source))return OrderResult.No("资源不可达，原任务保留");
            if(append&&Phase!=WorkerPhase.Idle)return Enqueue(new Order{source=source,once=once});
            if(Source==source&&Phase!=WorkerPhase.Blocked&&!FinishAfterTrip&&!once)return OrderResult.No("已在执行该采集任务");
            ResetTask();orders.Clear();FinishAfterTrip=once;Source=LastSource=source;
            if(CargoCount>0){deliverBeforeWork=true;BeginDelivery();}else BeginWork();return OrderResult.Yes();
        }
        private bool ReachResource(GatheringResource source)
        {for(int i=0;i<4;i++)if(CanReach(source.Slot(i)))return true;return false;}
        public OrderResult Move(Vector3 point,bool append=false)
        {
            if(world.Paused)return OrderResult.No("已暂停");
            if(!CanReach(point))return OrderResult.No("落点不可达，原任务保留");
            if(append&&Phase!=WorkerPhase.Idle)return Enqueue(new Order{point=point});
            ResetTask();orders.Clear();FinishAfterTrip=false;Source=null;Navigate(point,WorkerPhase.Moving);return OrderResult.Yes();
        }
        public OrderResult Deliver(bool append=false)
        {
            if(world.Paused)return OrderResult.No("已暂停");
            if(CargoCount==0)return OrderResult.No("未携货，原任务保留");
            if(append&&Phase!=WorkerPhase.Idle)return Enqueue(new Order{transport=true});
            if(Source==null&&(Phase==WorkerPhase.ToDepot||Phase==WorkerPhase.Unloading||Phase==WorkerPhase.WaitingDepot))return OrderResult.No("已在送货");
            ResetTask();orders.Clear();Source=null;FinishAfterTrip=false;BeginDelivery();return OrderResult.Yes();
        }
        public OrderResult Stop()
        {
            if(world.Paused)return OrderResult.No("已暂停");
            ResetTask();orders.Clear();Source=null;FinishAfterTrip=false;Phase=WorkerPhase.Idle;
            return OrderResult.Yes(CargoCount>0?"已停止，货物保留":"已停止");
        }
        public OrderResult FinishTrip()
        {
            if(world.Paused)return OrderResult.No("已暂停");
            if(BuildTarget)return OrderResult.No("建造请用停止、工地暂停或追加后续任务");
            if(!Source&&CargoCount==0)return OrderResult.No("没有采集或送货任务");
            orders.Clear();FinishAfterTrip=true;
            deliverBeforeWork=false;
            if(CargoCount>0&&Phase!=WorkerPhase.Unloading&&Phase!=WorkerPhase.ToDepot&&Phase!=WorkerPhase.WaitingDepot){ResetTask();Source=null;BeginDelivery();}
            return OrderResult.Yes("本轮入库后结束；后续队列已清空");
        }
        public OrderResult Resume()
        {
            if(Phase!=WorkerPhase.Idle)return OrderResult.No("仍有任务，请停止后再恢复");
            if(!LastSource||LastSource.Stock==0)return OrderResult.No("原资源已采空，请重新指定资源");
            return Work(LastSource);
        }
        public OrderResult Retry(bool manual=true)
        {
            if(world.Paused)return OrderResult.No("已暂停");
            if(Phase!=WorkerPhase.Blocked)return OrderResult.No("当前未受阻");
            RetryCount=manual?0:RetryCount+1;
            if(BuildTarget){if(CargoCount>0&&CargoSite!=BuildTarget)BeginDelivery();else BeginBuild();}
            else if(CargoCount>0&&(blockedPhase==WorkerPhase.ToDepot||blockedPhase==WorkerPhase.WaitingDepot))BeginDelivery();
            else if(Source)BeginWork();else Navigate(destination,WorkerPhase.Moving);
            return OrderResult.Yes("已重新检查路线，任务与货物保留");
        }
        public void RouteChanged()
        {
            if(Phase==WorkerPhase.Blocked){RetryCount=0;retryTimer=.6f;}
            else if(BuildTarget&&Phase==WorkerPhase.ToSite){if(agent&&agent.isOnNavMesh)agent.ResetPath();BeginBuild();}
            else if(agent&&agent.isOnNavMesh&&agent.hasPath)Navigate(destination,Phase);
        }
        private void Block(string reason)
        {
            if(Phase!=WorkerPhase.Blocked)blockedPhase=Phase;
            CancelBatch();Phase=WorkerPhase.Blocked;Slot=Bay=-1;retryTimer=3;BlockReason=reason;
            if(agent.isOnNavMesh)agent.ResetPath();world.Notice=$"{Id} 号：{reason}；任务与货物保留";
        }
        public void NoRoute(string reason){Block(reason);}
        public void ResourceDepleted(){Depleted();}
        private void BeginWork(){Slot=Bay=-1;QueueTicket=world.NextTicket();waitingSince=world.Elapsed;nextSlotCheck=0;if(!Source||Source.Stock==0){Depleted();return;}Phase=WorkerPhase.WaitingWork;}
        private void BeginDelivery(){Slot=Bay=-1;QueueTicket=world.NextTicket();waitingSince=world.Elapsed;nextSlotCheck=0;Phase=WorkerPhase.WaitingDepot;}
        private void Depleted()
        {
            var previous=Source;CancelBatch();Slot=-1;
            if(ContinueSameKind&&!FinishAfterTrip&&orders.Count==0&&previous)
            {var next=world.FindResource(previous.Work,this);if(next){Work(next);return;}}
            Source=null;FinishAfterTrip=false;NextOrHome();
        }
        private OrderResult Enqueue(Order order)
        {
            if(orders.Count>=16)return OrderResult.No("队列已满（16 条）");
            if(orders.Count>0)
            {var last=orders[orders.Count-1];if(last.building==order.building&&last.source==order.source&&last.transport==order.transport&&last.once==order.once&&Vector3.Distance(last.point,order.point)<.2f)return OrderResult.No("与上一条后续任务重复");}
            order.taskId=world.NextTicket();orders.Add(order);return OrderResult.Yes("已追加；本轮入库、移动结束或建造完工后执行");
        }
        public OrderResult RemoveOrder(int index)
        {
            if(world.Paused)return OrderResult.No("已暂停");
            if(index<0||index>=orders.Count)return OrderResult.No("任务已变化");
            orders.RemoveAt(index);return OrderResult.Yes("已取消该后续任务，当前任务继续");
        }
        public OrderResult ClearOrders()
        {
            if(world.Paused)return OrderResult.No("已暂停");
            if(orders.Count==0)return OrderResult.No("没有后续任务");
            orders.Clear();return OrderResult.Yes("已清空后续任务，当前任务继续");
        }
        internal void SortAutoBuildOrders()
        {
            for(int start=0;start<orders.Count;)
            {
                if(!orders[start].autoBuild){start++;continue;}
                int end=start+1;while(end<orders.Count&&orders[end].autoBuild)end++;
                orders.Sort(start,end-start,autoOrderComparer);start=end;
            }
        }
        private static readonly IComparer<Order> autoOrderComparer=Comparer<Order>.Create((left,right)=>left.building.PriorityKey.CompareTo(right.building.PriorityKey));
        private void NextOrHome()
        {
            while(orders.Count>0)
            {
                var order=orders[0];
                if(order.autoBuild&&order.building&&order.building.Paused&&!order.building.Cancelled&&!order.building.Complete)
                {order.building.AutoCrewGroup=order.building.Id;orders.RemoveAt(0);continue;}
                if(order.autoBuild&&order.building&&!order.building.Cancelled&&!order.building.Complete&&CargoCount==0)
                {
                    var earlier=world.TakeEarlierPending(this,order.building);
                    if(earlier){ResetTask();FinishAfterTrip=false;Source=null;BuildTarget=earlier;BeginBuild();return;}
                }
                orders.RemoveAt(0);
                if(order.building&&(order.building.Cancelled||order.building.Complete)||order.source&&order.source.Stock==0||order.transport&&CargoCount==0)continue;
                // Keep an unreachable next command as the active blocked task, rather than losing it.
                ResetTask();TaskId=order.taskId;FinishAfterTrip=order.once;Source=order.source;
                if(order.building){BuildTarget=order.building;if(CargoCount>0)BeginDelivery();else BeginBuild();}
                else if(order.source){LastSource=order.source;if(CargoCount>0){deliverBeforeWork=true;BeginDelivery();}else BeginWork();}
                else if(order.transport)BeginDelivery();else Navigate(order.point,WorkerPhase.Moving);
                return;
            }
            Source=null;Navigate(world.Home(Id),WorkerPhase.Moving);
        }
        private void Idle(){Phase=WorkerPhase.Idle;Slot=Bay=-1;if(orders.Count>0)NextOrHome();}
        public string Status=>Phase==WorkerPhase.Blocked?$"受阻 · {(RetryCount>=3?"需恢复路线":$"{Mathf.CeilToInt(Mathf.Max(0,retryTimer))}秒后重试")}":
            Phase==WorkerPhase.WaitingBuild?"等待建造":Phase==WorkerPhase.ToSupply?"前往领料":Phase==WorkerPhase.LoadingSupply?"领料":Phase==WorkerPhase.ToSite?"前往工地":Phase==WorkerPhase.UnloadingSite?"交付建材":Phase==WorkerPhase.Constructing?"施工":
            Phase==WorkerPhase.Working?$"采集 {Progress:P0}":Phase==WorkerPhase.Unloading?$"卸货 {Progress:P0}":
            Phase==WorkerPhase.WaitingWork?$"等工作位 #{world.QueuePosition(this)}":Phase==WorkerPhase.WaitingDepot?$"等卸货位 #{world.QueuePosition(this)}":
            Phase==WorkerPhase.ToWork?"前往资源":Phase==WorkerPhase.ToDepot?"运输":Phase==WorkerPhase.Moving?"移动":CargoCount>0?"待命·携货":"待命";
        public string Detail=>Phase==WorkerPhase.Blocked?$"{BlockReason}；{(RetryCount>=3?"自动重试已用尽；修路后自动恢复，也可手动重试":$"自动重试 {RetryCount}/3")}；任务和货物保留":
            BuildTarget?$"{BuildTarget.Title} · {BuildTarget.Status} · {WaitReason}":
            Phase==WorkerPhase.WaitingWork||Phase==WorkerPhase.WaitingDepot?$"{WaitReason} · 已等 {WaitingSeconds:0.0} 秒"+(WaitingSeconds>15?"；入口可能被占用":""):
            Source?$"目标 {Source.DisplayName} · {(FinishAfterTrip?"本轮入库后结束":orders.Count>0?"本轮入库后执行队列":"持续往返")}":CargoCount>0?"货物保留，可送回仓库或重新派工":"点击地面移动，点击资源采集";
        private void OnDestroy(){if(pathLine&&pathLine.sharedMaterial)Destroy(pathLine.sharedMaterial);}
    }
}

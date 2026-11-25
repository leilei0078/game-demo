using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using UnityEngine.Rendering;

namespace GatheringDemo
{
    public sealed partial class GatheringWorld : MonoBehaviour
    {
        [SerializeField] private GatheringSettings settings;
        public GatheringSettings Settings => settings;
        public readonly List<GatheringWorker> Workers=new List<GatheringWorker>();
        public readonly List<GatheringResource> Resources=new List<GatheringResource>();
        public readonly List<ConstructionSite> Buildings=new List<ConstructionSite>();
        public readonly int[] Inventory=new int[3];
        public int DepotWoodPercent {get;private set;}=GatheringDepot.DefaultWoodPercent;
        public void SetDepotWoodPercent(int percent)=>DepotWoodPercent=Mathf.Clamp(percent,GatheringDepot.MinWoodPercent,GatheringDepot.MaxWoodPercent);
        public GatheringMetrics Metrics {get;}=new GatheringMetrics();
        public float Elapsed { get; private set; }
        public bool Paused { get; set; }
        public int CompletedTrips { get; set; }
        public int GuideStep {get;set;}
        public bool GuideHidden {get;set;}
        public int Extracted(ResourceKind kind)=>InitialResources(kind)-Resources.Where(r=>r.Kind==kind&&!r.IsRefund).Sum(r=>r.Stock);
        public string Notice { get; set; }="选中工人，再点击地面移动、资源采集、仓库送货";
        public Camera View { get; private set; }
        public bool Roadblock => roadblock.activeSelf;
        public bool Ready {get;private set;}
        public const float HalfWidth=18,HalfDepth=13;
        public Vector3 CameraFocus {get;private set;}
        private static readonly Quaternion MapViewRotation=Quaternion.Euler(30,225,0);
        internal void PositionMapCamera(float verticalOffset)
        {
            var position=CameraFocus+MapViewRotation*new Vector3(0,verticalOffset,-50);
            View.transform.SetPositionAndRotation(position,MapViewRotation);
        }
        public void SetCameraFocus(Vector3 point){CameraFocus=new Vector3(Mathf.Clamp(point.x,-HalfWidth+2,HalfWidth-2),0,Mathf.Clamp(point.z,-HalfDepth+2,HalfDepth-2));}
        public int RoadRevision {get;private set;}
        public bool RoutesSettling=>NavigationUpdating||Time.unscaledTime<routesReadyAt;
        private GameObject roadblock;
        private readonly List<Material> materials=new List<Material>();
        private Texture2D groundTexture;
        private WastelandSurface wasteland;
        private readonly List<Vector3> bays=new List<Vector3>{new Vector3(-6.95f,0,-.15f),new Vector3(-6,0,-.15f),new Vector3(-5.05f,0,-.15f)};
        private readonly int[] initialResources=new int[3];
        public int DepotBayCount=>bays.Count;
        public int InitialResources(ResourceKind kind)=>initialResources[(int)kind];
        public int AccountedResources(ResourceKind kind)=>Resources.Where(r=>r.Kind==kind).Sum(r=>r.Stock)+Workers.Where(w=>w.CargoKind==kind).Sum(w=>w.CargoCount)+Inventory[(int)kind]+Buildings.Sum(b=>b.Delivered[(int)kind]);
        private long serial;
        private float routesReadyAt;
        public long NextTicket()=>++serial;
        public void SetSettings(GatheringSettings value){settings=value;}
        private void Awake()
        {
            Application.runInBackground=true;
            if(!settings)settings=ScriptableObject.CreateInstance<GatheringSettings>();
            BuildWorld();
            foreach(var r in Resources)initialResources[(int)r.Kind]+=r.Initial;
            Metrics.Reset(Workers);
            gameObject.AddComponent<GatheringSaveService>();
        }
        private void Update()
        {
            UpdateRoadNavigation();
            if(!Ready)return;
            float dt=Mathf.Min(Time.deltaTime,.05f);if(!Paused)Elapsed+=dt;
            DispatchPlacementCrews();
            Metrics.Tick(dt,Workers,Paused);
            foreach(var w in Workers.OrderBy(w=>w.QueueTicket).ToArray())w.Tick(dt);
            foreach(var w in Workers)w.DrawPath();
        }
        public Vector3 Home(int id)=>new Vector3(-8.7f+(id-1)%4*1.3f,0,2.5f+(id-1)/4*1.3f);
        public void Deposit(GatheringWorker worker,ResourceKind kind,int count,float weight)
        {Inventory[(int)kind]+=count;if(!worker.ReturningMaterials){Metrics.Deposit(worker.Id,kind,count,weight);CompletedTrips++;}Notice=$"{Name(kind)} +{count} 已入库";}
        public void ResetMetrics(){Metrics.Reset(Workers);Notice="统计已重新开始，库存与任务保留；途中货物在入库时计入新窗口";}
        public static string Name(ResourceKind kind)=>kind==ResourceKind.Wood?"木材":kind==ResourceKind.Ore?"矿石":"果实";
        public GatheringResource FindResource(WorkKind work,GatheringWorker worker)=>Resources.Where(s=>s.Work==work&&s.Stock>0&&Enumerable.Range(0,4).Any(i=>worker.CanReach(s.Slot(i)))).OrderBy(s=>Workers.Count(w=>w.Source==s)*6+Vector3.Distance(worker.transform.position,s.transform.position)).FirstOrDefault();
        public int QueuePosition(GatheringWorker worker)=>Workers.Count(w=>w.Phase==worker.Phase&&(worker.Phase!=WorkerPhase.WaitingWork||w.Source==worker.Source)&&w.QueueTicket<worker.QueueTicket)+1;
        public void TryWorkSlot(GatheringWorker w)
        {
            if(RoutesSettling)return;
            if(!w.Source||w.Source.Stock==0){w.ResourceDepleted();return;}
            bool reachable=false;
            foreach(int i in Enumerable.Range(0,4).OrderBy(i=>Vector3.Distance(w.transform.position,w.Source.Slot(i))))
            {
                var point=w.Source.Slot(i);if(!w.CanReach(point))continue;reachable=true;
                if(Workers.Any(o=>o!=w&&(o.Source==w.Source&&o.Slot==i||Vector3.Distance(o.transform.position,point)<.65f)))continue;
                w.GoToWork(i,point);return;
            }
            if(!reachable){w.NoRoute("资源工作位不可达，检查道路或路障");return;}
            int rank=QueuePosition(w)-1;float angle=(rank%8)*Mathf.PI*.25f;
            w.WaitAt(w.Source.transform.position+new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*(w.Source.Radius+2.3f+(rank/8)*.9f),"工作位正在使用，按先到顺序等待");
        }
        public void TryDepotSlot(GatheringWorker w,bool supply=false)
        {
            if(RoutesSettling)return;
            bool reachable=false;
            foreach(int i in Enumerable.Range(0,bays.Count).OrderBy(i=>Vector3.Distance(w.transform.position,bays[i])))
            {
                if(!w.CanReach(bays[i]))continue;reachable=true;
                if(Workers.Any(o=>o!=w&&(o.Bay==i||Vector3.Distance(o.transform.position,bays[i])<.65f)))continue;
                if(supply)w.GoToSupply(i,bays[i]);else w.GoToDepot(i,bays[i]);return;
            }
            if(!reachable){w.NoRoute("仓库入口不可达，货物保留");return;}
            int rank=QueuePosition(w)-1;
            w.WaitAt(new Vector3(-8.4f+(rank%4)*.95f,0,1.1f+(rank/4)*.95f),"卸货位正在使用，按先到顺序等待");
        }
        public void ToggleRoadblock()
        {
            if(Paused)return;
            if(!roadblock.activeSelf&&Workers.Any(w=>Mathf.Abs(w.transform.position.x+2)<.65f)){Notice="通道中有人，先移开再放路障";return;}
            roadblock.SetActive(!roadblock.activeSelf);RefreshHeathClearances();RoutesChanged();
            Notice=Roadblock?"通道已封闭；任务与携货保留":"路障已移除；受阻工人将自动重新检查路线";
        }
        public void RoutesChanged(){RoadRevision++;routesReadyAt=Time.unscaledTime+.5f;foreach(var worker in Workers)worker.RouteChanged();}
        public void OpenDepot(ConstructionSite site){for(int i=0;i<2;i++)bays.Add(site.WorkPoint(i));}
        public void SpawnRefund(ResourceKind kind,int amount,Vector3 point)
        {if(amount>0)CreateResource("退料箱·"+Name(kind),kind,WorkKind.Pickup,amount,point,.32f);}
        internal void RefreshHeathClearances()=>wasteland?.RefreshClearances(this);
        public OrderResult PlaceBuilding(Vector3 point,int rotation,out ConstructionSite site)
        {
            site=null;if(Paused)return OrderResult.No("已暂停");
            if(RoutesSettling)return OrderResult.No("正在更新道路，请稍后放置");
            point=PlacementGrid.Center(point,PlacementGrid.BuildingStep);
            string reason=ConstructionSite.PlacementReason(this,point,rotation);if(reason!="")return OrderResult.No(reason);
            int id=Buildings.Count==0?1:Buildings.Max(b=>b.Id)+1;
            var root=new GameObject("Stockyard "+id);root.transform.SetParent(transform);root.transform.position=point;
            site=root.AddComponent<ConstructionSite>();site.Initialize(this,id,rotation);Buildings.Add(site);RefreshHeathClearances();RoutesChanged();
            Notice="物资堆场蓝图已放置；派空闲工人，或选人右键工地安排施工";return OrderResult.Yes(Notice);
        }
        private Material Material(Color color)
        {
            var m=new Material(Shader.Find("Standard"));m.color=color;materials.Add(m);return m;
        }
        internal GameObject Shape(string name,PrimitiveType kind,Vector3 pos,Vector3 scale,Color color,Transform parent=null,bool collision=false)
        {
            var go=GameObject.CreatePrimitive(kind);go.name=name;go.transform.SetParent(parent);go.transform.position=pos;go.transform.localScale=scale;go.GetComponent<Renderer>().sharedMaterial=Material(color);
            if(!collision){var c=go.GetComponent<Collider>();c.enabled=false;Destroy(c);}return go;
        }
        private void BuildWorld()
        {
            foreach(var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))c.gameObject.SetActive(false);
            var cam=new GameObject("Gathering Camera");View=cam.AddComponent<Camera>();View.orthographic=true;View.orthographicSize=10.5f;View.backgroundColor=new Color(.47f,.49f,.51f);View.clearFlags=CameraClearFlags.SolidColor;
            PositionMapCamera(0);cam.AddComponent<AudioListener>();
            var light=new GameObject("Sun").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.22f;light.color=new Color(.97f,.986f,1);light.transform.rotation=Quaternion.Euler(34,-39,0);light.shadows=LightShadows.Soft;
            light.shadowCustomResolution=2048;light.shadowBias=.048f;light.shadowNormalBias=.21f;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.58f,.60f,.635f);
            var ground=Shape("Walkable Ground",PrimitiveType.Cube,new Vector3(0,-.15f,0),new Vector3(HalfWidth*2,.3f,HalfDepth*2),Color.white,transform,true);ground.layer=8;
            wasteland=WastelandSurface.Attach(ground);
            if(wasteland==null)
            {
                groundTexture=WastelandSurface.CreateTexture(HalfWidth*2,HalfDepth*2);
                var groundMaterial=ground.GetComponent<Renderer>().sharedMaterial;
                groundMaterial.mainTexture=groundTexture;groundMaterial.SetFloat("_Glossiness",.06f);
            }
            var surface=ground.AddComponent<NavMeshSurface>();navigationSurface=surface;surface.collectObjects=CollectObjects.All;surface.layerMask=1<<8;surface.useGeometry=NavMeshCollectGeometry.PhysicsColliders;surface.overrideTileSize=true;surface.tileSize=64;surface.overrideVoxelSize=true;surface.voxelSize=.1f;surface.BuildNavMesh();
            var depot=Shape("Depot",PrimitiveType.Cube,new Vector3(-6,.55f,-2),new Vector3(3,1.1f,2.1f),new Color(.48f,.52f,.56f),transform,true);depot.AddComponent<GatheringDepot>().BuildRack(this,wasteland);
            var blocker=depot.AddComponent<NavMeshObstacle>();blocker.shape=NavMeshObstacleShape.Box;blocker.size=Vector3.one;blocker.carving=true;blocker.carveOnlyStationary=false;
            CreateResource("树木 A",ResourceKind.Wood,WorkKind.Chop,144,new Vector3(3.2f,0,-3),.8f);
            CreateResource("树木 B",ResourceKind.Wood,WorkKind.Chop,108,new Vector3(7,0,-1.5f),.8f);
            CreateResource("矿石",ResourceKind.Ore,WorkKind.Mine,144,new Vector3(6.2f,0,3.7f),1);
            CreateResource("果丛",ResourceKind.Food,WorkKind.Gather,90,new Vector3(-.8f,0,-3.8f),.65f);
            roadblock=Shape("Practice Roadblock",PrimitiveType.Cube,new Vector3(-2,.4f,0),new Vector3(.5f,.8f,HalfDepth*2),new Color(.48f,.45f,.43f),transform,true);
            var obstacle=roadblock.AddComponent<NavMeshObstacle>();obstacle.shape=NavMeshObstacleShape.Box;obstacle.size=Vector3.one;obstacle.carving=true;obstacle.carveOnlyStationary=false;roadblock.SetActive(false);
            for(int i=1;i<=12;i++)
            {
                var root=new GameObject("Worker "+i);root.transform.SetParent(transform);root.transform.position=Home(i);
                var body=Shape("Body",PrimitiveType.Capsule,Home(i)+Vector3.up*.65f,new Vector3(.42f,.5f,.42f),i<=4?new Color(.22f,.5f,.7f):i<=8?new Color(.35f,.6f,.44f):new Color(.57f,.42f,.65f),root.transform,true);
                Shape("Head",PrimitiveType.Sphere,Home(i)+Vector3.up*1.2f,Vector3.one*.32f,new Color(.88f,.72f,.5f),root.transform);
                var ring=Shape("Selection",PrimitiveType.Cylinder,Home(i)+Vector3.up*.015f,new Vector3(.85f,.012f,.85f),new Color(.2f,.7f,.95f),root.transform);
                var cargo=Shape("Cargo",PrimitiveType.Cube,Home(i)+new Vector3(0,.8f,-.3f),Vector3.one*.4f,new Color(.73f,.48f,.2f),root.transform);
                var worker=root.AddComponent<GatheringWorker>();worker.Initialize(i,this,ring,cargo);Workers.Add(worker);
                if(i==1)FarmerAppearance.Attach(this,worker,cargo);
            }
            RefreshHeathClearances();routesReadyAt=Time.unscaledTime+.5f;Ready=true;
        }
        private GatheringResource CreateResource(string name,ResourceKind kind,WorkKind work,int stock,Vector3 p,float radius)
        {
            var root=new GameObject(name);root.transform.SetParent(transform);root.transform.position=p;
            Color color=work==WorkKind.Chop?new Color(.38f,.36f,.35f):work==WorkKind.Mine?new Color(.37f,.43f,.48f):work==WorkKind.Gather?new Color(.51f,.43f,.47f):new Color(.53f,.48f,.43f);
            var visual=Shape("Visual",work==WorkKind.Pickup?PrimitiveType.Cube:PrimitiveType.Sphere,p+Vector3.up*(work==WorkKind.Chop?1.4f:.5f),new Vector3(radius*2,work==WorkKind.Chop?2.6f:1,radius*2),color,root.transform,true);
            if(work==WorkKind.Chop)AttachDeadwood(visual,p);
            if(work==WorkKind.Mine&&kind==ResourceKind.Ore)wasteland?.AttachOre(visual,radius);
            var resource=root.AddComponent<GatheringResource>();resource.Initialize(name,kind,work,stock,radius,visual.transform);resource.SetStableId(Resources.Count==0?1:Resources.Max(r=>r.Id)+1);Resources.Add(resource);return resource;
        }
        private void OnDestroy(){if(roadNavigation!=null)StopCoroutine(roadNavigation);wasteland?.Dispose();foreach(var m in materials)if(m)Destroy(m);if(groundTexture)Destroy(groundTexture);if(restoredSettings)Destroy(restoredSettings);}
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GatheringDemo
{
    [RequireComponent(typeof(GatheringWorld))]
    public sealed partial class GatheringController : MonoBehaviour
    {
        private GatheringWorld world;
        private GatheringSaveService saves;
        private void Start(){saves=GetComponent<GatheringSaveService>();}
        internal void PersistenceFeedback(string text){feedback=text;}
        internal void ResetAfterLoad()
        {Select(Array.Empty<GatheringWorker>());selectedSite=null;focusedResource=null;showDepot=showConstructionQueue=placing=dragging=panning=mapDragMode=strokeDragging=false;ResetRightGesture();recentStroke.Clear();rejectedWorkers.Clear();workerLabels.Clear();orderMarker.positionCount=hoverMarker.positionCount=entranceMarker.positionCount=0;}
        private InputAction point,primary,secondary,shift,stop,escape,zoom,pan,rotate;
        private Vector2 pressed,queueScroll,panLast;
        private Vector2 rightPressed,rightPanLast;
        private bool rightPending,rightDragging,rightShift;
        private bool panning,suppressPrimary,hasPreview,mapDragMode;
        private readonly List<Texture2D> uiTextures=new List<Texture2D>();
        private bool dragging,oneTrip,pressedShift,appendMode,showMetrics,workerDetails;
        private readonly Dictionary<GatheringWorker,Rect> workerLabels=new Dictionary<GatheringWorker,Rect>();
        private Rect header,panel,metricsPanel;
        private Rect buildPanel,guidePanel,depotPanel;
        private bool showConstruction,placing,showConstructionQueue,showDepot;
        private bool DepotVisible=>showDepot&&!showConstruction&&!showMetrics;
        private int buildRotation;
        private BuildingKind buildKind;
        private bool infrastructureTab,strokeDragging;
        private Vector3 strokeStart;
        private readonly List<ConstructionSite> recentStroke=new List<ConstructionSite>();
        private ConstructionSite selectedSite;
        private Vector2 buildScroll;
        private Vector3 previewPoint;
        private string placementHint="";
        private Vector2 metricsScroll;
        private static readonly GUIContent CampTitle=new GUIContent("荒野木寨","营地总览");
        private GUIStyle labelStyle,titleStyle,campTitleStyle,smallStyle,worldStyle,sectionStyle,resourceStyle,activeButton,dangerButton,selectionStyle;
#if UNITY_EDITOR
        private Font localTitleFont;
#endif
        private GUISkin demoSkin;
        private float uiScale=1,markerUntil;
        private LineRenderer orderMarker,hoverMarker,entranceMarker;
        private string hoverHint="",feedback="选中工人，再点击地面、资源或仓库";
        private readonly List<GatheringWorker> rejectedWorkers=new List<GatheringWorker>();
        public string Feedback=>feedback;
        public int LastAccepted { get; private set; }
        public int LastRejected { get; private set; }
        public readonly List<GatheringWorker> Selected=new List<GatheringWorker>();
        private void Awake()
        {
            world=GetComponent<GatheringWorld>();
            point=new InputAction("Point",InputActionType.Value,"<Pointer>/position");
            primary=new InputAction("SelectAndOrder",InputActionType.Button,"<Pointer>/press");
            secondary=new InputAction("SecondaryOrder",InputActionType.Button,"<Mouse>/rightButton");
            shift=new InputAction("Append",InputActionType.Button,"<Keyboard>/leftShift");
            shift.AddBinding("<Keyboard>/rightShift");
            stop=new InputAction("Stop",InputActionType.Button,"<Keyboard>/s");
            escape=new InputAction("Deselect",InputActionType.Button,"<Keyboard>/escape");
            zoom=new InputAction("Zoom",InputActionType.Value,"<Mouse>/scroll/y");
            pan=new InputAction("Pan",InputActionType.Button,"<Mouse>/middleButton");
            rotate=new InputAction("RotateBlueprint",InputActionType.Button,"<Keyboard>/r");
            entranceMarker=MakeMarker("Blueprint entrances");entranceMarker.loop=false;
            orderMarker=MakeMarker("Order feedback");hoverMarker=MakeMarker("Target preview");
        }
        private LineRenderer MakeMarker(string title)
        {
            var go=new GameObject(title);go.transform.SetParent(transform);
            var line=go.AddComponent<LineRenderer>();line.material=new Material(Shader.Find("Sprites/Default"));
            line.useWorldSpace=true;line.loop=true;line.startWidth=line.endWidth=.055f;return line;
        }
        private void Circle(LineRenderer line,Vector3 center,float radius,Color color)
        {
            line.positionCount=40;line.startColor=line.endColor=color;
            for(int i=0;i<40;i++){float a=i*Mathf.PI*2/40;line.SetPosition(i,center+new Vector3(Mathf.Sin(a)*radius,.07f,Mathf.Cos(a)*radius));}
        }
        private void OnEnable(){foreach(var a in Actions())a.Enable();}
        private void OnDisable(){ResetRightGesture();dragging=panning=strokeDragging=false;foreach(var a in Actions())a.Disable();}
        private IEnumerable<InputAction> Actions(){yield return point;yield return primary;yield return secondary;yield return shift;yield return stop;yield return escape;yield return zoom;yield return pan;yield return rotate;}
        private void OnDestroy()
        {
#if UNITY_EDITOR
            if(localTitleFont)Destroy(localTitleFont);
#endif
            foreach(var a in Actions())a.Dispose();
            if(entranceMarker)Destroy(entranceMarker.sharedMaterial);
            if(orderMarker)Destroy(orderMarker.sharedMaterial);if(hoverMarker)Destroy(hoverMarker.sharedMaterial);if(demoSkin)Destroy(demoSkin);foreach(var texture in uiTextures)Destroy(texture);
        }
        private void Update()
        {
            if(!world.Ready){ResetRightGesture();return;}
            Layout();
            UpdateGuide();
            var pos=point.ReadValue<Vector2>();
            UpdateCamera(pos);
            if(stop.WasPressedThisFrame()&&!placing)Apply("立即停止",w=>w.Stop());
            if(escape.WasPressedThisFrame()){ResetRightGesture();if(placing)CancelPlacement();else if(mapDragMode)mapDragMode=false;else Select(Array.Empty<GatheringWorker>());dragging=false;}
            if(!primary.IsPressed())suppressPrimary=false;
            UpdateHover(pos);entranceMarker.positionCount=0;hasPreview=false;
            if(placing)
            {
                if(buildKind!=BuildingKind.Stockyard){UpdateInfrastructurePlacement(pos);return;}
                hoverMarker.positionCount=0;
                if(secondary.WasPressedThisFrame()){CancelPlacement();return;}
                if(rotate.WasPressedThisFrame())buildRotation=(buildRotation+1)%4;
                var plane=new Plane(Vector3.up,Vector3.zero);var ray=world.View.ScreenPointToRay(pos);
                if(!OverUI(pos)&&!panning&&plane.Raycast(ray,out float distance))
                {
                    hasPreview=true;var hit=ray.GetPoint(distance);
                    previewPoint=PlacementGrid.Center(hit,PlacementGrid.BuildingStep);
                    placementHint=world.Paused?"游戏已暂停":world.RoutesSettling?"正在更新道路，请稍候":ConstructionSite.PlacementReason(world,previewPoint,buildRotation);
                    var size=ConstructionSite.Size(buildRotation);hoverMarker.positionCount=4;hoverMarker.startColor=hoverMarker.endColor=placementHint==""?Color.cyan:new Color(1,.3f,.2f);
                    for(int i=0;i<4;i++)hoverMarker.SetPosition(i,previewPoint+new Vector3((i==0||i==3?-1:1)*size.x*.5f,.1f,(i<2?-1:1)*size.z*.5f));
                    var left=ConstructionSite.Point(previewPoint,buildRotation,0)+Vector3.up*.12f;
                    var right=ConstructionSite.Point(previewPoint,buildRotation,1)+Vector3.up*.12f;
                    var front=Quaternion.Euler(0,buildRotation*90,0)*Vector3.forward;
                    entranceMarker.startColor=entranceMarker.endColor=Color.yellow;entranceMarker.positionCount=7;
                    entranceMarker.SetPositions(new[]{left,right,(left+right)*.5f,(left+right)*.5f+front*.7f,(left+right)*.5f+front*.4f+Quaternion.Euler(0,buildRotation*90,0)*Vector3.right*.2f,(left+right)*.5f+front*.7f,(left+right)*.5f+front*.4f-Quaternion.Euler(0,buildRotation*90,0)*Vector3.right*.2f});
                    hoverHint=placementHint==""?"可放置 · 黄色为入口 · R旋转 · 右键取消":placementHint;
                    if(primary.WasPressedThisFrame()&&!suppressPrimary)
                    {var result=world.PlaceBuilding(previewPoint,buildRotation,out var site);feedback=result.Reason;if(result.Accepted){InspectSite(site);feedback=world.RequestPlacementCrew(new[]{site});CancelPlacement(false);}dragging=false;}
                }
                return;
            }
            if(mapDragMode){if(secondary.WasPressedThisFrame())mapDragMode=false;return;}
            if(rightPending)
            {
                dragging=false;
                if(rightDragging){hoverMarker.positionCount=0;hoverHint="正在拖动地图 · 松开右键结束 · 人员任务继续";}
                if(secondary.WasReleasedThisFrame())
                {
                    bool issueOrder=!rightDragging&&!OverUI(pos);bool append=rightShift;
                    ResetRightGesture();
                    if(issueOrder)Click(pos,true,append);
                }
                if(Time.unscaledTime>markerUntil)orderMarker.positionCount=0;
                return;
            }
            if(primary.WasPressedThisFrame()&&!suppressPrimary&&!panning&&!OverUI(pos)){pressed=pos;pressedShift=shift.IsPressed();dragging=true;}
            if(primary.WasReleasedThisFrame()&&dragging)
            {
                dragging=false;if(OverUI(pos)||panning)return;
                if(Vector2.Distance(pressed,pos)>8*uiScale)
                {
                    var rect=Rect.MinMaxRect(Mathf.Min(pressed.x,pos.x),Mathf.Min(pressed.y,pos.y),Mathf.Max(pressed.x,pos.x),Mathf.Max(pressed.y,pos.y));
                    Select(world.Workers.Where(w=>{var p=world.View.WorldToScreenPoint(w.transform.position+Vector3.up*.6f);return p.z>0&&rect.Contains(p);}),pressedShift);
                }
                else Click(pos,false,pressedShift);
            }
            if(Time.unscaledTime>markerUntil)orderMarker.positionCount=0;
        }
        private void UpdateCamera(Vector2 pos)
        {
            void Position()
            {
                float height=world.View.pixelHeight/uiScale;
                world.PositionMapCamera((header.yMax-116)/height*world.View.orthographicSize);
            }
            Position();var plane=new Plane(Vector3.up,Vector3.zero);
            Vector3 Ground(Vector2 p){var ray=world.View.ScreenPointToRay(p);return plane.Raycast(ray,out float d)?ray.GetPoint(d):world.CameraFocus;}
            if((pan.WasPressedThisFrame()||mapDragMode&&primary.WasPressedThisFrame())&&!OverUI(pos)){panning=true;panLast=pos;dragging=false;}
            if(panning&&(pan.IsPressed()||mapDragMode&&primary.IsPressed())){world.SetCameraFocus(world.CameraFocus+Ground(panLast)-Ground(pos));panLast=pos;Position();}
            if(!pan.IsPressed()&&!(mapDragMode&&primary.IsPressed()))panning=false;
            if(placing||mapDragMode||primary.IsPressed()||pan.IsPressed()||escape.WasPressedThisFrame())ResetRightGesture();
            else
            {
                if(secondary.WasPressedThisFrame()&&!OverUI(pos))
                {rightPending=true;rightDragging=false;rightPressed=rightPanLast=pos;rightShift=shift.IsPressed();dragging=false;}
                if(rightPending)
                {
                    // Once a drag starts it remains a drag, even if the pointer returns to its starting point.
                    // Include the release position so a fast drag cannot turn into a movement order.
                    if(Vector2.Distance(rightPressed,pos)>8*uiScale)rightDragging=true;
                    if(rightDragging)
                    {world.SetCameraFocus(world.CameraFocus+Ground(rightPanLast)-Ground(pos));Position();}
                    rightPanLast=pos;
                }
            }
            float scroll=zoom.ReadValue<float>();
            if(Mathf.Abs(scroll)>.01f&&!OverUI(pos))
            {var before=Ground(pos);world.View.orthographicSize=Mathf.Clamp(world.View.orthographicSize-scroll*.008f,5,20);Position();world.SetCameraFocus(world.CameraFocus+before-Ground(pos));Position();}
        }
        private void ResetRightGesture(){rightPending=rightDragging=false;}
        private void OnApplicationFocus(bool focused){if(!focused){dragging=panning=strokeDragging=false;ResetRightGesture();suppressPrimary=true;}}
        private void CancelPlacement(bool notify=true)
        {placing=false;strokeDragging=false;hasPreview=false;ResetRightGesture();suppressPrimary=true;hoverMarker.positionCount=entranceMarker.positionCount=0;if(notify)feedback="已退出蓝图放置";}
        private void Layout()
        {
            uiScale=Mathf.Clamp(Mathf.Floor(Mathf.Min(world.View.pixelHeight/800f,world.View.pixelWidth/1250f)),1,3);
            float width=world.View.pixelWidth/uiScale,height=world.View.pixelHeight/uiScale;
            float dockHeight=Selected.Count==0?104:!workerDetails?142:Selected.Count==1?294:230;
            header=new Rect(12,12,width-24,width<1250?110:79);panel=new Rect(12,height-12-dockHeight,width-24,dockHeight);
            metricsPanel=new Rect(Mathf.Max(12,width-422),header.yMax+8,410,Mathf.Max(80,panel.y-header.yMax-16));
            buildPanel=new Rect(12,header.yMax+8,335,Mathf.Min(selectedSite||showConstructionQueue?525:infrastructureTab?450:370,Mathf.Max(80,panel.y-header.yMax-16)));
            depotPanel=new Rect(12,header.yMax+8,335,140);
            guidePanel=new Rect(width-322,header.yMax+8,310,Mathf.Min(242,Mathf.Max(100,panel.y-header.yMax-16)));
        }
        private bool OverUI(Vector2 screen)
        {Vector2 gui=new Vector2(screen.x/uiScale,(world.View.pixelHeight-screen.y)/uiScale);return header.Contains(gui)||panel.Contains(gui)||showMetrics&&metricsPanel.Contains(gui)||showConstruction&&buildPanel.Contains(gui)||DepotVisible&&depotPanel.Contains(gui)||GuideVisible&&guidePanel.Contains(gui);}
        private bool AppendHeld=>appendMode||shift.IsPressed();
        public void Select(IEnumerable<GatheringWorker> workers,bool append=false)
        {
            if(!append){foreach(var w in Selected)w.Selected=false;Selected.Clear();}
            foreach(var w in workers)if(!Selected.Contains(w)){Selected.Add(w);w.Selected=true;}
            feedback=Selected.Count==0?"未选中工人：左键点工人或拖框多选；原任务继续":$"已选 {Selected.Count} 人 · 左键点空白取消选择，点工人只选他；右键地面移动";
            queueScroll=Vector2.zero;
        }
        private void UpdateHover(Vector2 pos)
        {
            hoverMarker.positionCount=0;hoverHint="";
            if(OverUI(pos))return;
            var labelWorker=WorkerAtLabel(pos);
            if(labelWorker){hoverHint=$"{labelWorker.Id} 号 · 左键选择 · {labelWorker.Status}";return;}
            if(!Physics.Raycast(world.View.ScreenPointToRay(pos),out var hit,200))return;
            var worker=hit.collider.GetComponentInParent<GatheringWorker>();
            if(worker){hoverHint=$"{worker.Id} 号 · 点击选择 · {worker.Status}";return;}
            var building=hit.collider.GetComponentInParent<ConstructionSite>();
            if(building){hoverHint=$"{building.Title} · {building.Status} · "+(building.Complete?(building.IsStockyard?"左键查看；携货者右键送库":"左键查看") :"左键查看；选人后右键派工");return;}
            var resource=hit.collider.GetComponentInParent<GatheringResource>();
            if(resource)
            {
                hoverHint=$"{resource.DisplayName} · 剩余 {resource.Stock} 件 · {world.Settings.Weight(resource.Kind):0.0} kg/件 · "+(Selected.Count==0?"先选工人":resource.Stock==0?"已采空":oneTrip?"点击采一趟":"点击持续采集");
                Circle(hoverMarker,resource.transform.position,resource.Radius+.2f,resource.Stock>0?new Color(.2f,.8f,.9f):Color.gray);return;
            }
            if(hit.collider.GetComponent<GatheringDepot>()){hoverHint="仓库 · 携货者点击送货；空手者保留原任务";Circle(hoverMarker,hit.transform.position,1.8f,Color.cyan);return;}
            if(hit.collider.gameObject.layer==8)hoverHint=Selected.Count==0?"右键按住拖动地图；左键点工人或拖框多选":"右键拖动地图／单击移动 · 左键空白取消选择 · Shift 追加任务";
            else hoverHint="障碍区域 · 无法下令到此处";
        }
        public void Click(Vector2 screen,bool right,bool? appendOverride=null)
        {
            // The queue toggle affects orders only; selection uses the Shift state captured on press.
            bool selectionAppend=appendOverride??shift.IsPressed();bool append=appendMode||selectionAppend;
            var labelWorker=WorkerAtLabel(screen);
            if(labelWorker){if(!right)Select(new[]{labelWorker},selectionAppend);else feedback="右键地面移动、资源采集或工地派工；人员原任务继续";return;}
            if(!Physics.Raycast(world.View.ScreenPointToRay(screen),out var hit,200))
            {if(!right)ClearSelection();else feedback="点击场地内的可行走地面或资源";return;}
            var worker=hit.collider.GetComponentInParent<GatheringWorker>();
            if(worker&&!right){Select(new[]{worker},selectionAppend);return;}
            var building=hit.collider.GetComponentInParent<ConstructionSite>();
            if(building)
            {
                if(right&&building.Complete&&!building.IsStockyard)
                {if(building.IsRoad&&CanOrder())OrderMove(hit.point,append);else feedback="围墙阻挡通行，请选择可走地面";return;}
                InspectSite(building);
                if(right&&Selected.Count>0){if(building.Complete)Apply("送回仓库",w=>w.Deliver(append));else Apply("安排建造",w=>w.Build(building,append));}
                else feedback=$"已查看{building.Title} · {building.Status}";return;
            }
            if(!right&&hit.collider.gameObject.layer==8){ClearSelection();return;}
            if(!CanOrder())return;
            var resource=hit.collider.GetComponentInParent<GatheringResource>();
            if(resource){OrderResource(resource,append,oneTrip);return;}
            if(hit.collider.GetComponent<GatheringDepot>()){Apply("送回仓库",w=>w.Deliver(append));Mark(hit.point);return;}
            if(hit.collider.gameObject.layer!=8){feedback="目标被障碍占用，所有人的原任务保持";LastAccepted=0;LastRejected=Selected.Count;Mark(hit.point);return;}
            OrderMove(hit.point,append);
        }
        private void ClearSelection(){Select(Array.Empty<GatheringWorker>());orderMarker.positionCount=0;}
        private GatheringWorker WorkerAtLabel(Vector2 screen)
        {
            var gui=new Vector2(screen.x/uiScale,(world.View.pixelHeight-screen.y)/uiScale);
            foreach(var entry in workerLabels)if(entry.Key&&entry.Value.Contains(gui))return entry.Key;
            return null;
        }
        private bool CanOrder()
        {
            if(Selected.Count==0){feedback="没有选中工人，请先点击工人或拖框选择";return false;}
            if(world.Paused){feedback="当前已暂停，点击右上角继续后下令";return false;}
            if(world.RoutesSettling){feedback="正在更新道路，请稍后下令";return false;}
            return true;
        }
        public void Apply(string title,Func<GatheringWorker,OrderResult> action)
        {
            LastAccepted=LastRejected=0;rejectedWorkers.Clear();
            if(!CanOrder())return;
            var reasons=new List<string>();var notes=new HashSet<string>();
            foreach(var worker in Selected)
            {
                var result=action(worker);
                if(result.Accepted){LastAccepted++;if(result.Reason!="已接受")notes.Add(result.Reason);}
                else {LastRejected++;rejectedWorkers.Add(worker);reasons.Add($"{worker.Id}号：{result.Reason}");}
            }
            feedback=$"{title}：{LastAccepted}/{Selected.Count} 人接受"+(LastRejected>0?$"，{LastRejected} 人未执行 · {string.Join("；",reasons.Take(3))}"+(LastRejected>3?$"；另 {LastRejected-3} 人，点击“选未执行”查看":""):notes.Count>0?$" · {string.Join("；",notes)}":"");
        }
        public void OrderResource(GatheringResource resource,bool append=false,bool once=false)
        {Apply($"{(append?"追加":"安排")} {resource.DisplayName} · {(once?"一趟":"持续")}",w=>w.Work(resource,append,once));Mark(resource.transform.position);}
        public void OrderMove(Vector3 target,bool append=false)
        {
            int i=0,cols=Mathf.Max(1,Mathf.CeilToInt(Mathf.Sqrt(Selected.Count)));
            Apply(append?"追加移动":"移动",w=>
            {
                int index=i++;var p=target+new Vector3((index%cols-(cols-1)*.5f)*.95f,0,(index/cols)*.95f);
                return UnityEngine.AI.NavMesh.SamplePosition(p,out var sample,.7f,UnityEngine.AI.NavMesh.AllAreas)?w.Move(sample.position,append):OrderResult.No("没有可行走落点，原任务保留");
            });Mark(target);
        }
        private void Mark(Vector3 pos)
        {if(Selected.Count==0)return;Circle(orderMarker,pos,.55f,LastAccepted==0?new Color(1,.3f,.25f):LastRejected>0?new Color(1,.75f,.2f):new Color(.2f,.9f,.55f));markerUntil=Time.unscaledTime+1.6f;}
        private static void PrepareHudFont(GUIStyle style)
        {
            style.CalcSize(new GUIContent(" "));
            var atlas=Resources.FindObjectsOfTypeAll<UnityEngine.TextCore.Text.FontAsset>().FirstOrDefault(asset=>asset.sourceFontFile==style.font);
            if(!atlas||atlas.atlasWidth>=4096)return;
            var width=atlas.GetType().GetProperty("atlasWidth")?.GetSetMethod(true);
            var height=atlas.GetType().GetProperty("atlasHeight")?.GetSetMethod(true);
            if(width==null||height==null)return;
            width.Invoke(atlas,new object[]{4096});height.Invoke(atlas,new object[]{4096});
            atlas.ClearFontAssetData(true);
        }
        private Texture2D HudFrame(Color fill,Color border,Color topEdge)
        {
            if(QualitySettings.activeColorSpace==ColorSpace.Linear)
            {fill=fill.linear;border=border.linear;topEdge=topEdge.linear;}
            var texture=new Texture2D(5,5,TextureFormat.RGBA32,false){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            for(int row=0;row<5;row++)for(int column=0;column<5;column++)
                texture.SetPixel(column,row,row==4?topEdge:row==0||column==0||column==4?border:fill);
            texture.Apply(false,true);uiTextures.Add(texture);return texture;
        }
        private void SetHudSurface(GUIStyleState state,Color fill,Color border,Color topEdge)
        {
            state.background=HudFrame(fill,border,topEdge);state.scaledBackgrounds=null;
            state.textColor=new Color32(243,232,211,255);
        }
        private void OnGUI()
        {
            if(!world||!world.View)return;Layout();
            var previousSkin=GUI.skin;var previousMatrix=GUI.matrix;var previousEnabled=GUI.enabled;
            if(!demoSkin)
            {
                demoSkin=Instantiate(previousSkin);var font=Resources.Load<Font>("CampSong");if(font){demoSkin.font=font;demoSkin.label.font=demoSkin.button.font=demoSkin.box.font=font;}
                var bronze=new Color32(127,97,63,255);var bronzeLight=new Color32(168,131,85,255);
                var neutralEdge=new Color32(90,78,64,255);var neutralTop=new Color32(112,97,77,255);
                demoSkin.box.border=new RectOffset(1,1,1,1);
                SetHudSurface(demoSkin.box.normal,new Color32(35,32,29,255),bronze,bronzeLight);
                SetHudSurface(demoSkin.button.normal,new Color32(60,53,46,255),neutralEdge,neutralTop);
                SetHudSurface(demoSkin.button.hover,new Color32(82,67,52,255),bronze,bronzeLight);
                SetHudSurface(demoSkin.button.active,new Color32(68,53,38,255),bronze,bronzeLight);
                demoSkin.button.focused.background=demoSkin.button.hover.background;demoSkin.button.focused.scaledBackgrounds=null;
                demoSkin.button.focused.textColor=demoSkin.button.normal.textColor;
                demoSkin.label.normal.textColor=new Color32(243,232,211,255);
                demoSkin.button.fontSize=14;demoSkin.button.fontStyle=FontStyle.Normal;demoSkin.button.fixedHeight=29;demoSkin.button.border=new RectOffset(1,1,1,1);
                activeButton=new GUIStyle(demoSkin.button);SetHudSurface(activeButton.normal,new Color32(100,72,44,255),bronzeLight,bronzeLight);
                dangerButton=new GUIStyle(demoSkin.button);SetHudSurface(dangerButton.normal,new Color32(91,50,43,255),new Color32(144,88,71,255),new Color32(173,108,87,255));
                selectionStyle=new GUIStyle(demoSkin.box);SetHudSurface(selectionStyle.normal,new Color32(217,183,115,34),bronzeLight,bronzeLight);
                foreach(var track in new[]{demoSkin.verticalScrollbar,demoSkin.horizontalScrollbar})
                {
                    track.border=new RectOffset(1,1,1,1);
                    SetHudSurface(track.normal,new Color32(28,25,23,255),neutralEdge,neutralEdge);
                }
                foreach(var thumb in new[]{demoSkin.verticalScrollbarThumb,demoSkin.horizontalScrollbarThumb})
                {
                    thumb.border=new RectOffset(1,1,1,1);
                    SetHudSurface(thumb.normal,new Color32(112,85,59,255),bronze,bronzeLight);
                    SetHudSurface(thumb.hover,new Color32(135,102,68,255),bronzeLight,bronzeLight);
                    thumb.active.background=thumb.hover.background;thumb.active.scaledBackgrounds=null;
                }
                labelStyle=new GUIStyle(demoSkin.label){fontSize=15,fontStyle=FontStyle.Normal,wordWrap=true};
                smallStyle=new GUIStyle(labelStyle){fontSize=13};smallStyle.normal.textColor=new Color32(219,208,189,255);
                titleStyle=new GUIStyle(labelStyle){fontSize=19,alignment=TextAnchor.MiddleLeft};
                campTitleStyle=new GUIStyle(titleStyle){fontSize=28,fixedHeight=32,padding=new RectOffset(),wordWrap=false};
                campTitleStyle.normal.textColor=new Color(.97f,.89f,.73f);
#if UNITY_EDITOR
                const string hanshan="zihun601hao-hanshansong";
                if(Font.GetOSInstalledFontNames().Contains(hanshan,StringComparer.OrdinalIgnoreCase))
                {
                    localTitleFont=Font.CreateDynamicFontFromOSFont(hanshan,campTitleStyle.fontSize);
                    if(localTitleFont&&CampTitle.text.All(localTitleFont.HasCharacter))campTitleStyle.font=localTitleFont;
                }
#endif
                sectionStyle=new GUIStyle(labelStyle){fontSize=16,alignment=TextAnchor.MiddleLeft};sectionStyle.normal.textColor=new Color32(234,195,139,255);
                resourceStyle=new GUIStyle(sectionStyle){fixedHeight=32,padding=new RectOffset(),wordWrap=false};
                worldStyle=new GUIStyle(demoSkin.box){fontSize=14,fontStyle=FontStyle.Normal,alignment=TextAnchor.MiddleCenter,wordWrap=true,padding=new RectOffset(2,2,0,0)};
                SetHudSurface(worldStyle.normal,new Color32(35,32,29,237),neutralEdge,neutralEdge);
                if(font)PrepareHudFont(labelStyle);
            }
            GUI.skin=demoSkin;GUI.matrix=Matrix4x4.Scale(Vector3.one*uiScale);
            try{DrawInterface();}finally{GUI.skin=previousSkin;GUI.matrix=previousMatrix;GUI.enabled=previousEnabled;}
        }
        private void DrawInterface()
        {
            if(!world.Ready){GUI.Box(header,GUIContent.none);GUI.Label(new Rect(header.x+12,header.y+6,header.width-24,32),"正在恢复世界，请稍候…",titleStyle);return;}
            GUI.Box(header,GUIContent.none);GUILayout.BeginArea(new Rect(header.x+12,header.y+6,header.width-24,header.height-12));
            GUILayout.BeginHorizontal();GUILayout.Label(CampTitle,campTitleStyle);GUILayout.Space(12);GUILayout.Label($"木材  {world.Inventory[0]}     矿石  {world.Inventory[1]}     果实  {world.Inventory[2]}",resourceStyle);GUILayout.FlexibleSpace();
            if(header.height>80){GUILayout.EndHorizontal();GUILayout.BeginHorizontal();}
            if(GUILayout.Button(showConstruction?"收起建造":"建造",showConstruction?activeButton:demoSkin.button,GUILayout.Width(80))){showConstruction=!showConstruction;CancelPlacement(false);if(showConstruction){showConstructionQueue=showMetrics=false;selectedSite=null;buildScroll=Vector2.zero;}}
            if(GUILayout.Button(mapDragMode?"结束拖图":"拖动地图",GUILayout.Width(85))){mapDragMode=!mapDragMode;placing=false;dragging=panning=false;ResetRightGesture();feedback=mapDragMode?"拖图模式：左键拖动地图；右键或“结束拖图”退出":"已退出拖图模式";}
            if(GUILayout.Button(world.GuideHidden?"显示引导":"收起引导",GUILayout.Width(85)))world.GuideHidden=!world.GuideHidden;
            if(GUILayout.Button("回到基地",GUILayout.Width(85)))world.SetCameraFocus(Vector3.zero);
            if(GUILayout.Button(showMetrics?"收起效率":"采集效率",GUILayout.Width(90))){showMetrics=!showMetrics;if(showMetrics){showConstruction=false;CancelPlacement(false);}}
            if(GUILayout.Button(DepotVisible?"收起仓库":"仓库",DepotVisible?activeButton:demoSkin.button,GUILayout.Width(70)))
            {showDepot=!DepotVisible;if(showDepot){showConstruction=showMetrics=false;CancelPlacement(false);}}
            GUI.enabled=saves&&!saves.Loading&&world.Ready;
            if(GUILayout.Button("保存",GUILayout.Width(60)))feedback=saves.Save().Reason;
            GUI.enabled=saves&&!saves.Loading&&saves.CanLoad;
            if(GUILayout.Button("继续存档",GUILayout.Width(85)))feedback=saves.Load().Reason;
            GUI.enabled=true;
            if(GUILayout.Button(world.Paused?"继续":"暂停",world.Paused?activeButton:demoSkin.button,GUILayout.Width(70)))world.Paused=!world.Paused;GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();GUILayout.Label(world.Paused?"已暂停":"指令反馈",sectionStyle,GUILayout.Width(65));GUILayout.Label(feedback,labelStyle);GUILayout.EndHorizontal();GUILayout.EndArea();
            DrawWorldLabels();
            if(placing&&hasPreview){var p=point.ReadValue<Vector2>();float x=Mathf.Clamp(p.x/uiScale+18,12,world.View.pixelWidth/uiScale-290),y=Mathf.Clamp((world.View.pixelHeight-p.y)/uiScale+22,header.yMax+4,panel.y-64);GUI.Box(new Rect(x,y,278,56),(placementHint==""?(buildKind==BuildingKind.Stockyard?"建筑大格 · R 旋转入口":buildKind==BuildingKind.Road?"道路小格 · 黄框可接堆场入口":"围墙小格 · 请留出入口"): "不可放置："+placementHint)+(buildKind==BuildingKind.Stockyard?"\n左键放置 · 右键取消":"\n拖出直线，松开确认 · 右键取消"),worldStyle);}
            if(showMetrics)DrawMetrics();
            if(showConstruction)DrawConstruction();
            if(DepotVisible)DrawDepot();
            if(GuideVisible)DrawGuide();
            GUI.Box(panel,GUIContent.none);GUILayout.BeginArea(new Rect(panel.x+12,panel.y+6,panel.width-24,panel.height-12));
            GUILayout.BeginHorizontal();GUILayout.Label("工人",titleStyle,GUILayout.Width(54));GUILayout.Label(Selected.Count==0?"尚未选择":$"已选 {Selected.Count} / {world.Workers.Count} 人",sectionStyle);
            GUILayout.FlexibleSpace();if(GUILayout.Button("全选",GUILayout.Width(62)))Select(world.Workers);if(GUILayout.Button("选空闲",GUILayout.Width(76)))Select(world.Workers.Where(w=>w.Phase==WorkerPhase.Idle));if(GUILayout.Button("选受阻",GUILayout.Width(76)))Select(world.Workers.Where(w=>w.Phase==WorkerPhase.Blocked));
            if(rejectedWorkers.Count>0&&GUILayout.Button("选未执行"))Select(rejectedWorkers);
            if(Selected.Count>0&&GUILayout.Button(workerDetails?"收起任务详情":Selected.Count==1?$"任务详情 · 队列 {Selected[0].QueueCount}":"任务详情",workerDetails?activeButton:demoSkin.button,GUILayout.Width(146)))workerDetails=!workerDetails;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();foreach(var w in world.Workers)
            {
                var style=w.Selected?activeButton:w.Phase==WorkerPhase.Blocked?dangerButton:demoSkin.button;
                if(GUILayout.Button($"{w.Id:00}  {w.Status}",style,GUILayout.MinWidth(55)))Select(new[]{w},shift.IsPressed());
            }GUILayout.EndHorizontal();
            if(Selected.Count>0)DrawWorkerActions();
            if(Selected.Count==0||!workerDetails)GUILayout.Label(string.IsNullOrEmpty(hoverHint)?$"最近动态：{world.Notice}":hoverHint,smallStyle);
            GUILayout.EndArea();
            if(dragging&&Vector2.Distance(pressed,point.ReadValue<Vector2>())>8*uiScale)
            {var p=point.ReadValue<Vector2>();var r=Rect.MinMaxRect(Mathf.Min(pressed.x,p.x)/uiScale,(world.View.pixelHeight-Mathf.Max(pressed.y,p.y))/uiScale,Mathf.Max(pressed.x,p.x)/uiScale,(world.View.pixelHeight-Mathf.Min(pressed.y,p.y))/uiScale);GUI.Box(r,GUIContent.none,selectionStyle);}
        }
        private void DrawDepot()
        {
            GUI.Box(depotPanel,GUIContent.none);
            GUILayout.BeginArea(new Rect(depotPanel.x+12,depotPanel.y+8,depotPanel.width-24,depotPanel.height-16));
            GUILayout.Label("仓库 · 存放空间分配",titleStyle);
            int wood=world.DepotWoodPercent;
            GUILayout.Label($"木材 {wood}%（{world.Inventory[0]} 件）   石料 {100-wood}%（{world.Inventory[1]} 件）",labelStyle);
            int updated=Mathf.RoundToInt(GUILayout.HorizontalSlider(wood,GatheringDepot.MinWoodPercent,GatheringDepot.MaxWoodPercent));
            if(updated!=wood)world.SetDepotWoodPercent(updated);
            GUILayout.Label("只调整存放空间，不限制入库。",smallStyle);
            GUILayout.EndArea();
        }

        private void DrawWorkerActions()
        {
            GUILayout.BeginHorizontal();GUILayout.Label("派工",sectionStyle,GUILayout.Width(54));GUI.enabled=!world.Paused;
            foreach(WorkKind kind in Enum.GetValues(typeof(WorkKind)))
            {
                string title=kind==WorkKind.Chop?"伐木":kind==WorkKind.Mine?"采矿":kind==WorkKind.Gather?"采果":"搬货";
                if(GUILayout.Button(title))Apply(title,w=>{var r=world.FindResource(kind,w);return r?w.Work(r,AppendHeld,oneTrip):OrderResult.No("无可达的同类资源");});
            }
            if(GUILayout.Button("送回仓库"))Apply("送回仓库",w=>w.Deliver());if(GUILayout.Button("立即停止"))Apply("立即停止",w=>w.Stop());GUI.enabled=true;
            if(GUILayout.Button(appendMode?"追加：开":"追加：关",appendMode?activeButton:demoSkin.button,GUILayout.Width(92)))appendMode=!appendMode;
            if(GUILayout.Button(oneTrip?"采集：一趟":"采集：持续",GUILayout.Width(105)))oneTrip=!oneTrip;GUILayout.EndHorizontal();
            if(!workerDetails)return;
            GUILayout.BeginHorizontal();GUILayout.Label("任务",sectionStyle,GUILayout.Width(54));GUI.enabled=!world.Paused;
            if(GUILayout.Button("本轮后结束"))Apply("本轮后结束",w=>w.FinishTrip());if(GUILayout.Button("恢复采集"))Apply("恢复采集",w=>w.Resume());if(GUILayout.Button("重试路线"))Apply("重试路线",w=>w.Retry());
            bool allContinue=Selected.All(w=>w.ContinueSameKind);
            if(GUILayout.Button(allContinue?"同类续采：开":"同类续采：关/混合"))Apply("同类续采",w=>{w.ContinueSameKind=!allContinue;return OrderResult.Yes(allContinue?"已关闭":"已开启");});
            GUI.enabled=true;GUILayout.EndHorizontal();
            if(Selected.Count==1)
            {
                var w=Selected[0];GUILayout.Label($"{w.Id}号 · {w.Status} · 携货 {w.CargoCount}/{world.Settings.MaxItems} 件 · {w.CargoWeight:0.0}/{world.Settings.MaxWeight:0.0} kg · 速度上限 {w.Speed:0.00} m/s",labelStyle);
                GUILayout.Label(w.Detail,smallStyle,GUILayout.Height(22));DrawQueue(w);
            }
            else if(Selected.Count>1)
            {
                GUILayout.Label($"携货 {Selected.Sum(w=>w.CargoCount)} 件 · 受阻 {Selected.Count(w=>w.Phase==WorkerPhase.Blocked)} 人 · 等工作位 {Selected.Count(w=>w.Phase==WorkerPhase.WaitingWork)} 人 · 等卸货位 {Selected.Count(w=>w.Phase==WorkerPhase.WaitingDepot)} 人",labelStyle);
                GUILayout.Label("点击单个工人查看原因和后续队列；取消队列保留当前任务，本轮后结束会清空队列。",smallStyle);
            }
        }
        private void DrawMetrics()
        {
            GUI.Box(metricsPanel,GUIContent.none);
            GUILayout.BeginArea(new Rect(metricsPanel.x+10,metricsPanel.y+8,metricsPanel.width-20,metricsPanel.height-16));
            metricsScroll=GUILayout.BeginScrollView(metricsScroll);
            var m=world.Metrics;var report=m.Snapshot(world.Workers.Sum(w=>w.CargoCount));
            GUILayout.Label("采集效率 · 本次统计",titleStyle);
            GUILayout.Label($"有效时间 {m.Elapsed:0.0} 秒 · 暂停不计时",smallStyle);
            GUILayout.Label($"入库 {m.Items} 件 / {m.Weight:0.0} kg · {m.Trips} 趟",labelStyle);
            GUILayout.Label($"平均 {m.ItemsPerMinute:0.0} 件/分 · {m.KgPerMinute:0.0} kg/分",labelStyle);
            GUILayout.Label($"近 {m.RecentDuration:0} 秒：{m.RecentItemsPerMinute:0.0} 件/分"+(m.RecentWarming?"（窗口积累中）":""),smallStyle);
            GUILayout.Label($"途中携货 {report.carriedItems} 件，尚未计入收益",smallStyle);
            foreach(ResourceKind kind in Enum.GetValues(typeof(ResourceKind)))
            {
                var r=m.Resource(kind);GUILayout.Label($"{GatheringWorld.Name(kind)}：{r.items} 件 · {r.weight:0.0} kg · {(m.Elapsed>0?r.items*60/m.Elapsed:0):0.0} 件/分",smallStyle);
            }
            var all=m.Aggregate();GUILayout.Space(7);GUILayout.Label("全员耗时占比（累计工人秒）",labelStyle);
            TimeBar("行走",all.travel,all.Total,new Color(.35f,.65f,.95f));TimeBar("采集",all.gather,all.Total,new Color(.3f,.8f,.4f));
            TimeBar("等工作/建材",all.waitWork,all.Total,new Color(.9f,.7f,.3f));TimeBar("等卸货位",all.waitDepot,all.Total,new Color(.95f,.55f,.25f));
            TimeBar("装卸货",all.unload,all.Total,new Color(.6f,.55f,.9f));TimeBar("施工",all.construction,all.Total,new Color(.3f,.8f,.7f));TimeBar("受阻",all.blocked,all.Total,new Color(.95f,.3f,.3f));
            TimeBar("空闲",all.idle,all.Total,Color.gray);
            GUILayout.Label("12 人各工作 1 秒合计 12 工人秒。等候时走向排队位置仍计为等待，不重复计入行走。",smallStyle);
            if(Selected.Count>0)
            {
                var selected=m.Aggregate(Selected.Select(w=>w.Id));GUILayout.Space(6);GUILayout.Label($"所选 {Selected.Count} 人：入库 {selected.delivered} 件 / {selected.trips} 趟",labelStyle);
                GUILayout.Label($"行走 {selected.travel:0.0}s · 采集 {selected.gather:0.0}s · 排队 {selected.waitWork+selected.waitDepot:0.0}s · 受阻 {selected.blocked:0.0}s",smallStyle);
            }
            GUILayout.Space(7);GUILayout.BeginHorizontal();
            if(GUILayout.Button("重新统计")){world.ResetMetrics();feedback="统计已清零，库存和任务保留；途中货物入库时计入新窗口";}
            if(GUILayout.Button("导出统计"))
            {
                try
                {
                    string dir=System.IO.Path.Combine(Application.persistentDataPath,"GatheringReports");System.IO.Directory.CreateDirectory(dir);
                    string path=System.IO.Path.Combine(dir,"gathering-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".json");
                    System.IO.File.WriteAllText(path,JsonUtility.ToJson(report,true));feedback="统计已导出："+path;
                }
                catch(Exception e){feedback="统计导出失败："+e.Message;}
            }
            GUILayout.EndHorizontal();GUILayout.Label("重新统计仅改变观察窗口，不清库存；途中货物可能来自窗口开始之前。",smallStyle);
            GUILayout.Space(8);GUILayout.Label("路线测试",sectionStyle);GUI.enabled=!world.Paused;
            if(GUILayout.Button(world.Roadblock?"移除路障":"放置路障"))world.ToggleRoadblock();GUI.enabled=true;
            GUILayout.EndScrollView();GUILayout.EndArea();
        }
        private void TimeBar(string title,double seconds,double total,Color color)
        {
            float fraction=total>0?(float)(seconds/total):0;
            var rect=GUILayoutUtility.GetRect(0,22,GUILayout.ExpandWidth(true));
            var old=GUI.color;GUI.color=new Color(.055f,.041f,.032f);GUI.DrawTexture(rect,Texture2D.whiteTexture);
            GUI.color=color;GUI.DrawTexture(new Rect(rect.x,rect.y,rect.width*fraction,rect.height),Texture2D.whiteTexture);GUI.color=old;
            GUI.Label(rect,$" {title}  {seconds:0.0}s / {fraction:P0}",smallStyle);
        }
        private void DrawQueue(GatheringWorker w)
        {
            GUILayout.BeginHorizontal();GUILayout.Label($"后续任务 {w.QueueCount}/16 · 下次入库或移动结束后交接",smallStyle);GUILayout.FlexibleSpace();GUI.enabled=!world.Paused&&w.QueueCount>0;
            if(GUILayout.Button("清空后续",GUILayout.Width(95))){var result=w.ClearOrders();feedback=result.Reason;}
            GUI.enabled=true;GUILayout.EndHorizontal();
            queueScroll=GUILayout.BeginScrollView(queueScroll,GUILayout.Height(37));GUILayout.BeginHorizontal();
            int remove=-1;
            for(int i=0;i<w.Orders.Count;i++)
            {
                GUILayout.Label($"{i+1}. {w.Orders[i].Label}",smallStyle,GUILayout.MinWidth(140));GUI.enabled=!world.Paused;
                if(GUILayout.Button("取消",GUILayout.Width(48)))remove=i;GUI.enabled=true;
            }
            if(w.QueueCount==0)GUILayout.Label("暂无后续任务。按住 Shift 点击目标追加。",smallStyle);
            GUILayout.EndHorizontal();GUILayout.EndScrollView();if(remove>=0)feedback=w.RemoveOrder(remove).Reason;
        }
        private void DrawWorldLabels()
        {
            if(Event.current.type==EventType.Repaint)workerLabels.Clear();
            var used=new List<Rect>();DrawResourceFocus(used);
            foreach(var s in world.Resources.Where(s=>!s.IsRefund||s.Stock>0))WorldLabel(s.transform.position+Vector3.up*s.LabelHeight,$"{s.DisplayName} · {s.Stock} 件",used,150);
            WorldLabel(new Vector3(-6,1.5f,-2),"仓库 · 点击送货",used,150);
            foreach(var b in world.Buildings.Where(b=>!b.Cancelled&&(b.IsStockyard||!b.Complete&&b==selectedSite)))WorldLabel(b.transform.position+Vector3.up*1.6f,$"{b.Title} · {b.Status}",used,180);
            // A clickable head label must not cover another worker's model.
            foreach(var w in world.Workers)
            {
                var min=new Vector2(float.MaxValue,float.MaxValue);var max=new Vector2(float.MinValue,float.MinValue);
                for(int i=0;i<8;i++)
                {
                    var p=world.View.WorldToScreenPoint(w.transform.position+new Vector3((i&1)==0?-.3f:.3f,(i&2)==0?.1f:1.4f,(i&4)==0?-.3f:.3f));
                    var gui=new Vector2(p.x/uiScale,(world.View.pixelHeight-p.y)/uiScale);min=Vector2.Min(min,gui);max=Vector2.Max(max,gui);
                }
                used.Add(Rect.MinMaxRect(min.x,min.y,max.x,max.y));
            }
            foreach(var w in world.Workers)
            {
                bool detailed=w.Selected||w.Phase==WorkerPhase.Blocked||w.Phase==WorkerPhase.Idle&&w.CargoCount>0;
                WorldLabel(w.transform.position+Vector3.up*1.7f,detailed?$"{w.Id}号 · {w.Status}":$"{w.Id}",used,detailed?155:27,w);
            }
        }
        public void BeginPlacement(){buildKind=BuildingKind.Stockyard;infrastructureTab=false;showConstruction=true;showMetrics=false;placing=true;mapDragMode=false;dragging=false;ResetRightGesture();suppressPrimary=true;feedback="左键放置 · 黄色为入口 · R/按钮旋转 · 右键/Esc取消 · 中键拖动 · 滚轮缩放";}
        private void InspectSite(ConstructionSite site)
        {
            selectedSite=site;showConstructionQueue=false;infrastructureTab=!site.IsStockyard;showConstruction=true;showMetrics=false;buildScroll=Vector2.zero;
        }
        private void DrawConstructionPriority(ConstructionSite site,int total,int index)
        {
            GUILayout.Label($"建造顺序 · 第 {index+1}/{total} 位",sectionStyle);
            GUILayout.BeginHorizontal();
            for(int level=0;level<3;level++)if(GUILayout.Button(level==0?"稍后":level==1?"普通":"优先",site.Priority==level?activeButton:demoSkin.button)&&site.Priority!=level)feedback=site.SetPriority(level).Reason;
            GUILayout.EndHorizontal();GUILayout.BeginHorizontal();GUI.enabled=index>0;
            if(GUILayout.Button("置顶"))feedback=world.MoveConstruction(site,0).Reason;
            if(GUILayout.Button("上移"))feedback=world.MoveConstruction(site,index-1).Reason;
            GUI.enabled=index+1<total;if(GUILayout.Button("下移"))feedback=world.MoveConstruction(site,index+1).Reason;
            GUI.enabled=true;GUILayout.EndHorizontal();
        }
        private void DrawConstruction()
        {
            GUI.Box(buildPanel,GUIContent.none);GUILayout.BeginArea(new Rect(buildPanel.x+10,buildPanel.y+8,buildPanel.width-20,buildPanel.height-16));
            bool inspecting=!showConstructionQueue&&selectedSite&&!selectedSite.Cancelled&&!placing;
            GUILayout.BeginHorizontal();GUILayout.Label(showConstructionQueue?"建造顺序":inspecting?"工地详情":"建造",titleStyle);GUILayout.FlexibleSpace();
            if(!showConstructionQueue&&!placing&&GUILayout.Button("建造顺序",GUILayout.Width(85))){showConstructionQueue=true;inspecting=false;buildScroll=Vector2.zero;}
            if((inspecting||showConstructionQueue)&&GUILayout.Button("返回目录",GUILayout.Width(80))){showConstructionQueue=false;selectedSite=null;buildScroll=Vector2.zero;inspecting=false;}
            GUILayout.EndHorizontal();
            if(showConstructionQueue)
            {
                var queue=world.ConstructionQueue();int index=queue.IndexOf(selectedSite);
                if(index>=0){GUILayout.Label(selectedSite.Title,labelStyle);DrawConstructionPriority(selectedSite,queue.Count,index);}
                GUILayout.Label(queue.Count==0?"没有待建工地":index<0?"点选工地后可上移、下移或置顶。":"排前者先派空闲人手、先领料；在途照常送达。",smallStyle);
                buildScroll=GUILayout.BeginScrollView(buildScroll);
                for(int row=0;row<queue.Count;row++){var site=queue[row];if(GUILayout.Button($"{row+1}. {site.Title} · {site.PriorityName} · {site.Status}",site==selectedSite?activeButton:demoSkin.button))selectedSite=site;}
                GUILayout.EndScrollView();GUILayout.EndArea();return;
            }
            if(!inspecting)
            {
                GUILayout.BeginHorizontal();
                if(GUILayout.Button("仓储物流",!infrastructureTab?activeButton:demoSkin.button)&&infrastructureTab){infrastructureTab=false;CancelPlacement(false);selectedSite=null;buildScroll=Vector2.zero;}
                if(GUILayout.Button("基础设施",infrastructureTab?activeButton:demoSkin.button)&&!infrastructureTab){infrastructureTab=true;CancelPlacement(false);selectedSite=null;buildScroll=Vector2.zero;}
                GUILayout.EndHorizontal();
            }
            buildScroll=GUILayout.BeginScrollView(buildScroll);
            if(!inspecting&&infrastructureTab)
            {
                GUILayout.Label("基础路面 · 1格宽 · 行走+10%",sectionStyle);
                GUILayout.Label("每格木1 · 3工人秒；未铺路也可通行",smallStyle);
                GUI.enabled=!world.Paused&&!world.RoutesSettling;
                if(GUILayout.Button(placing&&buildKind==BuildingKind.Road?"取消铺路":"铺设基础路面")){if(placing&&buildKind==BuildingKind.Road)CancelPlacement();else BeginInfrastructure(BuildingKind.Road);}
                GUI.enabled=true;
                GUILayout.Label("围墙 · 分隔场地并阻挡通行",sectionStyle);
                GUILayout.Label("每段木2 · 6工人秒；请留出入口",smallStyle);
                GUI.enabled=!world.Paused&&!world.RoutesSettling;
                if(GUILayout.Button(placing&&buildKind==BuildingKind.Wall?"取消围墙":"铺设围墙")){if(placing&&buildKind==BuildingKind.Wall)CancelPlacement();else BeginInfrastructure(BuildingKind.Wall);}
                GUI.enabled=true;
                GUILayout.Label("点按放一段，拖动铺直线；右键取消。",smallStyle);
            }
            else if(!inspecting)
            {
                GUILayout.Label("物资堆场",sectionStyle);
                GUILayout.Label("木18 · 矿6 · 24工人秒 · 最多2人施工\n占地3×2 · 完工增加2个共享装卸点",smallStyle);
                GUI.enabled=!world.Paused&&!world.RoutesSettling;
                if(GUILayout.Button(placing?"取消放置（Esc）":"放置蓝图")){if(placing)CancelPlacement();else BeginPlacement();}
                if(placing&&GUILayout.Button($"旋转90° · 当前 {buildRotation*90}°"))buildRotation=(buildRotation+1)%4;
                GUI.enabled=true;
            }
            if(infrastructureTab&&recentStroke.Any(site=>site&&!site.Paused&&!site.Complete&&!site.Cancelled&&site.PlannedBuilders<2))
            {
                GUI.enabled=!world.Paused&&!world.RoutesSettling;
                if(GUILayout.Button("为刚铺整段补派空闲工人"))feedback=AssignRecentStroke().Reason;
                GUI.enabled=true;
            }
            if(inspecting)
            {
                var site=selectedSite;GUILayout.Space(5);GUILayout.Label($"{site.Title} · {site.Status}",sectionStyle);
                if(!site.Complete){var queue=world.ConstructionQueue();DrawConstructionPriority(site,queue.Count,queue.IndexOf(site));}
                for(int k=0;k<2;k++){var kind=(ResourceKind)k;if(site.MaterialCost(kind)>0)GUILayout.Label($"{GatheringWorld.Name(kind)}：已到{site.Delivered[k]}/{site.MaterialCost(kind)} · 在途{site.InTransit(kind)} · 待运{site.Needed(kind)}",labelStyle);}
                GUILayout.Label($"施工 {site.Labour:0.0}/{site.LabourRequired:0} 工人秒 · 已派{site.Assigned}人",labelStyle);
                GUILayout.Label($"运输 {site.Hauling} · 施工 {site.Constructing} · 等待/受阻 {site.Waiting}",smallStyle);
                GUILayout.Label(site.SupplyStatus,smallStyle);
                if(!site.Complete)GUILayout.Label(site.IsStockyard?"周边就近运料/施工；黄色入口用于完工装卸":"从周边就近可达侧运料/施工",smallStyle);
                GUI.enabled=!world.Paused&&!world.RoutesSettling&&!site.Paused&&!site.Complete&&!site.Cancelled;
                GUILayout.BeginHorizontal();if(GUILayout.Button("派1名空闲工人"))feedback=site.AssignIdle(1).Reason;if(GUILayout.Button("派2名空闲工人"))feedback=site.AssignIdle(2).Reason;GUILayout.EndHorizontal();
                if(!site.Complete&&!site.Cancelled)
                {
                    GUILayout.BeginHorizontal();for(int k=0;k<2;k++){var kind=(ResourceKind)k;GUI.enabled=site.Needed(kind)>0;if(GUILayout.Button("定位"+GatheringWorld.Name(kind)))FocusMaterial(kind);}GUILayout.EndHorizontal();GUI.enabled=true;
                }
                GUI.enabled=!world.Paused&&!site.Complete&&!site.Cancelled&&Selected.Count>0;
                if(GUILayout.Button($"安排所选 {Selected.Count} 人"))Apply("安排建造",w=>w.Build(site,AppendHeld));
                GUI.enabled=!world.Paused&&!site.Complete&&!site.Cancelled;
                if(GUILayout.Button(site.Paused?"继续工地":"暂停工地"))feedback=site.TogglePause().Reason;
                if(GUILayout.Button("取消工地并退料",dangerButton))feedback=site.Cancel().Reason;
                GUI.enabled=true;
            }
            else
            {
                GUILayout.Space(8);GUILayout.Label("场内清单",sectionStyle);
                bool any=false;foreach(var site in world.Buildings.Where(b=>!b.Cancelled&&(!b.IsStockyard)==infrastructureTab).OrderByDescending(b=>!b.Complete).ThenBy(b=>b.PriorityKey).ThenBy(b=>b.Id)){any=true;if(GUILayout.Button($"{site.Title} · {site.PriorityName} · {site.Status}")){CancelPlacement(false);InspectSite(site);}}
                if(!any)GUILayout.Label("暂无此类工地。放置后自动派空闲工人。",smallStyle);
            }
            GUILayout.EndScrollView();GUILayout.EndArea();
        }
        private void WorldLabel(Vector3 position,string text,List<Rect> used,float width,GatheringWorker worker=null)
        {
            var p=world.View.WorldToScreenPoint(position);if(p.z<=0)return;
            var rect=new Rect(Mathf.Round(p.x/uiScale-width*.5f),Mathf.Round((world.View.pixelHeight-p.y)/uiScale)-24,width,24);
            for(int i=0;i<32&&used.Any(r=>r.Overlaps(rect));i++)rect.y-=25;
            if(rect.y<header.yMax||rect.yMax>panel.y||showConstruction&&rect.Overlaps(buildPanel)||showMetrics&&rect.Overlaps(metricsPanel)||DepotVisible&&rect.Overlaps(depotPanel)||GuideVisible&&rect.Overlaps(guidePanel))return;
            used.Add(rect);GUI.Box(rect,text,worldStyle);
            if(worker&&Event.current.type==EventType.Repaint)workerLabels[worker]=rect;
        }
    }
}

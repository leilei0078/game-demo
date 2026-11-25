using System.Linq;
using UnityEngine;

namespace GatheringDemo
{
    public sealed partial class GatheringController
    {
        private bool GuideVisible=>!world.GuideHidden&&!showMetrics;
        private float focusUntil;
        private GatheringResource focusedResource;
        public int GuideStage=>world.GuideStep;
        private ConstructionSite GuideSite=>world.Buildings.Where(b=>b.IsStockyard&&!b.Cancelled).OrderByDescending(b=>b.Complete).ThenByDescending(b=>b.Priority).ThenBy(b=>b.Id).FirstOrDefault();
        private int Secured(ResourceKind kind)=>world.Inventory[(int)kind]+world.Buildings.Where(b=>b.IsStockyard).Sum(b=>b.Delivered[(int)kind])+world.Workers.Where(w=>w.CargoKind==kind&&w.ReturningMaterials).Sum(w=>w.CargoCount);
        private void UpdateGuide()
        {
            if(GuideSite&&GuideSite.Complete)world.GuideStep=Mathf.Max(world.GuideStep,6);
            if(world.GuideStep>=4&&!GuideSite&&world.GuideStep<6)world.GuideStep=3;
            bool advance=true;
            while(advance&&world.GuideStep<6)
            {
                advance=world.GuideStep==0?(Selected.Count>0||world.Workers.Any(w=>w.Phase!=WorkerPhase.Idle)||GuideSite):world.GuideStep==1?Secured(ResourceKind.Wood)>=18||GuideSite:world.GuideStep==2?Secured(ResourceKind.Ore)>=6||GuideSite:world.GuideStep==3?GuideSite:world.GuideStep==4?GuideSite&&GuideSite.Assigned>0:GuideSite&&GuideSite.Complete;
                if(advance)world.GuideStep++;
            }
        }
        public OrderResult FocusMaterial(ResourceKind kind)
        {
            var reference=selectedSite?selectedSite.transform.position:world.CameraFocus;
            var resource=world.Resources.Where(r=>r.Kind==kind&&r.Stock>0).OrderBy(r=>r.IsRefund?2:r.Work==WorkKind.Pickup?1:0).ThenBy(r=>Vector3.Distance(reference,r.transform.position)).FirstOrDefault();
            if(!resource){feedback="地图上没有剩余的"+GatheringWorld.Name(kind)+"；检查仓库、携货和其他工地投入";return OrderResult.No(feedback);}
            focusedResource=resource;focusUntil=Time.unscaledTime+5;world.SetCameraFocus(resource.transform.position);
            world.View.orthographicSize=Mathf.Min(world.View.orthographicSize,10.5f);
            feedback=$"已定位{resource.DisplayName}；先选工人，再点击资源采集（尚未改派任务）";return OrderResult.Yes(feedback);
        }
        private void DrawGuide()
        {
            GUI.Box(guidePanel,GUIContent.none);GUILayout.BeginArea(new Rect(guidePanel.x+10,guidePanel.y+8,guidePanel.width-20,guidePanel.height-16));
            int stage=world.GuideStep;
            GUILayout.Label(stage>=7?"第一次建造 · 已完成":$"第一次建造 · {Mathf.Min(stage+1,7)}/7",titleStyle);
            string title=stage==0?"选一名工人":stage==1?"先把木材送入仓库":stage==2?"补齐矿石":stage==3?"放置物资堆场蓝图":stage==4?"给工地派人":stage==5?"等待运料与施工":stage==6?"保存这次进展":"可以继续规划基地了";
            string detail=stage==0?"左键点击工人或底部编号；按住左键拖框可以多选。":stage==1?$"目标木材18件，已入库/投入 {Secured(ResourceKind.Wood)}。选人后点击木材资源，工人会自动采集并送库。":stage==2?$"目标矿石6件，已入库/投入 {Secured(ResourceKind.Ore)}。留一些人采木，另选工人采矿。":stage==3?"打开建造，放置蓝图。黄色是入口，R或按钮旋转；红色位置会说明原因，右键取消。":stage==4?"在工地面板派1/2名空闲工人。选人后右键工地也可派工；左键只查看。":stage==5?(GuideSite?GuideSite.SupplyStatus:"选择一处工地查看")+"。缺料时保留采集人手；已派工者会自动补料。":stage==6?"堆场已增加2个共享装卸点。点顶部“保存”，下次运行可“继续存档”。":"下一座工地可设置优先供料。右键拖图、滚轮缩放；可收起引导。";
            if(stage==4&&GuideSite&&GuideSite.AutoCrewGroup>0){title="等待空闲工人接单";detail="蓝图会自动找空闲工人。也可以选人后右键工地，指定谁来建。";}
            GUILayout.Label(title,sectionStyle);GUILayout.Label(detail,smallStyle,GUILayout.MinHeight(60));
            if(world.Paused)GUILayout.Label("游戏已暂停：可查看与定位，继续后执行任务。",smallStyle);
            if(stage==0){if(GUILayout.Button("选1名空闲工人"))Select(world.Workers.Where(w=>w.Phase==WorkerPhase.Idle&&w.CargoCount==0).Take(1));}
            else if(stage==1||stage==2){if(GUILayout.Button(stage==1?"定位木材":"定位矿石"))FocusMaterial(stage==1?ResourceKind.Wood:ResourceKind.Ore);}
            else if(stage==3){if(GUILayout.Button("打开建造")){CancelPlacement(false);selectedSite=null;showConstructionQueue=infrastructureTab=false;buildScroll=Vector2.zero;showConstruction=true;showMetrics=false;}}
            else if(stage==4||stage==5){if(GUILayout.Button("查看工地")&&GuideSite){CancelPlacement(false);InspectSite(GuideSite);world.SetCameraFocus(selectedSite.transform.position);}}
            else if(stage==6){GUI.enabled=saves&&!saves.Loading;if(GUILayout.Button("保存进展"))feedback=saves.Save().Reason;GUI.enabled=true;}
            if(GUILayout.Button("收起引导"))world.GuideHidden=true;
            GUILayout.EndArea();
        }
        private void DrawResourceFocus(System.Collections.Generic.List<Rect> used)
        {
            if(!focusedResource||Time.unscaledTime>focusUntil)return;
            Circle(orderMarker,focusedResource.transform.position,focusedResource.Radius+.4f,Color.yellow);markerUntil=Time.unscaledTime+.1f;
            WorldLabel(focusedResource.transform.position+Vector3.up*3.4f,"目标资源 · "+focusedResource.DisplayName,used,190);
        }
    }
}

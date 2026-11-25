using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GatheringDemo
{
    public sealed partial class GatheringController
    {
        public void BeginInfrastructure(BuildingKind kind)
        {
            if(kind!=BuildingKind.Road&&kind!=BuildingKind.Wall)return;
            buildKind=kind;infrastructureTab=true;showConstruction=true;showMetrics=false;placing=true;strokeDragging=false;
            mapDragMode=dragging=false;ResetRightGesture();suppressPrimary=true;
            feedback="左键拖拽直线，松开铺设蓝图；右键/Esc取消 · 中键拖图 · 滚轮缩放";
        }
        public OrderResult AssignRecentStroke()
        {
            var sites=recentStroke.Where(b=>b&&!b.Complete&&!b.Cancelled&&!b.Paused).ToArray();
            if(world.Paused||world.RoutesSettling||sites.Length==0)return OrderResult.No("没有可执行的整段工地，或当前暂停/更新通行中");
            return world.AssignIdleCrew(sites);
        }
        private void UpdateInfrastructurePlacement(Vector2 pos)
        {
            hoverMarker.positionCount=0;
            if(secondary.WasPressedThisFrame()){CancelPlacement();return;}
            var plane=new Plane(Vector3.up,Vector3.zero);var ray=world.View.ScreenPointToRay(pos);
            if(OverUI(pos)||panning||!plane.Raycast(ray,out float distance))
            {if(primary.WasReleasedThisFrame())strokeDragging=false;return;}
            previewPoint=PlacementGrid.Center(ray.GetPoint(distance));hasPreview=true;
            if(primary.WasPressedThisFrame()&&!suppressPrimary){strokeStart=previewPoint;strokeDragging=true;}
            var cells=GatheringWorld.Stroke(strokeDragging?strokeStart:previewPoint,previewPoint);
            int rotation=cells.Count>1?(Mathf.Abs(cells[1].x-cells[0].x)>.5f?0:1)+(buildRotation>=2?2:0):buildRotation;
            placementHint=world.StrokeReason(cells,rotation,buildKind,out var fresh);
            if(world.Paused)placementHint="游戏已暂停";else if(world.RoutesSettling)placementHint="正在更新通行，请稍候";
            entranceMarker.loop=false;entranceMarker.positionCount=cells.Count*5;
            entranceMarker.startColor=entranceMarker.endColor=placementHint==""?Color.cyan:new Color(1,.3f,.2f);
            var size=ConstructionSite.SizeFor(buildKind,rotation);int index=0;
            foreach(var cell in cells)for(int i=0;i<5;i++)
                entranceMarker.SetPosition(index++,cell+new Vector3((i==0||i==3||i==4?-1:1)*size.x*.5f,.12f,(i<2||i==4?-1:1)*size.z*.5f));
            hoverHint=placementHint==""?$"{fresh.Count}段 · 木{fresh.Count*ConstructionSite.CostFor(buildKind,ResourceKind.Wood)} · {fresh.Count*ConstructionSite.LabourFor(buildKind):0}工人秒 · 松开确认":placementHint;
            if(primary.WasReleasedThisFrame()&&strokeDragging)
            {
                strokeDragging=false;
                var result=world.PlaceInfrastructure(strokeStart,previewPoint,buildKind,rotation,out var sites);feedback=result.Reason;
                if(result.Accepted){recentStroke.Clear();recentStroke.AddRange(sites);InspectSite(sites[0]);feedback=world.RequestPlacementCrew(sites);CancelPlacement(false);}
            }
        }
    }
}

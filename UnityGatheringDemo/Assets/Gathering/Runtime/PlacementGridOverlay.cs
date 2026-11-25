using UnityEngine;

namespace GatheringDemo
{
    public sealed partial class GatheringController
    {
        private static readonly Color GridInk=new Color(.89f,.87f,.80f,.54f),EntryInk=new Color(1f,.8f,.13f);

        private void OnRenderObject()
        {
            if(!placing||!hasPreview||!world||!world.Ready||Camera.current!=world.View||!entranceMarker.sharedMaterial.SetPass(0))return;
            bool building=buildKind==BuildingKind.Stockyard;
            int step=building?PlacementGrid.BuildingStep:1;
            float radius=step*(building?1.5f:3.5f);
            var min=previewPoint-new Vector3(radius,0,radius);
            var max=previewPoint+new Vector3(radius,0,radius);
            GL.PushMatrix();GL.Begin(GL.QUADS);GL.Color(GridInk);
            for(float offset=0;offset<=radius*2;offset+=step)
            {
                GridQuad(min.x+offset-.022f,min.z,min.x+offset+.022f,max.z,.085f);
                GridQuad(min.x,min.z+offset-.022f,max.x,min.z+offset+.022f,.085f);
            }
            GL.Color(EntryInk);
            if(building)DrawEntryCells(previewPoint,buildRotation);
            else if(buildKind==BuildingKind.Road)
                foreach(var site in world.Buildings)
                    if(site.IsStockyard&&!site.Cancelled&&(site.transform.position-previewPoint).sqrMagnitude<64)
                        DrawEntryCells(site.transform.position,site.Rotation);
            GL.End();GL.PopMatrix();
        }

        private static void DrawEntryCells(Vector3 center,int rotation)
        {
            for(int index=0;index<2;index++)
            {
                var cell=PlacementGrid.EntranceCell(center,rotation,index);
                float left=cell.x-.43f,right=cell.x+.43f,bottom=cell.z-.43f,top=cell.z+.43f;
                GridQuad(left,bottom,right,bottom+.065f,.145f);GridQuad(left,top-.065f,right,top,.145f);
                GridQuad(left,bottom,left+.065f,top,.145f);GridQuad(right-.065f,bottom,right,top,.145f);
            }
        }

        private static void GridQuad(float left,float bottom,float right,float top,float height)
        {
            left=Mathf.Max(left,-GatheringWorld.HalfWidth);right=Mathf.Min(right,GatheringWorld.HalfWidth);
            bottom=Mathf.Max(bottom,-GatheringWorld.HalfDepth);top=Mathf.Min(top,GatheringWorld.HalfDepth);
            if(left>=right||bottom>=top)return;
            GL.Vertex3(left,height,bottom);GL.Vertex3(left,height,top);GL.Vertex3(right,height,top);GL.Vertex3(right,height,bottom);
        }
    }
}

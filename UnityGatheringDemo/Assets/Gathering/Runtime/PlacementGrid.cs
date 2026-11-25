using UnityEngine;

namespace GatheringDemo
{
    public static class PlacementGrid
    {
        public const int BuildingStep=2;
        public static Vector3 Center(Vector3 point,int step=1)
        {
            float Axis(float value)=>Mathf.Floor((value+.5f)/step)*step+(step-1)*.5f;
            return new Vector3(Axis(point.x),0,Axis(point.z));
        }
        public static Vector3 EntranceCell(Vector3 center,int rotation,int index)=>Center(ConstructionSite.Point(center,rotation,index));
    }
}

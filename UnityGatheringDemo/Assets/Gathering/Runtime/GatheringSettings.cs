using UnityEngine;

namespace GatheringDemo
{
    public enum ResourceKind { Wood, Ore, Food }
    public enum WorkKind { Chop, Mine, Gather, Pickup }
    public enum WorkerPhase { Idle, Moving, WaitingWork, ToWork, Working, WaitingDepot, ToDepot, Unloading, Blocked, WaitingBuild, ToSupply, LoadingSupply, ToSite, UnloadingSite, Constructing }

    [CreateAssetMenu(menuName = "Gathering/Logistics Settings")]
    public sealed class GatheringSettings : ScriptableObject
    {
        [SerializeField, Range(1,24)] private int maxItems = 6;
        [SerializeField, Min(.1f)] private float maxWeight = 18;
        [SerializeField, Range(.5f,6)] private float walkSpeed = 2.9f;
        [SerializeField, Range(.25f,4)] private float gatherRate = 1;
        [SerializeField, Min(.02f)] private float unloadPerItem = .1f;
        [SerializeField] private float woodWeight = 2, oreWeight = 3, foodWeight = .5f;
        public int MaxItems => maxItems;
        public float MaxWeight => maxWeight;
        public float Weight(ResourceKind kind) => kind == ResourceKind.Wood ? woodWeight : kind == ResourceKind.Ore ? oreWeight : foodWeight;
        public int Batch(ResourceKind kind, int stock) => Mathf.Max(0, Mathf.Min(stock, maxItems, Mathf.FloorToInt((maxWeight+.00001f)/Weight(kind))));
        public float Speed(float weight) => walkSpeed * (1-.3f*Mathf.Clamp01(weight/maxWeight));
        public float GatherTime(WorkKind kind, int count) => count * (kind == WorkKind.Chop ? 2.2f/6 : kind == WorkKind.Mine ? .55f : kind == WorkKind.Gather ? .25f : .65f/6) / gatherRate;
        public float UnloadTime(int count) => .3f+unloadPerItem*count;
        public string ValidateSettings()
        {
            foreach(float n in new[]{maxWeight,walkSpeed,gatherRate,unloadPerItem,woodWeight,oreWeight,foodWeight})if(float.IsNaN(n)||float.IsInfinity(n))return "参数必须是有限数值";
            if (maxItems<1 || maxItems>24 || maxWeight<=0 || walkSpeed<=0 || gatherRate<=0 || unloadPerItem<=0) return "件数、载重、速度与耗时必须大于零";
            foreach(ResourceKind kind in System.Enum.GetValues(typeof(ResourceKind))) if (Weight(kind)<=0 || Weight(kind)>maxWeight) return "载重需容纳至少一件资源";
            return "";
        }
    }
}

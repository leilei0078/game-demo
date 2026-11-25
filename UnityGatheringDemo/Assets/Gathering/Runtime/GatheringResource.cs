using UnityEngine;
using UnityEngine.AI;

namespace GatheringDemo
{
    public sealed class GatheringResource : MonoBehaviour
    {
        public int Id {get;private set;}
        internal void SetStableId(int id){Id=id;}
        internal void RestoreStock(int stock,int initial){Stock=stock;Initial=initial;Reserved=0;obstacle.enabled=stock>0;visual.gameObject.SetActive(stock>0);}
        public string DisplayName { get; private set; }
        public ResourceKind Kind { get; private set; }
        public WorkKind Work { get; private set; }
        public int Stock { get; private set; }
        public int Initial { get; private set; }
        public int Reserved { get; private set; }
        public float Radius { get; private set; }
        public float LabelHeight {get;private set;}
        public bool IsRefund=>DisplayName.StartsWith("退料箱");
        private NavMeshObstacle obstacle;
        private Transform visual;
        public void Initialize(string title, ResourceKind kind, WorkKind work, int stock, float radius, Transform body)
        {
            DisplayName=title;Kind=kind;Work=work;Stock=Initial=stock;Radius=radius;visual=body;
            LabelHeight=work==WorkKind.Chop?3f:1.4f;
            if(work==WorkKind.Chop)foreach(var part in body.GetComponentsInChildren<MeshRenderer>())
                if(part.enabled)LabelHeight=Mathf.Max(LabelHeight,part.bounds.max.y-transform.position.y+.22f);
            obstacle=gameObject.AddComponent<NavMeshObstacle>();obstacle.shape=NavMeshObstacleShape.Capsule;obstacle.radius=radius;obstacle.height=3;
            obstacle.center=Vector3.up;obstacle.carving=true;obstacle.carveOnlyStationary=false;
        }
        public int Reserve(int capacity) { int amount=Mathf.Min(capacity,Stock-Reserved);Reserved+=amount;return amount; }
        public void Release(int count) { Reserved=Mathf.Max(0,Reserved-count); }
        public void Harvest(int count)
        {
            Release(count);Stock-=count;
            if(Stock==0){obstacle.enabled=false;visual.gameObject.SetActive(false);}
        }
        public Vector3 Slot(int index)
        {
            float angle=index*Mathf.PI*.5f+.35f;
            return transform.position+new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*(Radius+.85f);
        }
    }
}

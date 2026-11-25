using UnityEngine;

namespace GatheringDemo
{
    public sealed class GatheringDepot : MonoBehaviour
    {
        internal const int DefaultWoodPercent=65,MinWoodPercent=20,MaxWoodPercent=80;
        private static readonly string[] LogNames={"WodenLog_A","WodenLog_B","WodenLog_C"};
        private readonly GameObject[] logs=new GameObject[18];
        private readonly GameObject[] stones=new GameObject[6];
        private GatheringWorld world;
        private Transform timberZone,stoneZone;
        private int visibleLogs,visibleStones,lastWoodPercent;

        internal void BuildRack(GatheringWorld owner,WastelandSurface terrain)
        {
            world=owner;
            var models=new[]{Resources.Load<GameObject>("MarketRack/WoodenBarrier_A"),
                Resources.Load<GameObject>("MarketRack/WoodenBarrier_B"),Resources.Load<GameObject>("MarketRack/WoodenDeck"),
                LoadLog(0),LoadLog(1),LoadLog(2)};
            bool hasRack=System.Array.TrueForAll(models,model=>model!=null);
            var root=new GameObject(hasRack?"Timber Rack":"Depot Stock").transform;
            var footprint=GetComponent<BoxCollider>();
            root.position=transform.TransformPoint(footprint.center-Vector3.up*footprint.size.y*.5f);
            root.SetParent(transform,true);
            if(hasRack)BuildTimberRack(root,models);
            stoneZone=CreateZone(root,"Stone Storage");
            BuildStones(stoneZone,terrain,hasRack?.13f:footprint.bounds.size.y);
            RefreshStack();
        }

        private static Transform CreateZone(Transform root,string label)
        {
            var zone=new GameObject(label).transform;
            zone.SetParent(root,false);
            return zone;
        }

        internal static GameObject LoadLog(int variant)=>Resources.Load<GameObject>("MarketRack/"+LogNames[variant]);

        private void BuildTimberRack(Transform root,GameObject[] models)
        {
            GetComponent<Renderer>().enabled=false;
            for(int side=0;side<2;side++)
            {
                int sign=side*2-1;
                Place(models[side],root,new Vector3(0,0,sign*.82f),new Vector3(2.82f,.99f,.26f),side*180);
                Place(models[2],root,new Vector3(sign*.673f,0,0),new Vector3(1.34f,.13f,1.45f),side*180);
            }
            timberZone=CreateZone(root,"Timber Storage");
            float[] lengths={.845f,.73f,.90f};
            int piece=0;
            for(int tier=0;tier<3;tier++)for(int column=0;column<7-tier;column++,piece++)
            {
                int variant=(column+2*tier)%3;
                var position=new Vector3(.03f*Mathf.Sin(piece*2.3f),.13f+tier*.17f,(column-(6-tier)*.5f)*.193f);
                logs[piece]=Place(models[3+variant],timberZone,position,new Vector3(lengths[variant],.19f,.19f),(piece%2)*180);
                logs[piece].name="Stored log "+(piece+1)+" - "+LogNames[variant];
                logs[piece].SetActive(false);
            }
        }

        private void BuildStones(Transform root,WastelandSurface terrain,float floor)
        {
            var positions=new[]{new Vector2(-.03f,.03f),new Vector2(-.18f,-.34f),new Vector2(.20f,-.32f),
                new Vector2(-.17f,.39f),new Vector2(.22f,.36f),new Vector2(-.02f,-.02f)};
            for(int index=0;index<stones.Length;index++)
            {
                var position=positions[index];string label="Stored stone "+(index+1);
                float width=index==0?.50f:index==5?.29f:.33f;
                float height=index==0?.35f:index==5?.21f:.22f;
                var point=new Vector3(position.x,floor+(index==5?.29f:0),position.y);
                if(terrain!=null)stones[index]=terrain.PlaceStockStone(label,root,point,width,height,31+index*67);
                else stones[index]=world.Shape(label,PrimitiveType.Sphere,root.TransformPoint(point+Vector3.up*height*.5f),new Vector3(width,height,width*.8f),new Color(.48f,.49f,.52f),root);
                stones[index].SetActive(false);
            }
        }

        internal static GameObject Place(GameObject model,Transform root,Vector3 position,Vector3 size,float yaw)
        {
            var part=Instantiate(model,root,false);
            part.name=model.name;
            var renderers=part.GetComponentsInChildren<Renderer>();
            var bounds=renderers[0].bounds;
            foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
            var pivot=root.InverseTransformPoint(new Vector3(bounds.center.x,bounds.min.y,bounds.center.z));
            var scale=new Vector3(size.x/bounds.size.x,size.y/bounds.size.y,size.z/bounds.size.z);
            var rotation=Quaternion.Euler(0,yaw,0);
            var offset=Vector3.Scale(pivot-part.transform.localPosition,scale);
            part.transform.localScale=Vector3.Scale(part.transform.localScale,scale);
            part.transform.localRotation=rotation*part.transform.localRotation;
            part.transform.localPosition=position-rotation*offset;
            return part;
        }

        private void LateUpdate(){if(world)RefreshStack();}

        private void RefreshStack()
        {
            int woodPercent=world.DepotWoodPercent;
            if(lastWoodPercent!=woodPercent)
            {
                const float usableWidth=2.68f;
                float woodWidth=usableWidth*woodPercent*.01f,stoneWidth=usableWidth-woodWidth;
                if(timberZone)
                {
                    timberZone.localPosition=Vector3.left*(stoneWidth*.5f);
                    timberZone.localScale=new Vector3(woodWidth/1.85f,1,1);
                }
                stoneZone.localPosition=Vector3.right*(woodWidth*.5f);
                stoneZone.localScale=new Vector3(stoneWidth/.94f,1,1);
                lastWoodPercent=woodPercent;
            }
            if(logs[0])RefreshStock(logs,world.Inventory[(int)ResourceKind.Wood],ref visibleLogs);
            RefreshStock(stones,world.Inventory[(int)ResourceKind.Ore],ref visibleStones);
        }

        private static void RefreshStock(GameObject[] pieces,int count,ref int visible)
        {
            int target=count<=0?0:Mathf.Min(pieces.Length,1+(count-1)/6);
            while(visible<target)pieces[visible++].SetActive(true);
            while(visible>target)pieces[--visible].SetActive(false);
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GatheringDemo
{
    // A conservative clearance grid checks the candidate before it carves the live NavMesh.
    // Preserve every currently connected entrance; actual worker travel still uses NavMesh.
    internal static class PlacementConnectivity
    {
        private const float Step=.25f,Clearance=.45f;
        private static GatheringWorld cachedWorld;
        private static Vector3 cachedPoint;
        private static int cachedRotation,cachedRevision;
        private static float cachedUntil;
        private static string cachedResult;
        public static string Check(GatheringWorld world,Vector3 point,int rotation)
        {
            if(cachedWorld==world&&cachedPoint==point&&cachedRotation==rotation&&cachedRevision==world.RoadRevision&&Time.unscaledTime<cachedUntil)return cachedResult;
            cachedWorld=world;cachedPoint=point;cachedRotation=rotation;cachedRevision=world.RoadRevision;cachedUntil=Time.unscaledTime+.2f;
            return cachedResult=Evaluate(world,point,rotation);
        }
        internal static string Evaluate(GatheringWorld world,Vector3 candidate,int rotation,BuildingKind kind=BuildingKind.Stockyard,System.Collections.Generic.IReadOnlyList<Vector3> stroke=null)
        {
            int nx=(int)(GatheringWorld.HalfWidth*2/Step),nz=(int)(GatheringWorld.HalfDepth*2/Step);
            var open=new bool[nx*nz];var after=new bool[open.Length];
            bool Box(Vector3 p,Vector3 c,float hx,float hz)=>Mathf.Abs(p.x-c.x)<hx+Clearance&&Mathf.Abs(p.z-c.z)<hz+Clearance;
            int Cell(Vector3 p){int x=Mathf.RoundToInt((p.x+GatheringWorld.HalfWidth)/Step-.5f),z=Mathf.RoundToInt((p.z+GatheringWorld.HalfDepth)/Step-.5f);return x<0||x>=nx||z<0||z>=nz?-1:z*nx+x;}
            var resources=world.Resources.Where(r=>r.Stock>0).ToArray();var buildings=world.Buildings.Where(b=>b.BlocksMovement).ToArray();
            var size=ConstructionSite.SizeFor(kind,rotation);
            for(int z=0;z<nz;z++)for(int x=0;x<nx;x++)
            {
                int i=z*nx+x;var p=new Vector3(-GatheringWorld.HalfWidth+(x+.5f)*Step,0,-GatheringWorld.HalfDepth+(z+.5f)*Step);
                bool free=Mathf.Abs(p.x)<GatheringWorld.HalfWidth-Clearance&&Mathf.Abs(p.z)<GatheringWorld.HalfDepth-Clearance&&!Box(p,new Vector3(-6,0,-2),1.5f,1.05f)&&(!world.Roadblock||Mathf.Abs(p.x+2)>.25f+Clearance);
                if(free)foreach(var r in resources)if((p-r.transform.position).sqrMagnitude<(r.Radius+Clearance)*(r.Radius+Clearance)){free=false;break;}
                if(free)foreach(var b in buildings){var s=b.FootprintSize;if(Box(p,b.transform.position,s.x*.5f,s.z*.5f)){free=false;break;}}
                open[i]=free;after[i]=free&&(stroke==null?!Box(p,candidate,size.x*.5f,size.z*.5f):!stroke.Any(c=>Box(p,c,size.x*.5f,size.z*.5f)));
            }
            bool[] Flood(bool[] cells)
            {
                var seen=new bool[cells.Length];int seed=Cell(new Vector3(-6,0,-.15f));var queue=new Queue<int>();
                if(seed>=0&&cells[seed]){seen[seed]=true;queue.Enqueue(seed);}
                while(queue.Count>0){int i=queue.Dequeue(),x=i%nx,z=i/nx;void Visit(int n){if(cells[n]&&!seen[n]){seen[n]=true;queue.Enqueue(n);}}if(x>0)Visit(i-1);if(x<nx-1)Visit(i+1);if(z>0)Visit(i-nx);if(z<nz-1)Visit(i+nx);}
                return seen;
            }
            var before=Flood(open);var reachable=Flood(after);
            bool Lost(Vector3 p){int i=Cell(p);return i>=0&&before[i]&&!reachable[i];}
            bool Connected(Vector3 p){int cell=Cell(p);return cell>=0&&reachable[cell];}
            foreach(var center in stroke??new[]{candidate})
            {
                if(kind==BuildingKind.Stockyard){for(int i=0;i<2;i++)if(!Connected(ConstructionSite.Point(center,rotation,i)))return "放置后堆场装卸入口将不连通，请转向或留出通路";}
                else if(!Enumerable.Range(0,ConstructionSite.BuildPointCount).Any(i=>Connected(ConstructionSite.BuildPointFor(kind,center,rotation,i))))return "放置后工地周边没有可达施工位置";
            }
            foreach(var b in world.Buildings.Where(b=>!b.Cancelled))
            {
                if(b.IsStockyard){for(int i=0;i<2;i++)if(Lost(b.WorkPoint(i)))return "放置后会截断已有建筑入口";}
                else if(!b.Complete&&!Enumerable.Range(0,ConstructionSite.BuildPointCount).Any(i=>Connected(b.BuildPoint(i))))return "放置后会截断已有工地施工通路";
            }
            foreach(var r in resources)for(int i=0;i<4;i++)if(Lost(r.Slot(i)))return "放置后会截断资源采集路线";
            foreach(var w in world.Workers)if(Lost(w.transform.position)||Lost(world.Home(w.Id)))return "放置后会把工人或待命区困住";
            return "";
        }
    }
}

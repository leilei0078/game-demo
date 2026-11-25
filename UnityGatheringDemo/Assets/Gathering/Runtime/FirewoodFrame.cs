using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GatheringDemo
{
    // Metres in the worker's rest pose: the shelf is y=0 and the back faces -Z.
    internal sealed class FirewoodFrame : MonoBehaviour
    {
        private readonly List<Object> generated=new();
        private GameObject load;

        internal static FirewoodFrame Attach(Transform worker,Transform spine,Material source)
        {
            var frame=new GameObject("Firewood frame").AddComponent<FirewoodFrame>();
            frame.Build(source);
            frame.transform.SetPositionAndRotation(spine.position-worker.up*.43f,worker.rotation);
            frame.transform.SetParent(spine,true);
            frame.gameObject.SetActive(false);
            return frame;
        }

        internal void Show(bool equipped,bool loaded)
        {
            load.SetActive(loaded);
            gameObject.SetActive(equipped);
        }

        private T Own<T>(T asset) where T:Object {generated.Add(asset);return asset;}
        private void OnDestroy(){foreach(var asset in generated)Destroy(asset);}

        private Material Surface(Material source,Color tint)
        {
            var surface=Own(new Material(source){color=tint});
            surface.SetFloat("_Glossiness",.09f);
            return surface;
        }

        private void Build(Material source)
        {
            var timber=Surface(source,new Color(.72f,.59f,.43f));
            var grain=Own(new Texture2D(48,96,TextureFormat.RGB24,true){name="Weathered frame grain"});
            var pixels=new Color32[grain.width*grain.height];
            for(int y=0;y<grain.height;y++)for(int x=0;x<grain.width;x++)
            {
                float bend=Mathf.PerlinNoise(x*.053f,y*.033f);
                float drift=Mathf.PerlinNoise(x*.21f+bend,y*.023f);
                byte tone=(byte)(155+60*drift+30*Mathf.Abs(Mathf.Sin(x*1.19f+bend*3)));
                pixels[y*grain.width+x]=new Color32(tone,tone,tone,255);
            }
            grain.SetPixels32(pixels);grain.Apply(true,true);timber.mainTexture=grain;
            var strap=Surface(source,new Color(.42f,.29f,.18f));
            var rope=Surface(source,new Color(.67f,.53f,.32f));
            var beam=Sweep("Beveled timber",new[]{Vector3.down*.5f,Vector3.up*.5f},new[]{
                new Vector2(.5f,-.34f),new Vector2(.5f,.34f),new Vector2(.34f,.5f),new Vector2(-.34f,.5f),
                new Vector2(-.5f,.34f),new Vector2(-.5f,-.34f),new Vector2(-.34f,-.5f),new Vector2(.34f,-.5f)});
            void Beam(string label,Vector3 start,Vector3 end,float width,float depth)
            {
                var part=Part(label,transform,beam,timber);
                part.localPosition=(start+end)*.5f;part.localRotation=Quaternion.FromToRotation(Vector3.up,end-start);
                part.localScale=new Vector3(width,Vector3.Distance(start,end),depth);
            }
            foreach(int side in new[]{-1,1})
            {
                float x=side*.188f;
                Beam("Upright",new Vector3(x,-.075f,-.172f),new Vector3(x,.683f,-.149f),.059f,.057f);
                Beam("Shelf runner",new Vector3(x,-.032f,-.162f),new Vector3(x,-.032f,-.514f),.043f,.048f);
                Beam("Shelf brace",new Vector3(x,-.068f,-.172f),new Vector3(x,-.023f,-.354f),.037f,.031f);
                Beam("End stop",new Vector3(x,-.056f,-.499f),new Vector3(x,.066f,-.499f),.034f,.035f);
                var path=new[]{new Vector3(x,.56f,-.147f),new Vector3(side*.153f,.564f,-.098f),
                    new Vector3(side*.126f,.563f,-.026f),new Vector3(side*.12f,.537f,.057f),
                    new Vector3(side*.129f,.48f,.114f),new Vector3(side*.155f,.368f,.121f),
                    new Vector3(side*.178f,.253f,.082f),new Vector3(side*.176f,.142f,-.008f),new Vector3(x,.095f,-.173f)};
                var section=new[]{new Vector2(.024f,-.005f),new Vector2(.024f,.005f),new Vector2(-.024f,.005f),new Vector2(-.024f,-.005f)};
                Part("Shoulder strap",transform,Sweep("Harness strip",path,section),strap);
            }
            foreach(float y in new[]{.095f,.33f,.558f})
                Beam("Crossbar",new Vector3(-.209f,y,-.172f),new Vector3(.209f,y,-.172f),.047f,.039f);
            foreach(float z in new[]{-.227f,-.309f,-.391f,-.473f})
                Beam("Shelf slat",new Vector3(-.216f,.005f,z),new Vector3(.216f,.005f,z),.073f,.026f);

            load=new GameObject("Wood load");load.transform.SetParent(transform,false);
            var logs=Enumerable.Range(0,3).Select(GatheringDepot.LoadLog).ToArray();
            Material bark=null,cut=null;
            if(logs.Any(log=>!log))
            {
                bark=Surface(source,new Color(.34f,.20f,.12f));
                cut=Surface(source,new Color(.79f,.62f,.39f));
            }
            const float diameter=.15f;
            for(int index=0;index<6;index++)
            {
                int variant=(index+index/2*2)%logs.Length;
                float length=.48f-variant*.03f;
                var point=new Vector3(Mathf.Sin(index*2)*.019f,.018f+index/2*.133f,-.273f-index%2*.141f);
                if(logs[variant])GatheringDepot.Place(logs[variant],load.transform,point,new Vector3(length,diameter,diameter),index%2*180);
                else
                {
                    var center=point+Vector3.up*(diameter*.5f);
                    Cylinder(center,length,diameter,bark);
                    Cylinder(center+Vector3.right*length*.5f,.004f,.136f,cut);
                    Cylinder(center-Vector3.right*length*.5f,.004f,.136f,cut);
                }
            }
            var round=Enumerable.Range(0,8).Select(i=>new Vector2(Mathf.Cos(i*Mathf.PI/4),Mathf.Sin(i*Mathf.PI/4))*.009f).ToArray();
            foreach(float x in new[]{-.106f,.105f})
            {
                var lashing=new[]{new Vector3(x,.564f,-.177f),new Vector3(x,.530f,-.207f),new Vector3(x,.443f,-.264f),
                    new Vector3(x,.444f,-.408f),new Vector3(x,.423f,-.461f),new Vector3(x,.371f,-.488f),
                    new Vector3(x,.06f,-.494f),new Vector3(x,-.024f,-.482f),new Vector3(x,-.029f,-.253f),
                    new Vector3(x,.004f,-.176f),new Vector3(x,.516f,-.144f),new Vector3(x,.565f,-.153f),new Vector3(x,.564f,-.177f)};
                Part("Hemp lashing",load.transform,Sweep("Lashing loop",lashing,round),rope);
            }
            load.SetActive(false);
        }

        private void Cylinder(Vector3 center,float length,float diameter,Material surface)
        {
            var part=GameObject.CreatePrimitive(PrimitiveType.Cylinder);part.transform.SetParent(load.transform,false);
            part.transform.localPosition=center;part.transform.localRotation=Quaternion.Euler(0,0,90);
            part.transform.localScale=new Vector3(diameter,length*.5f,diameter);part.GetComponent<Renderer>().sharedMaterial=surface;
            var collider=part.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
        }

        private static Transform Part(string label,Transform parent,Mesh mesh,Material surface)
        {
            var part=new GameObject(label);part.transform.SetParent(parent,false);
            part.AddComponent<MeshFilter>().sharedMesh=mesh;part.AddComponent<MeshRenderer>().sharedMaterial=surface;
            return part.transform;
        }

        private Mesh Sweep(string label,Vector3[] path,Vector2[] section)
        {
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var indices=new List<int>();
            int sides=section.Length;
            bool loop=path[0]==path[path.Length-1];
            for(int row=0;row<path.Length;row++)
            {
                int prev=row>0 ? row-1 : loop ? path.Length-2 : 0;
                int next=row<path.Length-1 ? row+1 : loop ? 1 : row;
                var tangent=(path[next]-path[prev]).normalized;
                var across=Vector3.ProjectOnPlane(Vector3.right,tangent).normalized;
                var depth=Vector3.Cross(tangent,across);
                for(int side=0;side<sides;side++)
                {
                    // Split cross-section edges for flat timber faces and edge bevels.
                    for(int edge=0;edge<2;edge++)
                    {
                        var point=section[(side+edge)%sides];
                        vertices.Add(path[row]+across*point.x+depth*point.y);
                        uv.Add(new Vector2((side+edge)/(float)sides,row/(float)(path.Length-1)));
                    }
                    if(row==0)continue;
                    int a=((row-1)*sides+side)*2,b=a+sides*2;
                    indices.AddRange(new[]{a,a+1,b+1,a,b+1,b});
                }
            }
            if(!loop)for(int end=0;end<2;end++)
            {
                int start=(end==0?0:path.Length-1)*sides*2,cap=vertices.Count;
                for(int side=0;side<sides;side++){vertices.Add(vertices[start+side*2]);uv.Add(section[side]+Vector2.one*.5f);}
                for(int side=1;side<sides-1;side++)
                    indices.AddRange(end==0?new[]{cap,cap+side+1,cap+side}:new[]{cap,cap+side,cap+side+1});
            }
            var mesh=Own(new Mesh{name=label});mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();
            return mesh;
        }
    }
}

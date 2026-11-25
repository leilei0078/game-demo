using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

namespace GatheringDemo
{
    internal sealed class WastelandSurface
    {
        private const string AssetPath="Heathland/";
        private const int MaskWidth=576,MaskHeight=416;
        private const float Width=GatheringWorld.HalfWidth*2,Depth=GatheringWorld.HalfDepth*2;
        private static readonly Vector2 Center=new Vector2(2,-2.8f);
        private static readonly Vector2 Across=new Vector2(-1,1).normalized,Along=new Vector2(-1,-1).normalized;
        private static readonly Vector4 Map=new Vector4(-Width/2,-Depth/2,Width,Depth);
        private readonly List<Object> generated=new List<Object>();
        private readonly List<MeshRenderer> rocks=new List<MeshRenderer>();
        private Mesh boulderMesh;
        private Material boulderMaterial;
        private readonly List<Rect> clearances=new List<Rect>();
        private Texture2D occupied;
        private byte[] occupancy;

        private static Texture2D Scan(string name)=>Resources.Load<Texture2D>(AssetPath+name);
        private static float Noise(float east,float north,float scale)=>Mathf.PerlinNoise(23.37f+east*scale,38.12f+north*scale);
        private static Vector3 Point(float across,float along,float elevation)
        {
            var point=Center+Across*across+Along*along;
            return new Vector3(point.x,elevation,point.y);
        }

        private static Vector3 Coverage(float east,float north)
        {
            var offset=new Vector2(east,north)-Center;
            float across=Vector2.Dot(offset,Across),along=Vector2.Dot(offset,Along);
            float fiber=Noise(east,north,1.7f),medium=Noise(east+31,north-28,.46f),broad=Noise(east,north,.14f);
            float axis=1.10f*Mathf.Sin(along*.28f)+.33f*Mathf.Sin(along*.87f);
            float trail=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.64f,3.1f,Mathf.Abs(across-axis)+(medium-.5f)*2.35f));
            float yard=Mathf.SmoothStep(.07f,1,Mathf.InverseLerp(2.5f,5.2f,Vector2.Distance(new Vector2(east,north),new Vector2(10.5f,-5.8f))+(fiber-.5f)*.8f));
            float grass=trail*yard*Mathf.Clamp01(.57f+1.40f*(medium-.4f)+.48f*(broad-.5f)+.3f*(fiber-.5f));
            return new Vector3(grass,Mathf.Clamp01(.42f+(fiber-.5f)*.42f+(broad-.5f)*1.06f),Mathf.Clamp01(.62f+(broad-.5f)*.88f+(medium-.5f)*.3f));
        }

        public static WastelandSurface Attach(GameObject ground)
        {
            var shader=Resources.Load<Shader>(AssetPath+"Heathland");
            var boulder=Resources.Load<TextAsset>(AssetPath+"Boulder");
            if(!shader||!shader.isSupported||!boulder)return null;
            var scans=new[]{"Soil","SoilNormal","Grass","GrassNormal","Gravel","GravelNormal","Rock","RockNormal","RockArm"};
            var textures=new Texture2D[scans.Length];
            for(int index=0;index<scans.Length;index++)
            {
                textures[index]=Scan(scans[index]);
                if(!textures[index])return null;
            }
            var surface=new WastelandSurface();
            var parent=new GameObject("Heathland Details");parent.transform.SetParent(ground.transform.parent,false);
            var groundMaterial=surface.Material(shader,"Cold grey scanned soil");
            var inputs=new[]{"_MainTex","_BaseNormal","_GrassTex","_GrassNormal","_SandTex","_SandNormal"};
            for(int index=0;index<inputs.Length;index++)groundMaterial.SetTexture(inputs[index],textures[index]);
            var mask=surface.Own(new Texture2D(MaskWidth,MaskHeight,TextureFormat.RGBA32,true,true)
            {name="Heath coverage",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear});
            var pixels=new Color32[MaskWidth*MaskHeight];
            for(int row=0;row<MaskHeight;row++)for(int column=0;column<MaskWidth;column++)
            {
                var sample=Coverage(Map.x+(column+.5f)*Width/MaskWidth,Map.y+(row+.5f)*Depth/MaskHeight);
                pixels[row*MaskWidth+column]=new Color(sample.x,sample.y,sample.z);
            }
            mask.SetPixels32(pixels);mask.Apply(true,true);
            groundMaterial.SetTexture("_MaskTex",mask);
            var receiver=ground.GetComponent<Renderer>();receiver.sharedMaterial=groundMaterial;
            receiver.shadowCastingMode=ShadowCastingMode.Off;

            surface.occupied=surface.Own(new Texture2D(MaskWidth,MaskHeight,TextureFormat.R8,false,true)
            {name="Heath construction clearance",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Point});
            surface.occupancy=new byte[MaskWidth*MaskHeight];
            var grass=surface.Material(shader,"Cool dry grass");grass.EnableKeyword("HEATH_GRASS");
            grass.SetTexture("_Occupied",surface.occupied);
            surface.AddGrass(parent.transform,grass);
            var stone=surface.Material(shader,"Blue-grey weathered rock");stone.EnableKeyword("HEATH_ROCK");
            stone.SetTexture("_MainTex",textures[6]);stone.SetTexture("_BaseNormal",textures[7]);stone.SetTexture("_RockArm",textures[8]);
            surface.boulderMaterial=stone;surface.boulderMesh=surface.Own(ReadBoulder(boulder));
            surface.AddRocks(parent.transform,stone,surface.boulderMesh);
            return surface;
        }

        private T Own<T>(T asset) where T:Object{generated.Add(asset);return asset;}
        private Material Material(Shader shader,string name)
        {
            var material=Own(new Material(shader){name=name});material.SetVector("_Map",Map);return material;
        }

        private static MeshRenderer Model(string name,Transform parent,Mesh mesh,Material material,bool cast)
        {
            var node=new GameObject(name);node.transform.SetParent(parent,false);
            node.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=node.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=cast?ShadowCastingMode.On:ShadowCastingMode.Off;
            return renderer;
        }

        private sealed class GrassChunk
        {
            private readonly List<Vector3> vertices=new List<Vector3>(),normals=new List<Vector3>();
            private readonly List<Color> colors=new List<Color>();
            private readonly List<int> triangles=new List<int>();

            public void Blade(Vector3 root,Vector3 direction,float width,float rise,float reach,Color shade)
            {
                var side=Vector3.Cross(Vector3.up,direction)*width;
                var knee=root+direction*reach*.36f+Vector3.up*rise*.72f;
                var tip=root+direction*reach+Vector3.up*rise;
                int first=vertices.Count;
                vertices.Add(root-side);vertices.Add(root+side);vertices.Add(knee-side*.52f);vertices.Add(knee+side*.52f);vertices.Add(tip);
                var normal=(Vector3.up+direction*.32f).normalized;
                for(int index=0;index<5;index++)
                {
                    normals.Add(normal);float tint=index<2?.62f:index<4?.94f:1.14f;
                    colors.Add(new Color(shade.r*tint,shade.g*tint,shade.b*tint,index<2?.63f:.97f));
                }
                triangles.Add(first);triangles.Add(first+2);triangles.Add(first+1);
                triangles.Add(first+1);triangles.Add(first+2);triangles.Add(first+3);
                triangles.Add(first+2);triangles.Add(first+4);triangles.Add(first+3);
            }

            public Mesh Create()
            {
                var mesh=new Mesh{name="Clustered straw ribbons",indexFormat=vertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
                mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);
                return mesh;
            }
        }

        private void AddGrass(Transform parent,Material material)
        {
            var chunks=new Dictionary<Vector2Int,GrassChunk>();var random=new Random(48150);
            float Range(float low,float high)=>low+(high-low)*(float)random.NextDouble();
            for(int sample=0;sample<8150;sample++)
            {
                var center=Point(Range(-10.7f,10.7f),Range(-12.6f,11f),.013f);
                if(Mathf.Abs(center.x)>Width/2-.4f||Mathf.Abs(center.z)>Depth/2-.4f)continue;
                var density=Coverage(center.x,center.z).x;
                if(density<.32f||Range(0,1)>density*density*.93f)continue;
                var key=new Vector2Int(Mathf.FloorToInt(center.x/5),Mathf.FloorToInt(center.z/5));
                if(!chunks.TryGetValue(key,out var chunk))chunks[key]=chunk=new GrassChunk();
                float angle=.45f+(Noise(center.x,center.z,.48f)-.5f)*2.7f+Range(-.4f,.4f);
                int count=random.Next(10,22);
                var tone=Color.Lerp(new Color(.57f,.43f,.29f),new Color(.78f,.67f,.51f),Range(.25f,.96f)).linear;
                for(int blade=0;blade<count;blade++)
                {
                    var root=center+new Vector3(Range(-.17f,.17f),0,Range(-.15f,.15f));
                    float bearing=angle+Range(-1.10f,1.1f);
                    var direction=new Vector3(Mathf.Cos(bearing),0,Mathf.Sin(bearing));
                    chunk.Blade(root,direction,Range(.009f,.021f),Range(.075f,.30f),Range(.10f,.38f),tone*Range(.79f,1.16f));
                }
            }
            foreach(var chunk in chunks.Values)Model("Dry grass patch",parent,Own(chunk.Create()),material,false);
        }

        private static Mesh ReadBoulder(TextAsset boulder)
        {
            using var reader=new BinaryReader(new MemoryStream(boulder.bytes,false));
            int vertexCount=reader.ReadInt32(),indexCount=reader.ReadInt32();
            Vector3 Vector()=>new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
            var vertices=new Vector3[vertexCount];var normals=new Vector3[vertexCount];var uv=new Vector2[vertexCount];
            var triangles=new int[indexCount];
            for(int index=0;index<vertexCount;index++)
            {
                vertices[index]=Vector();normals[index]=Vector();uv[index]=new Vector2(reader.ReadSingle(),reader.ReadSingle());
            }
            for(int index=0;index<indexCount;index++)triangles[index]=reader.ReadUInt16();
            var mesh=new Mesh{name="Namaqualand boulder scan",vertices=vertices,normals=normals,uv=uv,triangles=triangles};
            mesh.RecalculateTangents();mesh.RecalculateBounds();return mesh;
        }

        private void AddRocks(Transform parent,Material material,Mesh mesh)
        {
            var placements=new[]{
                new Vector4(-6.3f,1.2f,1.18f,128),new Vector4(-7.1f,2.12f,.73f,247),new Vector4(-5.42f,-.1f,.53f,177),
                new Vector4(6.6f,-4.8f,1.06f,314),new Vector4(7.3f,-6.5f,.76f,93),new Vector4(5.25f,-5.85f,.52f,204),
                new Vector4(-5.1f,6.05f,1.08f,38),new Vector4(-6.2f,5.62f,.68f,223),new Vector4(7.15f,6.3f,.63f,195),
                new Vector4(-7.55f,4.15f,.42f,37),new Vector4(5.9f,.9f,.43f,112)
            };
            foreach(var placement in placements)
            {
                var rock=Model("Scanned boulder",parent,mesh,material,true);
                rock.transform.SetPositionAndRotation(Point(placement.x,placement.y,-.17f),Quaternion.Euler(0,placement.w,0));
                float scale=placement.z;rock.transform.localScale=new Vector3(scale,scale*.69f,scale*.94f);
                rocks.Add(rock);
            }
        }

        internal GameObject PlaceStockStone(string label,Transform parent,Vector3 bottom,float width,float height,float yaw)
        {
            var stone=Model(label,parent,boulderMesh,boulderMaterial,true).transform;
            var bounds=boulderMesh.bounds;
            var rotation=Quaternion.Euler(0,yaw,0);
            float horizontal=width/new Vector2(bounds.size.x,bounds.size.z).magnitude;
            var scale=new Vector3(horizontal,height/bounds.size.y,horizontal);
            stone.localRotation=rotation;
            stone.localScale=scale;
            stone.localPosition=bottom-rotation*Vector3.Scale(new Vector3(bounds.center.x,bounds.min.y,bounds.center.z),scale);
            return stone.gameObject;
        }

        public void AttachOre(GameObject visual,float radius)
        {
            var anchor=visual.transform.parent;
            void Place(string name,Vector3 offset,float size,float yaw)
            {
                var rock=Model(name,anchor,boulderMesh,boulderMaterial,true).transform;
                rock.localPosition=offset*radius;rock.localRotation=Quaternion.Euler(0,yaw,0);
                rock.localScale=new Vector3(1,.69f,.94f)*(size*radius);
                rock.SetParent(visual.transform,true);
            }
            Place("Ore boulder",new Vector3(-.10f,-.07f,-.04f),.65f,128);
            Place("Ore fragment",new Vector3(.53f,-.04f,.51f),.25f,177);
            visual.GetComponent<Renderer>().enabled=false;
        }

        public void RefreshClearances(GatheringWorld world)
        {
            clearances.Clear();
            void Clear(Vector3 position,float halfWidth,float halfDepth)
                =>clearances.Add(new Rect(position.x-halfWidth,position.z-halfDepth,halfWidth*2,halfDepth*2));
            Clear(new Vector3(-6,0,-1.8f),1.66f,1.4f);
            foreach(var worker in world.Workers)Clear(world.Home(worker.Id),.37f,.37f);
            foreach(var resource in world.Resources)Clear(resource.transform.position,resource.Radius+.28f,resource.Radius+.28f);
            if(world.Roadblock)Clear(new Vector3(-2,0,0),.35f,Depth/2);
            foreach(var site in world.Buildings)
            {
                if(site.Cancelled)continue;
                var size=site.FootprintSize;
                Clear(site.transform.position,size.x*.5f+.09f,size.z*.5f+.09f);
                if(site.IsStockyard)for(int index=0;index<2;index++)Clear(site.WorkPoint(index),.56f,.56f);
            }
            System.Array.Clear(occupancy,0,occupancy.Length);
            foreach(var area in clearances)
            {
                int left=Mathf.Clamp(Mathf.FloorToInt((area.xMin-Map.x)*MaskWidth/Width),0,MaskWidth);
                int right=Mathf.Clamp(Mathf.CeilToInt((area.xMax-Map.x)*MaskWidth/Width),0,MaskWidth);
                int near=Mathf.Clamp(Mathf.FloorToInt((area.yMin-Map.y)*MaskHeight/Depth),0,MaskHeight);
                int far=Mathf.Clamp(Mathf.CeilToInt((area.yMax-Map.y)*MaskHeight/Depth),0,MaskHeight);
                for(int row=near;row<far;row++)for(int column=left;column<right;column++)occupancy[row*MaskWidth+column]=255;
            }
            occupied.SetPixelData(occupancy,0);occupied.Apply(false,false);
            foreach(var rock in rocks)
            {
                var bounds=rock.bounds;
                var area=Rect.MinMaxRect(bounds.min.x,bounds.min.z,bounds.max.x,bounds.max.z);
                rock.enabled=!clearances.Exists(clearance=>area.Overlaps(clearance));
            }
        }

        public void Dispose(){foreach(var asset in generated)if(asset)Object.Destroy(asset);}

        public static Texture2D CreateTexture(float width,float depth)
        {
            const int size=512;
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,true)
            {
                name="Ash-grey bare soil",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear,anisoLevel=4
            };
            var pixels=new Color32[size*size];
            var dark=new Color(.332f,.343f,.36f);
            var ash=new Color(.61f,.606f,.59f);
            var gravel=new Color(.66f,.67f,.68f);
            for(int row=0;row<size;row++)for(int column=0;column<size;column++)
            {
                float east=(column+.5f)*width/size,north=(row+.5f)*depth/size;
                float broad=Mathf.PerlinNoise(east*.12f+17,north*.12f+29);
                float patch=Mathf.PerlinNoise(east*.57f+43,north*.57f+11);
                float grain=Mathf.PerlinNoise(east*4.8f+7,north*4.8f+53);
                float seam=Mathf.PerlinNoise(east*1.08f+patch*.5f+81,north*1.52f+patch*.5f+19);
                float tone=.53f+(broad-.5f)*.82f+(patch-.5f)*.35f+(grain-.5f)*.13f;
                var soil=Color.Lerp(dark,ash,tone);
                float grit=Mathf.InverseLerp(.67f,.87f,grain)*Mathf.InverseLerp(.47f,.72f,seam);
                pixels[row*size+column]=Color.Lerp(soil,gravel,grit*.65f);
            }
            texture.SetPixels32(pixels);texture.Apply(true,true);
            return texture;
        }
    }
}

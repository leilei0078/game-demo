using System;
using System.Linq;
using UnityEngine;

internal sealed class CarryClearanceCheck
{
    private readonly Mesh baked=new Mesh();
    private readonly SkinnedMeshRenderer skin;
    private readonly Transform bundle;
    private readonly Transform[] cylinders;
    private readonly int[] triangles;
    private readonly int[][] handVertices;
    private readonly Vector3[] clipped=new Vector3[8],scratch=new Vector3[8];
    public int Frames {get;private set;}
    public int DeepFrames {get;private set;}
    public float Worst {get;private set;}
    public float WidestHandGap {get;private set;}
    public string Region {get;private set;}
    public CarryClearanceCheck(Transform worker)
    {
        skin=worker.GetComponentInChildren<SkinnedMeshRenderer>();bundle=worker.Find("Cargo/Three pieces of firewood");
        cylinders=bundle.Cast<Transform>().ToArray();triangles=skin.sharedMesh.triangles;
        var weights=skin.sharedMesh.boneWeights;
        handVertices=new[]{" L "," R "}.Select(side=>Enumerable.Range(0,weights.Length).Where(index=>{var name=skin.bones[weights[index].boneIndex0].name;return name.Contains(side+"Hand")||name.Contains(side+"Finger");}).ToArray()).ToArray();
    }
    private static int Clip(Vector3[] input,int count,Vector3[] output,float plane,float sign)
    {
        int end=0;var previous=input[count-1];float before=(previous.x-plane)*sign;
        for(int index=0;index<count;index++){
            var next=input[index];float after=(next.x-plane)*sign;
            if((before>=0)!=(after>=0))output[end++]=Vector3.LerpUnclamped(previous,next,before/(before-after));
            if(after>=0)output[end++]=next;previous=next;before=after;
        }return end;
    }
    private bool Intersects(Vector3 first,Vector3 second,Vector3 third,float half,float radius,float depth)
    {
        half-=depth;radius-=depth;if(half<=0||radius<=0)return false;
        clipped[0]=first;clipped[1]=second;clipped[2]=third;
        int count=Clip(clipped,3,scratch,-half,1);if(count==0)return false;
        count=Clip(scratch,count,clipped,half,-1);if(count==0)return false;
        bool positive=false,negative=false;float area=0;var previous=clipped[count-1];
        for(int index=0;index<count;index++){
            var next=clipped[index];var start=new Vector2(previous.y,previous.z);var edge=new Vector2(next.y-previous.y,next.z-previous.z);
            var near=start-edge*Mathf.Clamp01(Vector2.Dot(start,edge)/Mathf.Max(1e-16f,edge.sqrMagnitude));
            if(near.sqrMagnitude<radius*radius)return true;
            float cross=previous.y*next.z-previous.z*next.y;positive|=cross>0;negative|=cross<0;area+=cross;previous=next;
        }return Mathf.Abs(area)>1e-12f&&!(positive&&negative);
    }
    public void Sample()
    {
        Frames++;skin.BakeMesh(baked);var vertices=baked.vertices;var matrix=bundle.worldToLocalMatrix*skin.transform.localToWorldMatrix;
        for(int index=0;index<vertices.Length;index++)vertices[index]=matrix.MultiplyPoint3x4(vertices[index]);
        foreach(var hand in handVertices){float closest=float.PositiveInfinity;foreach(int index in hand)foreach(var cylinder in cylinders){
            var point=vertices[index]-cylinder.localPosition;float axial=Mathf.Max(0,Mathf.Abs(point.x)-cylinder.localScale.y),radial=Mathf.Max(0,new Vector2(point.y,point.z).magnitude-cylinder.localScale.x*.5f);
            closest=Mathf.Min(closest,axial*axial+radial*radial);
        }WidestHandGap=Mathf.Max(WidestHandGap,Mathf.Sqrt(closest));}
        float maximum=0;
        foreach(var cylinder in cylinders){var center=cylinder.localPosition;float half=cylinder.localScale.y,radius=cylinder.localScale.x*.5f;
            for(int tri=0;tri<triangles.Length;tri+=3){
                var first=vertices[triangles[tri]]-center;var second=vertices[triangles[tri+1]]-center;var third=vertices[triangles[tri+2]]-center;
                if(Mathf.Min(first.y,Mathf.Min(second.y,third.y))>radius||Mathf.Max(first.y,Mathf.Max(second.y,third.y))< -radius||Mathf.Min(first.z,Mathf.Min(second.z,third.z))>radius||Mathf.Max(first.z,Mathf.Max(second.z,third.z))< -radius)continue;
                if(!Intersects(first,second,third,half,radius,maximum))continue;
                float low=maximum,high=Mathf.Min(half,radius);for(int iter=0;iter<9;iter++){float depth=(low+high)*.5f;if(Intersects(first,second,third,half,radius,depth))low=depth;else high=depth;}
                maximum=low;if(maximum>Worst){Worst=maximum;Region=skin.bones[skin.sharedMesh.boneWeights[triangles[tri]].boneIndex0].name;}
            }
        }if(maximum>.003f)DeepFrames++;
    }
}

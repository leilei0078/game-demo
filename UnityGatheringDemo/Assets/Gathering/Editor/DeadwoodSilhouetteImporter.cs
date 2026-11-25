using System.Linq;
using UnityEditor;
using UnityEngine;

internal sealed class DeadwoodSilhouetteImporter : AssetPostprocessor
{
    public override uint GetVersion()=>1;
    private void OnPostprocessModel(GameObject root)
    {
        if(assetPath!="Assets/Gathering/Resources/Deadwood/OldTree_long.fbx")return;
        var parts=root.GetComponentsInChildren<MeshFilter>();
        Matrix4x4 ToTree(MeshFilter part)=>root.transform.worldToLocalMatrix*part.transform.localToWorldMatrix;
        float top=parts.Max(part=>{var matrix=ToTree(part);return part.sharedMesh.vertices.Max(vertex=>matrix.MultiplyPoint3x4(vertex).y);});
        foreach(var part in parts)
        {
            var mesh=part.sharedMesh;var vertices=mesh.vertices;var normals=mesh.normals;var tangents=mesh.tangents;
            var toTree=ToTree(part);var fromTree=toTree.inverse;
            for(int index=0;index<vertices.Length;index++)
            {
                var point=toTree.MultiplyPoint3x4(vertices[index]);
                float progress=Mathf.Clamp01((point.y/top-.18f)/.44f);
                float width=Mathf.SmoothStep(1.16f,.86f,progress);
                float slope=(.86f-1.16f)*6*progress*(1-progress)/(.44f*top);
                var shape=Matrix4x4.Scale(new Vector3(width,1.35f,width));shape.m01=point.x*slope;shape.m21=point.z*slope;
                var localShape=fromTree*shape*toTree;
                vertices[index]=fromTree.MultiplyPoint3x4(new Vector3(point.x*width,point.y*1.35f,point.z*width));
                if(normals.Length!=0)normals[index]=localShape.inverse.transpose.MultiplyVector(normals[index]).normalized;
                if(tangents.Length!=0){var direction=localShape.MultiplyVector(tangents[index]).normalized;tangents[index]=new Vector4(direction.x,direction.y,direction.z,tangents[index].w);}
            }
            mesh.vertices=vertices;if(normals.Length!=0)mesh.normals=normals;if(tangents.Length!=0)mesh.tangents=tangents;mesh.RecalculateBounds();
        }
    }
}

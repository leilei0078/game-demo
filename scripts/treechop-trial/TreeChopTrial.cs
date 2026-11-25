using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GatheringDemo
{
    public sealed class TreeChopTrial : MonoBehaviour
    {
        [Serializable] private sealed class Clip {public float duration;public int count;public float[] data;}
        [Serializable] private sealed class Motion {public string[] names;public float[] rest;public Clip chop;}
        private Motion motion;
        private Transform[] joints;
        private Quaternion[] bind,local;
        private Vector3[] points;
        private Transform pivot,tool;
        private Vector3 origin,hip;
        private Quaternion orientation;
        private Material wood,iron;
        private Mesh head;
        private float clock;
        public Transform Edge {get;private set;}
        public float Cycle=>clock/motion.chop.duration;
        public void Setup(Transform visual,Material material)
        {
            pivot=visual;origin=pivot.localPosition;orientation=pivot.localRotation;
            motion=JsonUtility.FromJson<Motion>(Resources.Load<TextAsset>("Farmer/TreeChopTrial").text);
            var rig=pivot.GetComponentsInChildren<Transform>(true);
            joints=motion.names.Select(name=>name.Contains(":")?rig.Single(bone=>bone.name.EndsWith(name.Split(':')[1],StringComparison.Ordinal)):rig.Single(bone=>bone.name=="Char_Farmer_001 "+name)).ToArray();
            bind=joints.Select(bone=>Quaternion.Inverse(transform.rotation)*bone.rotation).ToArray();local=joints.Select(bone=>bone.localRotation).ToArray();
            hip=joints[0].localPosition;points=new Vector3[joints.Length];
            wood=new Material(material){color=new Color(.44f,.28f,.14f)};iron=new Material(material){color=new Color(.46f,.51f,.51f)};
            iron.SetFloat("_Metallic",.42f);
            tool=new GameObject("Chopping hatchet").transform;tool.SetParent(transform,false);
            var shaft=GameObject.CreatePrimitive(PrimitiveType.Cylinder);shaft.name="Ash handle";Destroy(shaft.GetComponent<Collider>());shaft.transform.SetParent(tool,false);
            shaft.transform.localPosition=Vector3.up*.19f;shaft.transform.localScale=new Vector3(.035f,.28f,.045f);shaft.GetComponent<Renderer>().sharedMaterial=wood;
            var shape=new[]{new Vector2(-.055f,.47f),new Vector2(.048f,.475f),new Vector2(.17f,.495f),new Vector2(.183f,.458f),new Vector2(.165f,.362f),new Vector2(.115f,.338f),new Vector2(.067f,.403f),new Vector2(-.055f,.414f)};
            var verts=new List<Vector3>();var faces=new List<int>();
            foreach(float side in new[]{-1f,1f})foreach(var point in shape)verts.Add(new Vector3(side*(point.x>.11f?.004f:.024f),point.y,point.x));
            int count=shape.Length;
            var triangles=new[]{0,1,7,1,6,7,1,2,3,1,3,6,3,4,6,4,5,6};
            for(int index=0;index<triangles.Length;index+=3){int first=triangles[index],second=triangles[index+1],third=triangles[index+2];faces.AddRange(new[]{first,third,second,count+first,count+second,count+third});}
            for(int index=0;index<count;index++){int next=(index+1)%count;faces.AddRange(new[]{index,next,next+count,index,next+count,index+count});}
            head=new Mesh{name="Trial hatchet head"};head.SetVertices(verts);head.SetTriangles(faces,0);head.RecalculateNormals();
            tool.gameObject.AddComponent<MeshFilter>().sharedMesh=head;tool.gameObject.AddComponent<MeshRenderer>().sharedMaterial=iron;
            Edge=new GameObject("Cutting edge").transform;Edge.SetParent(tool,false);Edge.localPosition=new Vector3(0,.43f,.175f);tool.gameObject.SetActive(false);
        }
        private static Vector3 Position(float[] values,int offset)=>new Vector3(values[offset],values[offset+1],values[offset+2]);
        private static Quaternion Rotation(float[] values,int offset)=>new Quaternion(values[offset+3],values[offset+4],values[offset+5],values[offset+6]);
        private void Aim(int from,int to){var bone=joints[from];bone.rotation=Quaternion.FromToRotation(joints[to].position-bone.position,points[to]-points[from])*bone.rotation;}
        public void Tick(GatheringWorker worker,float dt)
        {
            bool active=worker.Phase==WorkerPhase.Working&&worker.Source&&worker.Source.Work==WorkKind.Chop&&worker.BatchCount>0;
            if(!active){if(tool.gameObject.activeSelf){tool.gameObject.SetActive(false);pivot.localRotation=orientation;pivot.localPosition=origin;for(int index=22;index<joints.Length;index++)joints[index].localRotation=local[index];}clock=0;return;}
            var direction=Vector3.ProjectOnPlane(worker.Source.transform.position-transform.position,Vector3.up);var facing=Quaternion.LookRotation(direction);
            pivot.rotation=facing*orientation;pivot.position=transform.position+facing*(origin+new Vector3(.10f,0,.43f));
            tool.gameObject.SetActive(true);var clip=motion.chop;clock=Mathf.Repeat(clock+dt*.8f,clip.duration);float frame=clock*clip.count/clip.duration;
            int first=(int)frame,second=(first+1)%clip.count;float fraction=frame-first;
            joints[0].localPosition=hip;
            for(int index=0;index<joints.Length;index++){
                int start=(first*joints.Length+index)*7,end=(second*joints.Length+index)*7;
                points[index]=facing*Vector3.Lerp(Position(clip.data,start),Position(clip.data,end),fraction);
                joints[index].rotation=facing*Quaternion.Slerp(Rotation(clip.data,start),Rotation(clip.data,end),fraction)*Quaternion.Inverse(Rotation(motion.rest,index*7))*bind[index];
            }
            joints[0].position+=(points[0]-facing*Position(motion.rest,0))*.9f;
            foreach(int index in new[]{7,8,10,11,12,15,16,18,19,20})Aim(index,index+1);
            for(int side=0;side<2;side++){
                int hand=9+8*side,index=22+15*side,pinky=index+9;
                Quaternion Palm(Vector3 across,Vector3 along)=>Quaternion.LookRotation(along,Vector3.Cross(along,across));
                var actual=Palm(joints[index].position-joints[pinky].position,joints[index+3].position-joints[hand].position);
                var desired=Palm(points[index]-points[pinky],points[index+3]-points[hand]);joints[hand].rotation=desired*Quaternion.Inverse(actual)*joints[hand].rotation;
                for(int digit=index;digit<index+15;digit+=3){Aim(digit,digit+1);Aim(digit+1,digit+2);}
            }
            var grip=(joints[40].position+joints[42].position)*.5f;
            var across=(joints[37].position-joints[46].position).normalized;
            var along=(joints[40].position-joints[17].position).normalized;
            var axis=(across+along*.75f).normalized;var blade=Vector3.ProjectOnPlane(along,axis).normalized;
            tool.SetPositionAndRotation(grip,Quaternion.LookRotation(blade,axis));
        }
        private void OnDestroy(){Destroy(head);Destroy(wood);Destroy(iron);}
    }
}

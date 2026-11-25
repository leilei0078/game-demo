using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GatheringDemo
{
    internal sealed class FarmerPose
    {
        [Serializable] private sealed class Clip {public float duration;public int count;public float[] data;}
        [Serializable] private sealed class Motion {public string[] names;public float[] rest;public Clip walk,idle;}
        private readonly Motion motion;
        private readonly Transform root;
        private readonly Transform[] bones;
        private readonly Quaternion[] restWorld,restLocal;
        private readonly Vector3[] source;
        private readonly Vector3 neutralHip;
        private static readonly int[] Directed={7,8,10,11,12,15,16,18,19,20};
        private float walkTime,idleTime,blend,backLean;
        internal FarmerPose(Transform root,Dictionary<string,Transform> mapping)
        {
            this.root=root;
            motion=JsonUtility.FromJson<Motion>(Resources.Load<TextAsset>("Farmer/QuaterniusMotion").text);
            bones=motion.names.Select(name=>mapping["Char_Farmer_001 "+name]).ToArray();
            restWorld=bones.Select(bone=>Quaternion.Inverse(root.rotation)*bone.rotation).ToArray();restLocal=bones.Select(bone=>bone.localRotation).ToArray();
            source=new Vector3[bones.Length];neutralHip=bones[0].localPosition;
        }
        private static Vector3 Pos(float[] data,int offset)=>new Vector3(data[offset],data[offset+1],data[offset+2]);
        private static Quaternion Rot(float[] data,int offset)=>new Quaternion(data[offset+3],data[offset+4],data[offset+5],data[offset+6]);
        private void Sample(Clip clip,float time,int bone,out Vector3 point,out Quaternion rotation)
        {
            float frame=Mathf.Repeat(time,clip.duration)*clip.count/clip.duration;int index=Mathf.FloorToInt(frame);float fraction=frame-index;
            int first=(index*bones.Length+bone)*7,second=(((index+1)%clip.count)*bones.Length+bone)*7;
            point=Vector3.Lerp(Pos(clip.data,first),Pos(clip.data,second),fraction);rotation=Quaternion.Slerp(Rot(clip.data,first),Rot(clip.data,second),fraction);
        }
        internal void Tick(float distance,float dt,float targetLean)
        {
            walkTime=Mathf.Repeat(walkTime+distance*motion.walk.duration/1.60f,motion.walk.duration);idleTime=Mathf.Repeat(idleTime+dt,motion.idle.duration);
            blend=Mathf.MoveTowards(blend,Mathf.Clamp01(distance/Mathf.Max(.001f,dt)*1.5f),dt*7);
            bones[0].localPosition=neutralHip;
            for(int index=0;index<bones.Length;index++)bones[index].localRotation=restLocal[index];
            for(int index=0;index<bones.Length;index++){
                Sample(motion.walk,walkTime,index,out var walkPoint,out var walkRot);Sample(motion.idle,idleTime,index,out var idlePoint,out var idleRot);
                source[index]=Vector3.Lerp(idlePoint,walkPoint,blend);bones[index].rotation=root.rotation*Quaternion.Slerp(idleRot,walkRot,blend)*Quaternion.Inverse(Rot(motion.rest,index*7))*restWorld[index];
            }
            var travel=source[0]-Pos(motion.rest,0);bones[0].position+=root.TransformDirection(new Vector3(travel.x,travel.y,Mathf.Clamp(travel.z,-.05f,.05f))*.92f);
            foreach(int joint in Directed)Aim(bones[joint],bones[joint+1],root.TransformDirection(source[joint+1]-source[joint]));
            backLean=dt>0?Mathf.MoveTowards(backLean,targetLean,dt*38):targetLean;
            if(backLean>0)bones[2].rotation=Quaternion.AngleAxis(backLean,root.right)*bones[2].rotation;
        }
        private static void Aim(Transform start,Transform end,Vector3 direction){start.rotation=Quaternion.FromToRotation(end.position-start.position,direction)*start.rotation;}
    }
}

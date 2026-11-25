using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public sealed class FarmerMotionStudy
{
    [Serializable] private sealed class Clip { public string name; public float duration; public int count; public float[] data; }
    [Serializable] private sealed class Motion { public string[] names; public float[] rest; public Clip walk,idle; }
    [DllImport("FarmerOzz")] private static extern int FarmerMask(ref Quaternion lower,ref Quaternion upper,float weight,out Quaternion result);
    private readonly Motion motion;
    private readonly Transform root,bundle;
    private readonly Transform[] bones;
    private readonly Quaternion[] restWorld, restLocal, lower;
    private readonly Vector3[] source;
    private readonly Vector3 neutralHip;
    private readonly Quaternion[] palmAxes=new Quaternion[2];
    private readonly int mode;
    private float walkTime,idleTime,blend,carry;
    private readonly FeetGraph feet;
    public int LayersApplied {get;private set;}
    public int SurfaceHits {get;private set;}
    public float MaxCorrection {get;private set;}
    public float GripError {get;private set;}
    public int GripFrames {get;private set;}
    public FarmerMotionStudy(int mode,Transform root,Dictionary<string,Transform> mapping,Transform bundle,bool contactBoard)
    {
        this.mode=mode;this.root=root;this.bundle=bundle;
        motion=JsonUtility.FromJson<Motion>(Resources.Load<TextAsset>("QuaterniusMotion").text);
        bones=motion.names.Select(name=>mapping["Char_Farmer_001 "+name]).ToArray();
        restWorld=bones.Select(bone=>Quaternion.Inverse(root.rotation)*bone.rotation).ToArray();
        restLocal=bones.Select(bone=>bone.localRotation).ToArray(); lower=new Quaternion[bones.Length]; source=new Vector3[bones.Length];
        neutralHip=bones[0].localPosition;
        for(int side=0;side<2;side++){
            var hand=bones[9+side*8];string prefix="Char_Farmer_001 "+(side==0?"L ":"R ");
            Vector3 Knuckle(int digit)=>hand.InverseTransformPoint(mapping[prefix+"Finger"+digit].position);
            var forward=Knuckle(2).normalized;var across=Knuckle(1)-Knuckle(4);
            palmAxes[side]=Quaternion.LookRotation(forward,Vector3.Cross(forward,across)*(side==0?-1:1));
        }
        if(contactBoard){var board=GameObject.CreatePrimitive(PrimitiveType.Cube);board.name="Contact test - raised sloping timber";board.layer=26;
            board.transform.SetPositionAndRotation(new Vector3(-1.8f,.045f,.2f),Quaternion.Euler(0,0,4));board.transform.localScale=new Vector3(1.8f,.12f,6.2f);
            board.GetComponent<Renderer>().material.color=new Color(.64f,.52f,.34f);}
        if(mode==3)feet=new FeetGraph();
        Debug.Log($"MOTION_SOURCE {motion.walk.name} {motion.walk.count} poses; mode={mode}");
    }
    private static Vector3 Pos(float[] data,int offset)=>new Vector3(data[offset],data[offset+1],data[offset+2]);
    private static Quaternion Rot(float[] data,int offset)=>new Quaternion(data[offset+3],data[offset+4],data[offset+5],data[offset+6]);
    private void Sample(Clip clip,float time,int bone,out Vector3 point,out Quaternion rotation)
    {
        float frame=Mathf.Repeat(time,clip.duration)*clip.count/clip.duration;int index=Mathf.FloorToInt(frame);float fraction=frame-index;
        int first=(index*motion.names.Length+bone)*7,second=(((index+1)%clip.count)*motion.names.Length+bone)*7;
        point=Vector3.Lerp(Pos(clip.data,first),Pos(clip.data,second),fraction);
        rotation=Quaternion.Slerp(Rot(clip.data,first),Rot(clip.data,second),fraction);
    }
    private Vector3 Wrist(int side)=>bundle.TransformPoint(new Vector3((side==0?-1:1)*.22f,-.106f,-.127f));
    public void Tick(float distance,bool wood,float readiness)
    {
        float dt=Time.deltaTime; walkTime+=distance*motion.walk.duration/1.60f; idleTime+=dt;
        blend=Mathf.MoveTowards(blend,Mathf.Clamp01(distance/Mathf.Max(.001f,dt)*1.5f),dt*7);
        carry=Mathf.MoveTowards(carry,wood?1:readiness,dt*3.4f);
        bones[0].localPosition=neutralHip;
        for(int index=0;index<bones.Length;index++)bones[index].localRotation=restLocal[index];
        for(int index=0;index<bones.Length;index++)
        {
            Sample(motion.walk,walkTime,index,out var walkPoint,out var walkRot);Sample(motion.idle,idleTime,index,out var idlePoint,out var idleRot);
            source[index]=Vector3.Lerp(idlePoint,walkPoint,blend);
            var rotation=Quaternion.Slerp(idleRot,walkRot,blend)*Quaternion.Inverse(Rot(motion.rest,index*7));
            bones[index].rotation=root.rotation*rotation*restWorld[index];
        }
        var travel=source[0]-Pos(motion.rest,0);
        bones[0].position+=root.TransformDirection(new Vector3(travel.x,travel.y,Mathf.Clamp(travel.z,-.05f,.05f))*.92f);
        for(int side=0;side<2;side++)
        {
            int arm=7+side*8,thigh=10+side*8;
            foreach(int joint in new[]{arm,arm+1,thigh,thigh+1,thigh+2})
                Aim(bones[joint],bones[joint+1],root.TransformDirection(source[joint+1]-source[joint]));
        }
        if(mode>=2)
        {
            for(int index=0;index<bones.Length;index++)lower[index]=bones[index].localRotation;
            var torso=bones[3]; torso.rotation=Quaternion.AngleAxis(8,root.right)*torso.rotation;
            var chest=root.InverseTransformPoint(torso.position);
            bundle.position=root.TransformPoint(new Vector3(chest.x*.55f,chest.y-.24f,.385f+chest.z*.3f));
            for(int side=0;side<2;side++)
            {
                int arm=7+side*8; float sign=side==0?-1:1;
                Solve(bones[arm],bones[arm+1],bones[arm+2],Wrist(side),root.TransformDirection(new Vector3(sign*.60f,-1,-.45f)));
                var fingers=root.TransformDirection(new Vector3(-sign*.18f,.12f,1));
                bones[arm+2].rotation=Quaternion.LookRotation(fingers,root.up)*Quaternion.Inverse(palmAxes[side]);
            }
            for(int index=0;index<bones.Length;index++)
            {
                float mask=index==3||index>=7&&index<=9||index>=15&&index<=17?carry:0;
                var upper=bones[index].localRotation;
                if(FarmerMask(ref lower[index],ref upper,mask,out var result)==0)throw new InvalidOperationException("ozz rejected a mask");
                bones[index].localRotation=result.normalized;
            }
            LayersApplied++;
        }
        if(wood && carry>.99f){for(int side=0;side<2;side++)GripError+=Vector3.Distance(bones[9+side*8].position,mode>=2?Wrist(side):bundle.TransformPoint(new Vector3((side==0?-1:1)*.225f,-.064f,.03f)));GripFrames+=2;}
        if(feet!=null)
        {
            feet.Run(bones,root,distance,dt,out float correction,out bool raised); MaxCorrection=Mathf.Max(MaxCorrection,correction); if(raised)SurfaceHits++;
        }
    }
    internal static void Aim(Transform start,Transform end,Vector3 direction){start.rotation=Quaternion.FromToRotation(end.position-start.position,direction)*start.rotation;}
    internal static void Solve(Transform start,Transform mid,Transform end,Vector3 goal,Vector3 bend)
    {
        var ray=(goal-start.position).normalized;float upper=Vector3.Distance(start.position,mid.position),lower=Vector3.Distance(mid.position,end.position);
        float reach=Mathf.Clamp(Vector3.Distance(start.position,goal),Mathf.Abs(upper-lower)+.001f,upper+lower-.001f);
        float along=(upper*upper-lower*lower+reach*reach)/(2*reach);
        Aim(start,mid,ray*along+Vector3.ProjectOnPlane(bend,ray).normalized*Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along)));
        Aim(mid,end,goal-mid.position);
    }
    public void Dispose(){feet?.Dispose();}

    private sealed class FeetGraph
    {
        private struct Seed : IAnimationJob
        {
            public TransformStreamHandle left,right,leftKnee,rightKnee;
            public Vector3 leftPos,rightPos,leftHint,rightHint;
            public Quaternion leftRot,rightRot;
            public void ProcessRootMotion(AnimationStream stream){}
            public void ProcessAnimation(AnimationStream stream){left.SetPosition(stream,leftPos);right.SetPosition(stream,rightPos);left.SetRotation(stream,leftRot);right.SetRotation(stream,rightRot);leftKnee.SetPosition(stream,leftHint);rightKnee.SetPosition(stream,rightHint);}
        }
        private PlayableGraph graph;
        private AnimationScriptPlayable input,output;
        private readonly List<GCHandle> buffers=new List<GCHandle>();
        private Transform[] joints=new Transform[5];
        public FeetGraph()
        {
            var host=new GameObject("IKFootPlacement targets");var animator=host.AddComponent<Animator>();animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            for(int index=0;index<joints.Length;index++){joints[index]=new GameObject("Target"+index).transform;joints[index].SetParent(host.transform,false);}
            animator.avatar=AvatarBuilder.BuildGenericAvatar(host,"");animator.Rebind();
            var handles=joints.Select(joint=>animator.BindStreamTransform(joint)).ToArray();
            graph=PlayableGraph.Create("Upstream foot placement"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var seed=new Seed{left=handles[0],right=handles[1],leftKnee=handles[2],rightKnee=handles[3]};
            var job=new IKFootPlacementJob();job.Create(2);job.targets[0]=handles[0];job.targets[1]=handles[1];job.hints[0]=handles[2];job.hints[1]=handles[3];job.hips=handles[4];
            job.targetPositionOffsetWeight=1;job.targetRotationOffsetWeight=.8f;job.feetPositionOffsetSmoothing=.05f;job.feetRotationOffsetSmoothing=.08f;
            foreach(var field in typeof(IKFootPlacementJob).GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public))
                if(field.GetValue(job) is Array buffer)buffers.Add(GCHandle.Alloc(buffer,GCHandleType.Pinned));
            input=AnimationScriptPlayable.Create(graph,seed);output=AnimationScriptPlayable.Create(graph,job,1);
            graph.Connect(input,0,output,0);output.SetInputWeight(0,1);AnimationPlayableOutput.Create(graph,"Feet",animator).SetSourcePlayable(output);graph.Play();
        }
        public void Run(Transform[] bones,Transform root,float distance,float dt,out float correction,out bool raised)
        {
            var seed=input.GetJobData<Seed>();seed.leftPos=bones[12].position;seed.rightPos=bones[20].position;seed.leftRot=bones[12].rotation;seed.rightRot=bones[20].rotation;seed.leftHint=bones[11].position;seed.rightHint=bones[19].position;input.SetJobData(seed);
            var job=output.GetJobData<IKFootPlacementJob>();job.rootPosition=new Vector3(root.position.x,0,root.position.z);job.deltaTime=dt;job.isMoving=distance>.002f;raised=false;
            for(int side=0;side<2;side++){var ankle=bones[12+side*8].position;var origin=new Vector3(ankle.x,.7f,ankle.z);if(!Physics.Raycast(origin,Vector3.down,out var hit,1.2f,(1<<8)|(1<<26)))throw new InvalidOperationException("Missing foot surface");job.legRaycastOrigin[side]=origin;job.legRaycastHitPoint[side]=hit.point;job.legRaycastHitNormal[side]=hit.normal;raised|=hit.collider.gameObject.layer==26;}
            output.SetJobData(job);graph.Evaluate(dt);correction=0;
            for(int side=0;side<2;side++){int leg=10+side*8;var ankle=bones[leg+2];var target=joints[side];correction=Mathf.Max(correction,Mathf.Abs(target.position.y-ankle.position.y));Solve(bones[leg],bones[leg+1],ankle,target.position,bones[leg+1].position-(bones[leg].position+ankle.position)*.5f+root.forward*.08f);ankle.rotation=target.rotation;}
        }
        public void Dispose(){if(graph.IsValid())graph.Destroy();foreach(var handle in buffers)handle.Free();}
    }
}

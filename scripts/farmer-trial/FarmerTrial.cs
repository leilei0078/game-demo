using System;
using System.Collections;
using System.Linq;
using GatheringDemo;
using UnityEngine;

public sealed class FarmerTrial : MonoBehaviour
{
    public GameObject model;
    public Material hair, face, clothing;
    public int studyMode;
    public bool contactBoard;
    public FarmerMotionStudy Study {get; private set;}
    public GatheringWorker Worker { get; private set; }
    public bool WoodVisible => bundle && bundle.activeInHierarchy;
    public float Height { get; private set; }
    private Transform rig;
    private GameObject bundle;
    private Renderer cargoMarker;
    private GatheringWorld world;
    private readonly Limb[] limbs = new Limb[2];
    private float stride, motion;
    private Vector3 lastPosition;

    private sealed class Limb
    {
        public Transform arm, elbow, hand, thigh, shin;
        public Quaternion armRest, elbowRest, thighRest, shinRest;
    }

    private IEnumerator Start()
    {
        world = GetComponent<GatheringWorld>();
        while (!world.Ready || world.RoutesSettling) yield return null;
        Worker = world.Workers.First(worker => worker.Id == 1);
        var root = Worker.transform;
        rig = new GameObject("Farmer visual pivot").transform; rig.SetParent(root,false);
        var instance = Instantiate(model, rig, false);
        if (instance.TryGetComponent<Animator>(out var animator)) animator.enabled = false;
        var skin = rig.GetComponentInChildren<SkinnedMeshRenderer>();
        skin.updateWhenOffscreen = true;
        skin.sharedMaterials = skin.sharedMaterials.Select(material =>
            material.name == "Hair" ? hair : material.name == "Head" ? face :
            material.name.Contains("Cloths") ? clothing : throw new InvalidOperationException(material.name)).ToArray();
        var bones = skin.bones.ToDictionary(bone => bone.name);
        Transform Bone(string suffix) => bones["Char_Farmer_001 " + suffix];
        var front = root.InverseTransformDirection(Bone("L Toe0").position - Bone("L Foot").position);
        rig.localRotation = Quaternion.AngleAxis(-Mathf.Atan2(front.x, front.z) * Mathf.Rad2Deg, Vector3.up) * rig.localRotation;
        Bounds BoundsInWorker()
        {
            var mesh = new Mesh(); skin.BakeMesh(mesh);
            var matrix = root.worldToLocalMatrix * skin.transform.localToWorldMatrix;
            var vertices = mesh.vertices; var bounds = new Bounds(matrix.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            foreach (var vertex in vertices) bounds.Encapsulate(matrix.MultiplyPoint3x4(vertex));
            Destroy(mesh); return bounds;
        }
        var fitted = BoundsInWorker(); float scale = 1.72f / fitted.size.y;
        rig.localScale = Vector3.one * scale; Height = fitted.size.y * scale;
        var pelvis = root.InverseTransformPoint(Bone("Pelvis").position);
        rig.localPosition -= new Vector3(pelvis.x, fitted.min.y * scale + root.position.y, pelvis.z);
        for (int index = 0; index < 2; index++)
        {
            string side = index == 0 ? "L " : "R ";
            var limb = new Limb { arm = Bone(side+"UpperArm"), elbow = Bone(side+"Forearm"), hand = Bone(side+"Hand"), thigh = Bone(side+"Thigh"), shin = Bone(side+"Calf") };
            limb.armRest = limb.arm.localRotation; limb.elbowRest = limb.elbow.localRotation;
            limb.thighRest = limb.thigh.localRotation; limb.shinRest = limb.shin.localRotation; limbs[index] = limb;
        }
        root.Find("Body").GetComponent<Renderer>().enabled = false;
        root.Find("Head").GetComponent<Renderer>().enabled = false;
        var cargo = root.Find("Cargo"); cargoMarker = cargo.GetComponent<Renderer>();
        bundle = new GameObject("Three pieces of firewood"); bundle.transform.SetParent(cargo, false);
        bundle.transform.localScale = Vector3.one / cargo.localScale.x;
        bundle.transform.position = root.TransformPoint(new Vector3(0, .99f, .30f));
        var bark = new Material(Shader.Find("Standard")) {color = new Color(.34f,.18f,.09f)};
        var cut = new Material(bark) {color = new Color(.94f,.72f,.46f)};
        for (int index = 0; index < 3; index++)
        {
            var center = new Vector3(index * .018f, index == 2 ? .105f : 0, index == 0 ? -.07f : index == 1 ? .07f : 0);
            float length = .82f - index * .06f;
            Cylinder(center, length, .15f, bark);
            foreach (float side in new[]{-1f,1f}) Cylinder(center + Vector3.right * side * length * .5f, .008f, .142f, cut);
        }
        void Cylinder(Vector3 center, float length, float diameter, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cylinder); part.transform.SetParent(bundle.transform,false);
            part.transform.localPosition = center; part.transform.localRotation = Quaternion.Euler(0,0,90);
            part.transform.localScale = new Vector3(diameter,length*.5f,diameter); part.GetComponent<Renderer>().sharedMaterial = material;
            var collider = part.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
        }
        lastPosition = root.position;
        if (studyMode>0) Study=new FarmerMotionStudy(studyMode,root,bones,bundle.transform,contactBoard);
        var order = Worker.Work(world.Resources.First(source => source.Work == WorkKind.Chop), once:true);
        if (!order.Accepted) throw new InvalidOperationException(order.Reason);
    }

    private void LateUpdate()
    {
        if (!bundle || world.Paused) return;
        var root = Worker.transform; float distance = Vector3.Distance(root.position,lastPosition); lastPosition = root.position;
        motion = Mathf.MoveTowards(motion, Mathf.Clamp01(distance / Mathf.Max(.001f,Time.deltaTime)), Time.deltaTime*8);
        stride += distance * 5.1f;
        bool wood = Worker.CargoCount > 0 && Worker.CargoKind == ResourceKind.Wood;
        bundle.SetActive(wood); cargoMarker.enabled = !wood;
        if (Study!=null) {float readiness=Worker.Phase==WorkerPhase.Working && Worker.Source && Worker.Source.Kind==ResourceKind.Wood ? Mathf.InverseLerp(.75f,.96f,Worker.Progress):0; Study.Tick(distance,wood,readiness); return; }
        for (int index = 0; index < limbs.Length; index++)
        {
            var limb = limbs[index]; float swing = Mathf.Sin(stride + index * Mathf.PI) * motion;
            limb.thigh.localRotation = limb.thighRest; limb.shin.localRotation = limb.shinRest;
            limb.thigh.rotation = Quaternion.AngleAxis(-swing * 25,root.right) * limb.thigh.rotation;
            limb.shin.rotation = Quaternion.AngleAxis(Mathf.Max(0,swing)*29,root.right) * limb.shin.rotation;
            limb.arm.localRotation = limb.armRest; limb.elbow.localRotation = limb.elbowRest;
            float side = Mathf.Sign(root.InverseTransformPoint(limb.arm.position).x);
            void Aim(Transform start, Transform end, Vector3 direction)
            { start.rotation = Quaternion.FromToRotation(end.position-start.position,root.TransformDirection(direction)) * start.rotation; }
            if (wood)
            {
                var target = bundle.transform.TransformPoint(new Vector3(side*.23f,-.07f,.025f));
                var ray = (target-limb.arm.position).normalized;
                float upper = Vector3.Distance(limb.arm.position,limb.elbow.position), lower = Vector3.Distance(limb.elbow.position,limb.hand.position);
                float reach = Mathf.Clamp(Vector3.Distance(target,limb.arm.position),Mathf.Abs(upper-lower)+.001f,upper+lower-.001f);
                float along = (upper*upper-lower*lower+reach*reach)/(2*reach);
                var bend = Vector3.ProjectOnPlane(root.TransformDirection(new Vector3(side*.6f,-1,-.35f)),ray).normalized;
                var elbow = limb.arm.position+ray*along+bend*Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
                Aim(limb.arm,limb.elbow,root.InverseTransformDirection(elbow-limb.arm.position));
                Aim(limb.elbow,limb.hand,root.InverseTransformDirection(target-limb.elbow.position));
            }
            else
            {
                Aim(limb.arm,limb.elbow,new Vector3(side * .13f,-1,-swing * .44f));
                Aim(limb.elbow,limb.hand,new Vector3(-side * .13f,-1,.26f-swing*.38f));
            }
        }
    }
    private void OnDestroy() { Study?.Dispose(); }
}

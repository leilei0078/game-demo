using System.Linq;
using UnityEngine;

namespace GatheringDemo
{
    public sealed class FarmerAppearance : MonoBehaviour
    {
        private GatheringWorld world;
        private GatheringWorker worker;
        private FirewoodFrame firewood;
        private GameObject basket,stones;
        private Renderer cargoMarker;
        private FarmerPose pose;
        private Vector3 lastPosition;
        private bool loaded;
        internal static void Attach(GatheringWorld world,GatheringWorker worker,GameObject cargo)
        {
            var survivor=Resources.Load<GameObject>("Survivor/Survivor_LightOlive");
            var model=survivor ? survivor : Resources.Load<GameObject>("Farmer/Famer_001_A");
            if(!model)return;
            var visual=worker.gameObject.AddComponent<FarmerAppearance>();
            visual.world=world;visual.worker=worker;visual.Setup(model,cargo,survivor!=null);
        }
        private void Setup(GameObject model,GameObject cargo,bool survivor)
        {
            var pivot=new GameObject("Farmer visual").transform;pivot.SetParent(transform,false);
            var instance=Instantiate(model,pivot,false);
            if(instance.TryGetComponent<Animator>(out var animator))animator.enabled=false;
            var skin=instance.GetComponentInChildren<SkinnedMeshRenderer>();skin.updateWhenOffscreen=true;
            var bones=survivor
                ? instance.GetComponentsInChildren<Transform>(true).Where(bone=>bone.name.StartsWith("Char_Farmer_001 ",System.StringComparison.Ordinal)).ToDictionary(bone=>bone.name)
                : skin.bones.ToDictionary(bone=>bone.name);
            if(!survivor)skin.sharedMaterials=skin.sharedMaterials.Select(slot=>Resources.Load<Material>("Farmer/"+(slot.name=="Hair"?"HairCutout":slot.name=="Head"?"Skin":"Workwear"))).ToArray();
            var front=transform.InverseTransformDirection(bones["Char_Farmer_001 L Toe0"].position-bones["Char_Farmer_001 L Foot"].position);
            pivot.localRotation=Quaternion.AngleAxis(-Mathf.Atan2(front.x,front.z)*Mathf.Rad2Deg,Vector3.up);
            var mesh=new Mesh();skin.BakeMesh(mesh);
            // Bake the FBX hierarchy scale once; its source rig can carry a 200x unit conversion.
            var matrix=transform.worldToLocalMatrix*(survivor ? Matrix4x4.TRS(skin.transform.position,skin.transform.rotation,Vector3.one) : skin.transform.localToWorldMatrix);
            var heights=mesh.vertices.Select(vertex=>matrix.MultiplyPoint3x4(vertex).y).ToArray();Destroy(mesh);
            float floor=heights.Min(),scale=1.72f/(heights.Max()-floor);pivot.localScale=Vector3.one*scale;
            var pelvis=transform.InverseTransformPoint(bones["Char_Farmer_001 Pelvis"].position);
            pivot.localPosition-=new Vector3(pelvis.x,floor*scale+transform.position.y,pelvis.z);
            cargoMarker=cargo.GetComponent<Renderer>();
            var spine=bones["Char_Farmer_001 Spine3"];
            firewood=FirewoodFrame.Attach(transform,spine,cargoMarker.sharedMaterial);
            var carrier=Resources.Load<GameObject>("WorkerGear/MarketP019/P019_BackBasket");
            if(!carrier)carrier=Resources.Load<GameObject>("WorkerGear/OrePannier");
            if(carrier){
                basket=Instantiate(carrier,spine.position+transform.rotation*new Vector3(0,-.38f,-.24f),transform.rotation);
                basket.name="Ore basket";basket.transform.SetParent(spine,true);
                stones=basket.transform.Find("Stone load").gameObject;
                basket.SetActive(false);
            }
            pose=new FarmerPose(transform,bones);lastPosition=transform.position;
            transform.Find("Body").GetComponent<Renderer>().enabled=false;transform.Find("Head").GetComponent<Renderer>().enabled=false;
        }
        private void LateUpdate()
        {
            if(pose==null)return;
            bool wood=worker.CargoCount>0&&worker.CargoKind==ResourceKind.Wood;
            bool ore=basket&&worker.CargoCount>0&&worker.CargoKind==ResourceKind.Ore;
            bool emptyFrame=worker.CargoCount==0&&worker.Source&&worker.Source.Kind==ResourceKind.Wood&&
                (worker.Phase==WorkerPhase.WaitingWork||worker.Phase==WorkerPhase.ToWork||worker.Phase==WorkerPhase.Working||worker.Phase==WorkerPhase.Blocked);
            bool emptyBasket=basket&&worker.CargoCount==0&&worker.Source&&worker.Source.Kind==ResourceKind.Ore&&
                (worker.Phase==WorkerPhase.Working||worker.Phase==WorkerPhase.ToWork);
            if(basket){basket.SetActive(ore||emptyBasket);stones.SetActive(ore);}
            firewood.Show(wood||emptyFrame,wood);cargoMarker.enabled=!wood&&!ore;
            bool changed=loaded!=(wood||ore);loaded=wood||ore;
            float distance=Vector3.Distance(lastPosition,transform.position);lastPosition=transform.position;
            if(world.Paused&&!changed)return;
            pose.Tick(distance>.6f?0:distance,world.Paused?0:Time.deltaTime,wood?10:ore?7:0);
        }
    }
}

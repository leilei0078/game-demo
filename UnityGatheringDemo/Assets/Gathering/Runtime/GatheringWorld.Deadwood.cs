using System;
using System.Collections.Generic;
using UnityEngine;

namespace GatheringDemo
{
    public sealed partial class GatheringWorld
    {
        private const string DeadwoodPath="Deadwood/";
        private readonly Dictionary<string,Material> deadwoodMaterials=new Dictionary<string,Material>();

        private void AttachDeadwood(GameObject visual,Vector3 position)
        {
            var source=UnityEngine.Resources.Load<GameObject>(DeadwoodPath+"OldTree_long");
            var shader=UnityEngine.Resources.Load<Shader>(DeadwoodPath+"Deadwood");
            if(!source||!shader||!shader.isSupported)return;

            var tree=Instantiate(source,visual.transform.parent);
            tree.name="Deadwood";
            bool east=position.x>5;
            tree.transform.SetPositionAndRotation(position,Quaternion.Euler(0,east?38:151,0));
            tree.transform.localScale=Vector3.one*(east?.40f:.48f);
            var renderers=tree.GetComponentsInChildren<MeshRenderer>();
            foreach(var renderer in renderers)
            {
                string part=renderer.name.Substring(renderer.name.LastIndexOf('_')+1);
                if(!deadwoodMaterials.TryGetValue(part,out var material))
                {
                    var texture=UnityEngine.Resources.Load<Texture2D>(DeadwoodPath+"OldTree_"+part+"_BC");
                    if(!texture){Destroy(tree);return;}
                    bool twigs=string.Equals(part,"Twigs",StringComparison.Ordinal);
                    material=new Material(shader){name="Deadwood "+part,mainTexture=texture};
                    material.SetFloat("_Cutoff",twigs?.2151264f:0);
                    material.SetFloat("_Cull",twigs?0:2);
                    deadwoodMaterials.Add(part,material);materials.Add(material);
                }
                renderer.sharedMaterial=material;
            }
            if(renderers.Length==0){Destroy(tree);return;}
            tree.transform.SetParent(visual.transform,true);
            visual.GetComponent<Renderer>().enabled=false;
        }
    }
}

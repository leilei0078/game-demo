using GatheringDemo;
using UnityEngine;

[RequireComponent(typeof(GatheringWorld))]
public sealed class MapShadowProfile : MonoBehaviour
{
    private void Start()
    {
        Light sun=null;
        foreach(var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if(light.gameObject.scene==gameObject.scene && light.type==LightType.Directional && light.name=="Sun"){sun=light;break;}
        if(!sun || sun.shadows==LightShadows.None || QualitySettings.shadows==ShadowQuality.Disable)return;
        var view=GetComponent<GatheringWorld>().View;
        view.farClipPlane=Mathf.Min(view.farClipPlane,96f);
        sun.shadowCustomResolution=2048;
        sun.shadowBias=.16f;
        sun.shadowNormalBias=.45f;
        var ground=transform.Find("Walkable Ground");
        if(ground && ground.TryGetComponent<Renderer>(out var receiver))receiver.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}

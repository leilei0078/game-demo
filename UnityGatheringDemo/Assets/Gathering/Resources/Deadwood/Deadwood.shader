Shader "Gathering/Deadwood"
{
    Properties
    {
        _MainTex ("Bark", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull [_Cull]
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard alphatest:_Cutoff addshadow fullforwardshadows

        sampler2D _MainTex;
        struct Input { float2 uv_MainTex; };

        void surf(Input input, inout SurfaceOutputStandard output)
        {
            fixed4 bark = tex2D(_MainTex, input.uv_MainTex);
            half value = dot(bark.rgb, half3(.2126, .7152, .0722));
            output.Albedo = lerp(half3(value, value, value), bark.rgb, .17) * half3(.99, 1, 1.022);
            output.Alpha = bark.a;
            output.Smoothness = .13;
        }
        ENDCG
    }
}

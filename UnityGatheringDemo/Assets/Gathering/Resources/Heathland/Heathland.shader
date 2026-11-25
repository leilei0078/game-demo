Shader "Gathering/Heathland"
{
    Properties
    {
        _MainTex ("Scan albedo", 2D) = "white" {}
        _BaseNormal ("Scan normal RGB", 2D) = "bump" {}
        _GrassTex ("Dry grass scan", 2D) = "white" {}
        _GrassNormal ("Dry grass normal RGB", 2D) = "bump" {}
        _SandTex ("Gravel scan", 2D) = "white" {}
        _SandNormal ("Gravel normal RGB", 2D) = "bump" {}
        _MaskTex ("Ground coverage", 2D) = "white" {}
        _RockArm ("Rock occlusion and roughness", 2D) = "white" {}
        _Occupied ("Decoration clearance", 2D) = "black" {}
        _Map ("Map min XZ and size XZ", Vector) = (-18,-13,36,26)
        _Palette ("Cool tint RGB and original saturation", Vector) = (0.91,1,1.09,0.14)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma multi_compile_local _ HEATH_GRASS HEATH_ROCK

        sampler2D _MainTex, _BaseNormal, _GrassTex, _GrassNormal, _SandTex, _SandNormal, _MaskTex, _RockArm, _Occupied;
        float4 _Map, _Palette;

        struct Input { float3 worldPos; float2 uv_MainTex; float4 color : COLOR; };

        float3 ScanNormal(sampler2D source, float2 uv, float strength)
        {
            float3 decoded = tex2D(source, uv).xyz * 2 - 1;
            return normalize(float3(decoded.xy * strength, max(decoded.z, .15)));
        }

        float3 CoolAlbedo(float3 source)
        {
            float luminance = dot(source, float3(.2126, .7152, .0722));
            return lerp(luminance.xxx, source, _Palette.w) * _Palette.rgb;
        }

        void surf(Input IN, inout SurfaceOutputStandard output)
        {
            #if HEATH_GRASS
                clip(.5 - tex2D(_Occupied, (IN.worldPos.xz - _Map.xy) / _Map.zw).r);
                output.Albedo = lerp(CoolAlbedo(IN.color.rgb), IN.color.rgb, .21) * .92;
                output.Occlusion = IN.color.a;
                output.Smoothness = .1;
                output.Normal = float3(0,0,1);
            #elif HEATH_ROCK
                float2 uv = IN.uv_MainTex;
                half2 arm = tex2D(_RockArm, uv).rg;
                output.Albedo = CoolAlbedo(tex2D(_MainTex, uv).rgb);
                output.Normal = ScanNormal(_BaseNormal, uv, .93);
                output.Occlusion = lerp(.58, 1, arm.r);
                output.Smoothness = (1 - arm.g) * .45;
            #else
                float2 world = IN.worldPos.xz;
                if (IN.worldPos.y < -.0005)
                {
                    float2 edgeUV = float2(world.x + world.y, IN.worldPos.y) / 3.65;
                    output.Albedo = CoolAlbedo(tex2D(_MainTex, edgeUV).rgb) * .7;
                    output.Normal = float3(0,0,1);
                    output.Smoothness = .06;
                    return;
                }
                float3 mask = tex2D(_MaskTex, (world - _Map.xy) / _Map.zw).rgb;
                float2 baseUV = world / 3.65;
                float2 grassUV = world / 3.4 + float2(.37,.84);
                float2 sandUV = world / 2.85 + float2(.49,.21);
                half3 soil = tex2D(_MainTex, baseUV).rgb * half3(.77,.79,.76);
                half3 straw = tex2D(_GrassTex, grassUV).rgb * half3(.94,.90,.82);
                half3 gravel = tex2D(_SandTex, sandUV).rgb * half3(.78,.79,.76);
                half3 combined = lerp(lerp(soil, gravel, mask.g * .7), straw, mask.r);
                output.Albedo = CoolAlbedo(combined) * lerp(.69, 1.12, mask.b);
                float3 soilNormal = lerp(ScanNormal(_BaseNormal, baseUV, .7), ScanNormal(_SandNormal, sandUV, .57), mask.g * .7);
                output.Normal = normalize(lerp(soilNormal, ScanNormal(_GrassNormal, grassUV, .62), mask.r));
                output.Occlusion = lerp(.75, 1, mask.b);
                output.Smoothness = .065;
            #endif
        }
        ENDCG
    }
    FallBack Off
}

// Flat-colour shader for the guide character and its panel (Assets/Scripts/MuseumGuide.cs).
//
// Created at runtime, so it cannot rely on a scene material for its shader, and it should
// not need lights (it is a cheap unlit look with a fixed fake light so shapes still read).
// In a Resources folder so it is always included in the build.
Shader "Museum/Solid"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Emit  ("Glow (added)", Color) = (0,0,0,0)
        _Shade ("Fake shading (0 = flat)", Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "MuseumSolid"
            Tags { "LightMode"="UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _Emit;
                half  _Shade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3  normalWS   : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                half3 n    = normalize(IN.normalWS);
                half  lamb = saturate(dot(n, normalize(half3(0.4, 0.8, 0.5))));
                half  lit  = lerp(1.0h, 0.45h + 0.65h * lamb, _Shade);
                return half4(_Color.rgb * lit + _Emit.rgb, 1.0h);
            }
            ENDHLSL
        }
    }
}

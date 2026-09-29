// Text shader for the museum's TextMesh objects (placards, prompts, debug readout).
//
// Unity's built-in font material ("GUI/Text Shader") hard-codes ZTest Always, which draws
// text on top of everything - so exhibit names show through walls and closed doors.
// This one is identical except that it depth-tests, so walls hide the text.
//
// Lives in a Resources folder so it is always included in the build and can be loaded
// with Resources.Load<Shader>("MuseumDepthText") (see Assets/Scripts/MuseumText.cs).
Shader "Museum/DepthText"
{
    Properties
    {
        _MainTex ("Font Texture", 2D) = "white" {}
        _Color   ("Text Color", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4   // 4 = LessEqual, 8 = Always
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "MuseumText"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color      = IN.color * _Color;
                OUT.uv         = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // Font atlases store the glyph shape in the alpha channel.
                half glyph = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a;
                return half4(IN.color.rgb, IN.color.a * glyph);
            }
            ENDHLSL
        }
    }
}

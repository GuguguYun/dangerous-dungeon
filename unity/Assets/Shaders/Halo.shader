// A soft round glow that always faces the camera (pickup halos). Drawn on a 1 x 1 quad centred on the
// object; the object's X scale is the halo's diameter. Additive, steady (no pulsing).
Shader "Meshy/Halo"
{
    Properties
    {
        _Color ("Colour (HDR)", Color) = (0.3, 0.6, 1, 1)
        _Falloff ("Falloff", Float) = 6
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "DisableBatching" = "True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Falloff;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 p : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 origin = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                float size = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
                float3 centreVS = TransformWorldToView(origin);
                float3 p = centreVS + float3(i.positionOS.xy * size, 0.0);
                o.positionCS = TransformWViewToHClip(p);
                o.p = i.uv * 2.0 - 1.0;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r2 = dot(i.p, i.p);
                float a = exp(-r2 * _Falloff) * saturate(1.0 - r2);
                return half4(_Color.rgb * a, 1.0);
            }
            ENDHLSL
        }
    }
}

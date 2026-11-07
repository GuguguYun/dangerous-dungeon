// An additive force-field: bright at the silhouette (fresnel), nearly clear in the middle, with slow
// moving bands. Used for the knight's shield, elite shields and the assassin's stealth outline.
Shader "Meshy/Fresnel"
{
    Properties
    {
        _Color ("Color", Color) = (0.3, 0.8, 1, 1)
        _Power ("Rim power", Float) = 2.5
        _Inner ("Inner glow", Float) = 0.06
        _Bands ("Band strength", Float) = 0.6
        _Speed ("Band speed", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Back
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Power;
                half _Inner;
                half _Bands;
                half _Speed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; float3 positionWS : TEXCOORD2; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS = GetWorldSpaceViewDir(p.positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(i.viewWS);
                float rim = pow(1.0 - saturate(abs(dot(n, v))), _Power);
                float t = _Time.y * _Speed;
                float bands = 0.5 + 0.5 * sin(i.positionWS.y * 12.0 + t * 4.0) * sin(i.positionWS.x * 7.0 + i.positionWS.z * 7.0 - t * 3.0);
                float a = rim * (1.0 + _Bands * bands) + _Inner;
                return half4(_Color.rgb * _Color.a * a, 1.0);
            }
            ENDHLSL
        }
    }
}

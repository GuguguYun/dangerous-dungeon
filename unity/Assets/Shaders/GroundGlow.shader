// A glow on the floor under loot and pickups: a soft pool of light, a crisp ring and (for rarer loot)
// an outer ring of dashes turning slowly. Drawn on a flat quad (LootFx.Quad), additive, so it lights
// the stone without hiding it. It breathes very gently (~0.3 Hz, a few percent).
Shader "Meshy/GroundGlow"
{
    Properties
    {
        _Color ("Colour (HDR)", Color) = (1, 0.7, 0.3, 1)
        _Pool ("Light pool strength", Float) = 0.6
        _PoolFalloff ("Light pool falloff", Float) = 5
        _Ring ("Ring strength", Float) = 1.2
        _RingRadius ("Ring radius (0..1 of the quad)", Float) = 0.62
        _RingWidth ("Ring width", Float) = 0.035
        _Dashes ("Outer dash strength", Float) = 0
        _DashCount ("Dash count", Float) = 12
        _Spin ("Dash turn speed (rad/s)", Float) = 0.25
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off
        Offset -1, -1
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Pool;
                half _PoolFalloff;
                half _Ring;
                half _RingRadius;
                half _RingWidth;
                half _Dashes;
                half _DashCount;
                half _Spin;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 p : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.p = i.uv * 2.0 - 1.0;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r = length(i.p);
                float t = _Time.y;
                float pool = exp(-r * r * _PoolFalloff) * _Pool;
                float aa = fwidth(r);
                float w = _RingWidth + aa;
                float rk = (r - _RingRadius) / w;
                float ring = exp(-rk * rk) * _Ring;
                // Outer dashes, turning slowly: a soft-edged square wave around the circle.
                float ang = atan2(i.p.y, i.p.x) + t * _Spin;
                float dash = smoothstep(0.15, 0.45, sin(ang * _DashCount) * 0.5 + 0.5);
                float outerR = _RingRadius + 0.16;
                float dk = (r - outerR) / (w * 0.8);
                float dashes = exp(-dk * dk) * dash * _Dashes;
                float edge = saturate((1.0 - r) * 5.0);
                float breathe = 1.0 + 0.06 * sin(t * 1.9);
                return half4(_Color.rgb * (pool + ring + dashes) * edge * breathe, 1.0);
            }
            ENDHLSL
        }
    }
}

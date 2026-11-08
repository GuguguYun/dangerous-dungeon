// The assassin's 鸦影遁: the body becomes a dark violet shadow you can half see through, with a faint
// glowing edge and slow smoke drifting up through it (PlayerSkill swaps it onto the body's renderers).
// A depth-only pass goes first so the shadow is one even layer (an arm in front of the chest does not
// stack darker). Calm on purpose: the smoke rises at walking pace, a soft glint climbs the body once
// every few seconds, nothing flickers. No shadow caster: a hidden body casts no shadow. The colours are
// not called _Color / _BaseColor: the player's hit-flash property block sets those on the body.
Shader "Meshy/Stealth"
{
    Properties
    {
        _Shadow ("Shadow colour (linear); alpha = opacity", Color) = (0.02, 0.008, 0.045, 0.6)
        _Edge ("Edge glow (HDR, linear)", Color) = (0.42, 0.2, 1, 1)
        _EdgePower ("Edge power", Float) = 2.4
        _Smoke ("Smoke shimmer (0..1)", Float) = 0.35
        _FeetY ("Feet height (world)", Float) = 0
        _Height ("Body height", Float) = 1.6
        _Fade ("Fade in (0..1)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "StealthDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 vert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha // premultiplied: the dark body darkens, the edge glow adds
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Shadow;
                half4 _Edge;
                half _EdgePower;
                half _Smoke;
                float _FeetY;
                float _Height;
                half _Fade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                return o;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            // Smooth value noise, 0..1.
            float Noise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float rim = pow(1.0 - saturate(abs(dot(n, v))), _EdgePower);
                float t = _Time.y;

                // Smoke drifting up through the body (~0.35 m/s), two octaves.
                float3 q = i.positionWS * 2.6 - float3(0.0, t * 0.9, 0.0);
                float smoke = Noise(q) * 0.65 + Noise(q * 2.3 + 11.7) * 0.35;

                // A soft glint climbing from the feet to the head every ~3.5 s.
                float h = saturate((i.positionWS.y - _FeetY) / max(0.1, _Height));
                float s = frac(t * 0.28) * 1.6 - 0.3;
                float band = (h - s) * 7.0;
                band = exp(-band * band);

                float a = saturate(_Shadow.a * lerp(1.0 - _Smoke, 1.0, smoke) + rim * 0.25);
                float3 glow = _Edge.rgb * (rim * (0.6 + 0.4 * smoke) + band * (0.05 + 0.1 * smoke) + smoke * smoke * 0.04);
                return half4(_Shadow.rgb * a + glow, a) * _Fade;
            }
            ENDHLSL
        }
    }
}

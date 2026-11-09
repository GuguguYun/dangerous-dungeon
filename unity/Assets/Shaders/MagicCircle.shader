// One layer of a spell's magic circle on the floor (MagicCircle.cs): a flat quad whose texture holds the
// circle's lines in red and their soft glow (plus a pool of light) in green, made in code. Additive, so it
// lights the stone without hiding it. _Reveal draws the lines in round the circle, with a bright tip
// where the drawing has got to; _Fade takes the whole layer down.
Shader "Meshy/MagicCircle"
{
    Properties
    {
        _MainTex ("Lines (R), glow (G)", 2D) = "black" {}
        _Color ("Colour (HDR)", Color) = (1, 0.5, 0.2, 1)
        _Glow ("Glow strength", Float) = 0.8
        _Reveal ("Drawn so far (0..1 round the circle)", Float) = 1
        _Fade ("Fade", Float) = 1
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

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Glow;
                half _Reveal;
                half _Fade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half2 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rg;
                float2 p = i.uv * 2.0 - 1.0;
                float a = atan2(p.y, p.x) / 6.2831853 + 0.5; // 0..1 round the circle
                float drawn = saturate((_Reveal - a) * 80.0 + 1.0);
                float tip = _Reveal < 1.0 ? exp(-pow((a - _Reveal) * 40.0, 2.0)) * 3.0 : 0.0;
                half lines = t.r * (drawn + tip);
                half glow = t.g * _Glow * saturate(_Reveal * 1.5);
                return half4(_Color.rgb * (lines + glow) * _Fade, 1.0);
            }
            ENDHLSL
        }
    }
}

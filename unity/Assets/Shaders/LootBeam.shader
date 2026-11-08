// A loot light pillar: a quad that turns about its vertical axis to face the camera, drawn as a soft
// additive column (bright thin core inside a wide glow) that fades out towards the top, with faint
// streaks drifting slowly upward. The object's X scale is the beam width, its Y scale the height.
Shader "Meshy/LootBeam"
{
    Properties
    {
        _Color ("Colour (HDR)", Color) = (1, 0.7, 0.3, 1)
        _Core ("Core strength", Float) = 1.6
        _Glow ("Glow strength", Float) = 0.5
        _Falloff ("Fade towards the top", Float) = 1.6
        _Streaks ("Streak strength", Float) = 0.35
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
                half _Core;
                half _Glow;
                half _Falloff;
                half _Streaks;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float height : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 origin = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                float width = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
                float height = length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));
                float3 toCam = _WorldSpaceCameraPos - origin;
                toCam.y = 0.0;
                toCam.xz += 1e-4;
                float3 right = normalize(cross(float3(0.0, 1.0, 0.0), toCam));
                float3 p = origin + right * i.positionOS.x * width + float3(0.0, 1.0, 0.0) * i.positionOS.y * height;
                o.positionCS = TransformWorldToHClip(p);
                o.uv = i.uv;
                o.height = height;
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 c = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(c), Hash(c + float2(1, 0)), f.x), lerp(Hash(c + float2(0, 1)), Hash(c + 1.0), f.x), f.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float u = i.uv.x * 2.0 - 1.0;
                float v = i.uv.y;
                float y = v * i.height; // metres above the floor
                // A soft column of light: a gentle core inside a wide haze (no hard laser line).
                float core = exp(-u * u * 22.0);
                float glow = exp(-u * u * 3.2) * (1.0 - u * u);
                // Fade upward; brighter where it rises out of the floor glow; soft first centimetres.
                float up = pow(saturate(1.0 - v), _Falloff) * smoothstep(0.0, 0.05, y);
                float base = 1.0 + 1.3 * exp(-y / 0.45);
                // Long vertical streaks drifting slowly up (in metres, so tall beams don't stretch them).
                float n = Noise(float2(u * 3.0, y * 0.8 - _Time.y * 0.6)) * 0.65 + Noise(float2(u * 6.0 + 5.0, y * 1.5 - _Time.y * 0.9)) * 0.35;
                float streak = lerp(1.0 - _Streaks, 1.0 + _Streaks, n);
                float3 white = max(_Color.r, max(_Color.g, _Color.b)).xxx;
                float3 col = _Color.rgb * glow * _Glow * streak + lerp(_Color.rgb, white, 0.35) * core * _Core;
                return half4(col * up * base, 1.0);
            }
            ENDHLSL
        }
    }
}

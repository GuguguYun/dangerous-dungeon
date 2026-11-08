// A stylized energy shield on a geodesic dome (ShieldFx builds the mesh): hexagonal cells drawn from
// barycentric coordinates (the dual of an icosphere, so no UV seams or pole pinch), a soft rim, a clear
// middle so the character stays readable, a slow energy sweep, impact ripples and a glowing line where
// the dome meets the floor. Calm on purpose: nothing pulses faster than ~0.3 Hz.
Shader "Meshy/Shield"
{
    Properties
    {
        _Color ("Field colour (HDR)", Color) = (0.4, 0.8, 1, 1)
        _Accent ("Accent colour: hits, sweep (HDR)", Color) = (1, 0.9, 0.6, 1)
        _RimPower ("Rim power", Float) = 2.6
        _Rim ("Rim strength", Float) = 1.1
        _Line ("Cell line strength", Float) = 0.75
        _LineWidth ("Cell line width", Float) = 0.07
        _LineCentre ("Line strength facing the camera", Float) = 0.1
        _Face ("Cell fill strength", Float) = 0.08
        _Back ("Back face strength", Float) = 0.3
        _Fill ("Visible cells (0..1)", Float) = 1
        _FloorY ("Floor height (world)", Float) = 0
        _Contact ("Floor contact glow", Float) = 1.4
        _Now ("Script time (hits are stamped with it)", Float) = 0
        _Hit0 ("Hit 0 (object pos, start time)", Vector) = (0, 0, 0, -100)
        _Hit1 ("Hit 1", Vector) = (0, 0, 0, -100)
        _Hit2 ("Hit 2", Vector) = (0, 0, 0, -100)
        _Hit3 ("Hit 3", Vector) = (0, 0, 0, -100)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off
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
                half4 _Accent;
                half _RimPower;
                half _Rim;
                half _Line;
                half _LineWidth;
                half _LineCentre;
                half _Face;
                half _Back;
                half _Fill;
                float _FloorY;
                half _Contact;
                float _Now;
                float4 _Hit0;
                float4 _Hit1;
                float4 _Hit2;
                float4 _Hit3;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 bary : COLOR;      // (1,0,0) / (0,1,0) / (0,0,1) per triangle corner
                float3 cellRand : TEXCOORD0; // a random value per corner (= per hex cell)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float3 bary : TEXCOORD3;
                float3 cellRand : TEXCOORD4;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.positionOS = i.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.bary = i.bary.rgb;
                o.cellRand = i.cellRand;
                return o;
            }

            // An expanding ring and a short flare around one impact (positions on the unit sphere).
            float Hit(float4 h, float3 p, float t)
            {
                float age = t - h.w;
                if (age < 0.0 || age > 0.7) return 0.0;
                float d = distance(p, h.xyz);
                float k = (d - age * 1.0) / 0.07;
                float fade = 1.0 - age / 0.7;
                float ring = exp(-k * k) * fade * fade;
                float flare = exp(-d * d / 0.012) * exp(-age * 7.0);
                return ring * 0.8 + flare;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float height = i.positionWS.y - _FloorY;
                clip(height);

                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float ndv = abs(dot(n, v));
                float rim = pow(saturate(1.0 - ndv), _RimPower);

                // Hex cells: the cell is the corner with the largest barycentric weight; its border
                // is where the two largest weights meet.
                float3 b = i.bary;
                float mx = max(b.x, max(b.y, b.z));
                float mn = min(b.x, min(b.y, b.z));
                float mid = b.x + b.y + b.z - mx - mn;
                float edge = mx - mid;
                float aa = fwidth(edge) * 1.5;
                float lines = 1.0 - smoothstep(_LineWidth, _LineWidth + aa, edge);
                float rnd = b.x >= mx ? i.cellRand.x : (b.y >= mx ? i.cellRand.y : i.cellRand.z);

                float t = _Time.y;
                // Cells fade in and out by their random value (appear, dissolve, damage).
                float cell = saturate((_Fill * 1.15 - rnd) * 7.0);
                // Each cell breathes slowly with its own phase.
                float shimmer = 0.6 + 0.4 * sin(t * 1.1 + rnd * 6.2832);
                float inner = pow(1.0 - saturate(edge), 3.0);

                // A soft band of light climbing the dome every few seconds.
                float lat = i.positionOS.y; // -1..1 on the unit sphere
                float s = frac(t * 0.22) * 2.6 - 1.1;
                float sl = (lat - s) * 5.0;
                float sweep = exp(-sl * sl);

                float hits = min(1.5, Hit(_Hit0, i.positionOS, _Now) + Hit(_Hit1, i.positionOS, _Now) + Hit(_Hit2, i.positionOS, _Now) + Hit(_Hit3, i.positionOS, _Now));

                float lineA = lines * lerp(_LineCentre, 1.0, saturate(rim * 1.6)) * _Line;
                float3 col = _Color.rgb * (lineA * (0.75 + 0.5 * sweep) + inner * _Face * shimmer) * cell;
                col += _Color.rgb * rim * _Rim * lerp(0.35, 1.0, cell);
                // Hits light up the cell borders around the impact more than the cells themselves.
                col += _Accent.rgb * (sweep * lines * 0.3 * cell + hits * (0.12 + lines * 0.9 + inner * 0.2));
                // Where the dome meets the floor: a clean bright seam.
                col += _Color.rgb * exp(-height / 0.05) * _Contact * saturate(_Fill * 2.0);
                col *= IS_FRONT_VFACE(face, 1.0, _Back);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}

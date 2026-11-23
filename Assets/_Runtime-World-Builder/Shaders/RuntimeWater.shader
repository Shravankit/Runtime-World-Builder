Shader "RuntimeWorldBuilder/Water"
{
    Properties
    {
        [Enum(River,0,Still,1)] _Mode ("Mode (River = flows along UV.y, Still = lake/pond)", Float) = 0
        _ShallowColor ("Shallow colour", Color) = (0.15, 0.50, 0.52, 1)
        _DeepColor ("Deep colour", Color) = (0.02, 0.13, 0.20, 1)
        _DepthDistance ("Depth distance (m)", Float) = 2.5
        _EdgeSoftness ("Shore fade (m)", Float) = 0.25
        _Width ("River width (m)", Float) = 24
        _FlowSpeed ("Flow speed (m/s)", Float) = 1.6
        _Cycle ("Flow loop time (s)", Float) = 2
        _RippleScale ("Ripple scale", Float) = 0.35
        _RippleStrength ("Ripple strength", Float) = 1.2
        _Refraction ("Refraction", Float) = 0.06
        _SpecPower ("Sun glint power", Float) = 500
        _SunSpec ("Sun glint amount", Float) = 1.5
        _ReflectionStrength ("Sky reflection", Float) = 1
        _FoamColor ("Foam colour", Color) = (0.95, 0.97, 1, 1)
        _FoamDistance ("Foam shore distance (m)", Float) = 0.7
        _FoamNoiseScale ("Foam noise scale", Float) = 1.4
        _FoamAmount ("Foam amount", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Mode;
                float4 _ShallowColor, _DeepColor, _FoamColor;
                float _DepthDistance, _EdgeSoftness, _Width, _FlowSpeed, _Cycle;
                float _RippleScale, _RippleStrength, _Refraction;
                float _SpecPower, _SunSpec, _ReflectionStrength;
                float _FoamDistance, _FoamNoiseScale, _FoamAmount;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 posWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                VertexPositionInputs v = GetVertexPositionInputs(IN.positionOS.xyz);
                o.positionCS = v.positionCS;
                o.posWS = v.positionWS;
                o.screenPos = v.positionNDC;
                o.uv = IN.uv;
                return o;
            }

            // ---------- procedural noise (no textures needed) ----------
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i), b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1)), d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm(float2 p)
            {
                float v = 0, a = 0.5;
                [unroll] for (int k = 0; k < 4; k++) { v += a * vnoise(p); p = p * 2.03 + 17.1; a *= 0.5; }
                return v;
            }

            float2 RippleGrad(float2 p)
            {
                const float e = 0.03;
                float h0 = fbm(p);
                return float2(fbm(p + float2(e, 0)) - h0, fbm(p + float2(0, e)) - h0) / e;
            }

            float3 SkyReflection(float3 R)
            {
                half4 e = SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, R, 1.0);
                return DecodeHDREnvironment(e, unity_SpecCube0_HDR);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 posWS = i.posWS;
                float3 V = normalize(GetWorldSpaceViewDir(posWS));
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float3 up = float3(0, 1, 0);

                // surface tangent frame from screen derivatives (so ripples follow the river's bends)
                float3 dp1 = ddx(posWS), dp2 = ddy(posWS);
                float2 du1 = ddx(i.uv), du2 = ddy(i.uv);
                float3 dp2perp = cross(dp2, up), dp1perp = cross(up, dp1);
                float3 Tt = dp2perp * du1.x + dp1perp * du2.x;   // along UV.x (across the river)
                float3 Bt = dp2perp * du1.y + dp1perp * du2.y;   // along UV.y (downstream)
                Tt = normalize(Tt + 1e-6);
                Bt = normalize(Bt + 1e-6);

                float2 coord, dir;
                float speedMul = 1.0;
                if (_Mode < 0.5)
                {
                    coord = float2(i.uv.x * _Width, i.uv.y * 10.0);   // metres across / along (uvPerMeter = 0.1)
                    dir = float2(0, 1);
                    float c = abs(i.uv.x * 2.0 - 1.0);
                    speedMul = lerp(1.0, 0.4, c * c);                 // slower at the banks
                }
                else
                {
                    coord = posWS.xz;
                    dir = normalize(float2(1.0, 0.6));
                    Tt = float3(1, 0, 0);
                    Bt = float3(0, 0, 1);
                }

                // two layers, half a cycle apart, cross-faded, so the flow loops with no visible pop
                float t = _Time.y / max(_Cycle, 0.01);
                float t1 = frac(t), t2 = frac(t + 0.5);
                float w1 = 1.0 - abs(1.0 - 2.0 * t1);
                float travel = _FlowSpeed * speedMul * _Cycle;
                float2 p1 = coord - dir * t1 * travel;
                float2 p2 = coord - dir * t2 * travel + float2(5.2, 1.3);

                float2 g = lerp(RippleGrad(p2 * _RippleScale), RippleGrad(p1 * _RippleScale), w1);
                float2 g2 = lerp(RippleGrad(p2 * _RippleScale * 3.1), RippleGrad(p1 * _RippleScale * 3.1), w1);
                g = (g + g2 * 0.35) * _RippleStrength * 0.25;
                float3 n = normalize(up - (Tt * g.x + Bt * g.y));

                // ---------- depth ----------
                float surfEye = i.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float diff = max(sceneEye - surfEye, 0.0);

                // refraction (fall back to straight-through if the refracted sample hits something in front of the water)
                float2 ruv = screenUV + n.xz * _Refraction * saturate(diff);
                float rEye = LinearEyeDepth(SampleSceneDepth(ruv), _ZBufferParams);
                if (rEye < surfEye) ruv = screenUV;
                float3 under = SampleSceneColor(ruv);
                float diffR = max(rEye - surfEye, 0.0);
                diff = rEye < surfEye ? diff : diffR;

                float depthF = 1.0 - exp(-diff / max(_DepthDistance, 0.01));
                float3 waterCol = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthF);
                float3 trans = exp(-diff * float3(0.45, 0.15, 0.08) * 3.0 / max(_DepthDistance, 0.01));

                Light mainLight = GetMainLight();
                float3 ambient = SampleSH(up);
                float3 lightAmt = saturate(ambient * 0.9 + mainLight.color * saturate(mainLight.direction.y) * 0.7);
                float3 body = under * trans + waterCol * lightAmt * (1.0 - trans);

                // ---------- reflection + sun ----------
                float3 R = reflect(-V, n);
                float fres = 0.02 + 0.98 * pow(1.0 - saturate(dot(n, V)), 5.0);
                float3 refl = SkyReflection(R) * _ReflectionStrength;
                float3 H = normalize(mainLight.direction + V);
                float3 spec = pow(saturate(dot(n, H)), _SpecPower) * _SunSpec * mainLight.color;

                float3 col = body * (1.0 - fres) + refl * fres + spec;

                // ---------- foam at the shoreline ----------
                float edge = 1.0 - saturate(diff / max(_FoamDistance, 0.01));
                float fn = lerp(fbm(p2 * _FoamNoiseScale), fbm(p1 * _FoamNoiseScale), w1);
                float foam = saturate(edge * edge * 1.6) * smoothstep(0.30, 0.65, fn + edge * 0.45);
                foam *= _FoamAmount;
                col = lerp(col, _FoamColor.rgb * lightAmt, saturate(foam));

                float alpha = saturate(diff / max(_EdgeSoftness, 0.01));
                alpha = saturate(max(alpha, foam * 0.8));
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
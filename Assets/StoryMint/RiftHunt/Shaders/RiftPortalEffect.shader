Shader "StoryMint/RiftPortalEffect"
{
    Properties
    {
        _MainTex ("Noise Texture", 2D) = "white" {}
        _Color ("Edge Color", Color) = (0.1, 0.95, 1.0, 0.8)
        _CoreColor ("Core Color", Color) = (0.02, 0.03, 0.06, 0.6)
        _CrackColor ("Crack Color", Color) = (1.0, 0.35, 0.18, 0.9)
        _EmissionBoost ("Emission Boost", Float) = 2.5
        _PulseSpeed ("Pulse Speed", Float) = 3.0
        _DistortSpeed ("Distort Speed", Float) = 1.2
        _DistortAmount ("Distort Amount", Float) = 0.08
        _EdgeWidth ("Edge Width", Range(0.01, 0.4)) = 0.12
        _JaggedAmount ("Jagged Amount", Range(0.0, 0.3)) = 0.1
        _PortalRadius ("Portal Radius", Range(0.1, 0.5)) = 0.38
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+50"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "RiftPortal"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half4 _CoreColor;
                half4 _CrackColor;
                half _EmissionBoost;
                half _PulseSpeed;
                half _DistortSpeed;
                half _DistortAmount;
                half _EdgeWidth;
                half _JaggedAmount;
                half _PortalRadius;
            CBUFFER_END

            // Simple hash-based noise (no texture needed as fallback)
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f); // smoothstep

                float a = hash21(i);
                float b = hash21(i + float2(1.0, 0.0));
                float c = hash21(i + float2(0.0, 1.0));
                float d = hash21(i + float2(1.0, 1.0));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                float2 shift = float2(100.0, 100.0);
                for (int i = 0; i < 4; i++)
                {
                    v += a * valueNoise(p);
                    p = p * 2.0 + shift;
                    a *= 0.5;
                }
                return v;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 centeredUV = input.uv - 0.5;
                float dist = length(centeredUV);
                float angle = atan2(centeredUV.y, centeredUV.x);

                float t = _Time.y;

                // Jagged edge distortion
                float jaggedNoise = fbm(float2(angle * 3.0, t * 0.5)) * _JaggedAmount;
                float distortedDist = dist + jaggedNoise;

                // Pulsing radius
                float pulse = 1.0 + sin(t * _PulseSpeed) * 0.04;
                float portalRadius = _PortalRadius * pulse;
                float outerRadius = portalRadius + _EdgeWidth;

                // Core void (dark center)
                float coreMask = 1.0 - smoothstep(portalRadius - 0.06, portalRadius - 0.01, distortedDist);

                // Edge ring (bright rim)
                float edgeMask = smoothstep(portalRadius - 0.02, portalRadius, distortedDist)
                               * (1.0 - smoothstep(outerRadius, outerRadius + 0.03, distortedDist));

                // Soft outer glow
                float glowMask = (1.0 - smoothstep(outerRadius, outerRadius + 0.15, distortedDist)) * 0.35;

                // Animated cracks through the rim
                float crackNoise = fbm(float2(angle * 5.0 + t * 0.8, dist * 8.0 - t * _DistortSpeed));
                float crackMask = edgeMask * step(0.62, crackNoise) * 1.5;

                // Inner vortex distortion
                float2 vortexUV = centeredUV;
                float vortexAngle = t * 0.6;
                float s = sin(vortexAngle);
                float c = cos(vortexAngle);
                vortexUV = float2(vortexUV.x * c - vortexUV.y * s,
                                  vortexUV.x * s + vortexUV.y * c);
                float vortex = fbm(vortexUV * 6.0 + t * 0.3) * coreMask * 0.3;

                // Compose color
                half3 col = half3(0, 0, 0);
                col += _CoreColor.rgb * (coreMask * 0.8 + vortex);
                col += _Color.rgb * _EmissionBoost * edgeMask;
                col += _CrackColor.rgb * _EmissionBoost * crackMask;
                col += _Color.rgb * glowMask;

                // Alpha
                float alpha = saturate(coreMask * _CoreColor.a
                            + edgeMask * _Color.a
                            + glowMask
                            + crackMask * _CrackColor.a);

                // Discard fully outside
                alpha *= 1.0 - smoothstep(outerRadius + 0.12, outerRadius + 0.2, dist);

                half4 finalColor = half4(col, alpha);
                finalColor.rgb = MixFog(finalColor.rgb, input.fogFactor);
                return finalColor;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

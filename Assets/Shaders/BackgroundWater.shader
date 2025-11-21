Shader "Custom/BackgroundWater"
{
    Properties
    {
        _ShallowColor("Shallow Color", Color) = (0.19, 0.65, 0.88, 1)
        _DeepColor("Deep Water Color", Color) = (0.02, 0.17, 0.32, 1)
        _HorizonColor("Horizon Tint", Color) = (0.0, 0.35, 0.6, 1)
        _WaveScale("Wave Tiling", Range(0.5, 40)) = 8
        _WaveSpeed1("Primary Speed", Range(0.0, 2.0)) = 0.45
        _WaveSpeed2("Secondary Speed", Range(0.0, 2.0)) = 0.25
        _Distortion("Distortion Strength", Range(0.0, 1.0)) = 0.2
        _FoamStrength("Foam Highlight", Range(0.0, 1.0)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
            "PreviewType" = "Plane"
        }

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "UnlitWater"
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float4 _HorizonColor;
                float _WaveScale;
                float _WaveSpeed1;
                float _WaveSpeed2;
                float _Distortion;
                float _FoamStrength;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);

                float a = hash21(i);
                float b = hash21(i + float2(1.0, 0.0));
                float c = hash21(i + float2(0.0, 1.0));
                float d = hash21(i + float2(1.0, 1.0));

                float2 u = f * f * (3.0 - 2.0 * f);

                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float fbm(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.5;
                float2 shift = float2(37.0, 17.0);

                for (int i = 0; i < 4; ++i)
                {
                    value += noise(p) * amplitude;
                    p = p * 2.0 + shift;
                    amplitude *= 0.5;
                }

                return value;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float waveScale = max(_WaveScale, 0.01);
                float2 uv = input.uv * waveScale;
                float time = _Time.y;

                float n1 = fbm(uv * 1.25 + time * _WaveSpeed1);
                float n2 = fbm(float2(uv.y, uv.x) * 1.75 - time * (_WaveSpeed2 + 0.1));
                float combined = n1 * 0.65 + n2 * 0.35;

                float2 distortion = float2(n2 - 0.5, n1 - 0.5) * _Distortion;
                float2 flowUV = uv + distortion + float2(time * _WaveSpeed1, time * _WaveSpeed2);

                float crest = sin((flowUV.x + flowUV.y) * 3.14159);
                crest = crest * 0.5 + 0.5;
                crest = saturate(crest + combined * 0.5);

                float gradient = saturate(input.uv.y + combined * 0.15);
                float3 waterColor = lerp(_DeepColor.rgb, _ShallowColor.rgb, gradient);

                float horizonMix = saturate(input.uv.y * 0.8 + combined * 0.2);
                waterColor = lerp(waterColor, _HorizonColor.rgb, horizonMix * 0.25);

                float foam = pow(saturate(crest), 3.0) * _FoamStrength;
                waterColor += foam;

                return half4(waterColor, 1.0);
            }
            ENDHLSL
        }
    }
}

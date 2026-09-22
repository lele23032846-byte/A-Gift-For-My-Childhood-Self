Shader "MemoryLook/TV CRT Overlay"
{
    Properties
    {
        _MainTex ("Screen Texture", 2D) = "white" {}
        _Tint ("Screen Tint", Color) = (0.72, 0.82, 0.84, 0.72)
        _Brightness ("Brightness", Range(0, 2)) = 0.65
        _ScanlineStrength ("Scanline Strength", Range(0, 1)) = 0.16
        _NoiseStrength ("Noise Strength", Range(0, 0.25)) = 0.025
        _Curvature ("Curvature", Range(0, 0.2)) = 0.055
        _ChromaOffset ("Chroma Offset", Range(0, 0.01)) = 0.0014
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+10"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "TVCRT"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint;
                half _Brightness;
                half _ScanlineStrength;
                half _NoiseStrength;
                half _Curvature;
                half _ChromaOffset;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 centered = input.uv * 2.0 - 1.0;
                centered *= 1.0 + dot(centered, centered) * _Curvature;
                float2 uv = centered * 0.5 + 0.5;

                half inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                float2 chroma = float2(_ChromaOffset, 0.0);
                half3 screen;
                screen.r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + chroma).r;
                screen.g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).g;
                screen.b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - chroma).b;

                half scanline = 1.0 - _ScanlineStrength * (0.5 + 0.5 * sin(uv.y * 720.0));
                half noise = (Hash21(floor(uv * float2(320.0, 180.0)) + floor(_Time.y * 12.0)) - 0.5) * _NoiseStrength;
                half vignette = saturate(1.0 - dot(centered * float2(0.68, 0.86), centered * float2(0.68, 0.86)) * 0.34);

                screen = saturate((screen * scanline + noise) * _Tint.rgb * _Brightness * vignette);
                return half4(screen, _Tint.a * inside);
            }
            ENDHLSL
        }
    }
}

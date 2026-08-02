Shader "Project Eri/AP Ready Soft Glow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _GlowColor ("Glow Color", Color) = (0.56,0.92,1,1)
        _GlowSoftness ("Glow Softness", Range(0.5,8)) = 2.4
        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "CanUseSpriteAtlas" = "True"
            "PreviewType" = "Plane"
        }

        Cull Off
        ZWrite Off
        Blend One One

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        float4 _MainTex_TexelSize;

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half4 _GlowColor;
            float _GlowSoftness;
        CBUFFER_END

        struct Attributes
        {
            float3 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
        };

        Varyings GlowVertex(Attributes input)
        {
            Varyings output;
            output.positionCS = TransformObjectToHClip(input.positionOS);
            output.uv = input.uv;
            output.color = input.color * _Color;
            return output;
        }

        half SampleAlpha(float2 uv)
        {
            return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a;
        }

        half4 GlowFragment(Varyings input) : SV_Target
        {
            float2 offset = _MainTex_TexelSize.xy * _GlowSoftness;

            half alpha = SampleAlpha(input.uv) * 0.20;
            alpha += SampleAlpha(input.uv + float2(offset.x, 0)) * 0.10;
            alpha += SampleAlpha(input.uv - float2(offset.x, 0)) * 0.10;
            alpha += SampleAlpha(input.uv + float2(0, offset.y)) * 0.10;
            alpha += SampleAlpha(input.uv - float2(0, offset.y)) * 0.10;
            alpha += SampleAlpha(input.uv + offset) * 0.10;
            alpha += SampleAlpha(input.uv - offset) * 0.10;
            alpha += SampleAlpha(input.uv + float2(offset.x, -offset.y)) * 0.10;
            alpha += SampleAlpha(input.uv + float2(-offset.x, offset.y)) * 0.10;

            alpha = saturate(alpha) * input.color.a;
            half3 emittedColor = saturate(_GlowColor.rgb) * alpha;
            return half4(emittedColor, alpha);
        }
        ENDHLSL

        Pass
        {
            Name "APReadyGlowUniversal2D"
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex GlowVertex
            #pragma fragment GlowFragment
            ENDHLSL
        }

        Pass
        {
            Name "APReadyGlowUniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex GlowVertex
            #pragma fragment GlowFragment
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}

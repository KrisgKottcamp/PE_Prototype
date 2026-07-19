Shader "Project Eri/Damage Flash White"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
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
        Blend One OneMinusSrcAlpha

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass Keep
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
        CBUFFER_END

        struct Attributes
        {
            float3 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings FlashVertex(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            output.positionCS =
                TransformObjectToHClip(input.positionOS);
            output.uv = input.uv;
            output.color = input.color * _Color;
            return output;
        }

        half4 FlashFragment(Varyings input) : SV_Target
        {
            half alpha = SAMPLE_TEXTURE2D(
                _MainTex,
                sampler_MainTex,
                input.uv
            ).a * input.color.a;

            clip(alpha - 0.001);

            // Premultiplied white preserves soft sprite edges while replacing
            // every visible source color with pure white.
            return half4(alpha, alpha, alpha, alpha);
        }
        ENDHLSL

        Pass
        {
            Name "DamageFlashUniversal2D"
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex FlashVertex
            #pragma fragment FlashFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "DamageFlashUniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex FlashVertex
            #pragma fragment FlashFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}

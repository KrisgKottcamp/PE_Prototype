Shader "Project Eri/UI/Screen Invert"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest Always
        ColorMask RGB
        // Source gray 0 leaves the framebuffer alone; white 1 becomes 1 - destination.
        Blend OneMinusDstColor OneMinusSrcColor
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
            };
            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.color = input.color;
                return output;
            }
            fixed4 frag(v2f input) : SV_Target
            {
                return fixed4(input.color.rgb, 1);
            }
            ENDCG
        }
    }
}

Shader "Waribasi/HandState"
{
    Properties
    {
        _BaseMap("Texture", 2D) = "white" {}
        _BaseColor("Color", Color) = (1,1,1,1)
        _Opacity("Opacity", Range(0,1)) = 1
        _Rainbow("Rainbow", Float) = 0
        _Selected("Selected", Float) = 0
        _RainbowSpeed("Rainbow speed", Float) = 0.3
        _ZWrite("Depth write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_ZWrite]
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _Opacity;
                float _Rainbow;
                float _Selected;
                float _RainbowSpeed;
                float _ZWrite;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half3 selected = half3(1, 0.07, 0.04);
                float hue = frac(input.positionWS.x * 0.6 + input.positionWS.y * 0.4 + _Time.y * _RainbowSpeed);
                half3 rainbow = saturate(abs(frac(hue + float3(0, 0.6666667, 0.3333333)) * 6 - 3) - 1);
                baseColor.rgb = lerp(baseColor.rgb, selected, _Selected);
                baseColor.rgb = lerp(baseColor.rgb, rainbow, _Rainbow);
                baseColor.a *= _Opacity;
                return baseColor;
            }
            ENDHLSL
        }
    }
}

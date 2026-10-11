// Far-off scenery seen through the fog: the silhouette of a zone that is not loaded yet,
// or a distant light (a lit window, a warning beacon).
// Unlit and not affected by scene fog, because scene fog would hide anything this far away.
// The haze is baked into the colour instead. Nearer, fogged geometry still covers it.
// Fades out as the camera approaches, so the real zone can take over.
Shader "Hollowmere/Vista"
{
    Properties
    {
        [Header(Shape)]
        _MainTex ("Silhouette mask (alpha)", 2D) = "white" {}
        [Toggle] _Glow ("Soft round glow instead of mask", Float) = 0
        [Toggle] _Billboard ("Always face the camera", Float) = 0

        [Header(Colour)]
        [HDR] _Color ("Colour", Color) = (0.11, 0.13, 0.15, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.85

        [Header(Distance fade)]
        _FadeStart ("Fully visible beyond (m)", Float) = 150
        _FadeEnd ("Invisible nearer than (m)", Float) = 100

        [Header(Pulse)]
        _PulseSpeed ("Pulse speed (0 = steady)", Float) = 0
        _PulseMin ("Dimmest point of the pulse", Range(0, 1)) = 0.15

        [Header(Blending)]
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend (One = additive light)", Float) = 10
    }

    SubShader
    {
        // Drawn before the water and other transparent surfaces, which sit in front of it.
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Vista"

            Blend SrcAlpha [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Opacity;
                half _Glow;
                half _Billboard;
                float _FadeStart;
                float _FadeEnd;
                float _PulseSpeed;
                half _PulseMin;
                float _DstBlend;
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
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                float3 positionWS;
                if (_Billboard > 0.5)
                {
                    // Keep the object's centre and size, but spread the corners along the camera's axes.
                    float3 centreWS = TransformObjectToWorld(float3(0, 0, 0));
                    float width = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                    float height = length(float3(UNITY_MATRIX_M[0].y, UNITY_MATRIX_M[1].y, UNITY_MATRIX_M[2].y));
                    float3 right = UNITY_MATRIX_V[0].xyz;
                    float3 up = UNITY_MATRIX_V[1].xyz;
                    positionWS = centreWS + right * input.positionOS.x * width + up * input.positionOS.y * height;
                }
                else
                {
                    positionWS = TransformObjectToWorld(input.positionOS.xyz);
                }

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half shape;
                if (_Glow > 0.5)
                {
                    half falloff = saturate(1.0 - length(input.uv - 0.5) * 2.0);
                    shape = falloff * falloff;
                }
                else
                {
                    shape = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv * _MainTex_ST.xy + _MainTex_ST.zw).a;
                }

                float distanceToCamera = distance(_WorldSpaceCameraPos, input.positionWS);
                half fade = saturate((distanceToCamera - _FadeEnd) / max(_FadeStart - _FadeEnd, 0.001));

                half pulse = 1.0;
                if (_PulseSpeed > 0.0)
                {
                    half wave = 0.5 + 0.5 * sin(_Time.y * _PulseSpeed);
                    pulse = lerp(_PulseMin, 1.0, wave * wave);
                }

                return half4(_Color.rgb * pulse, shape * _Opacity * fade * _Color.a);
            }
            ENDHLSL
        }
    }
}

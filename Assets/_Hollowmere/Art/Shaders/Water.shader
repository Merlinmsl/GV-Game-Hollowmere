// Flood water for URP (Forward / Forward+).
// Depth-based colour and transparency, two scrolling normal maps, Fresnel sky reflection,
// and specular highlights from the main light and nearby lights (lamps, flashlight).
// Needs the Depth Texture enabled on the URP asset.
Shader "Hollowmere/Water"
{
    Properties
    {
        [Header(Colour)]
        _ShallowColor ("Shallow colour", Color) = (0.16, 0.20, 0.17, 0.55)
        _DeepColor ("Deep colour", Color) = (0.03, 0.06, 0.06, 0.94)
        _DepthFade ("Depth for full deep colour (m)", Range(0.05, 5)) = 0.6
        _EdgeFade ("Shoreline soft edge (m)", Range(0.005, 0.5)) = 0.05

        [Header(Waves)]
        [Normal] _NormalMap ("Normal map", 2D) = "bump" {}
        _NormalStrength ("Normal strength", Range(0, 2)) = 0.35
        _WaveA ("Wave A: tiling (xy), speed (zw)", Vector) = (0.11, 0.11, 0.012, 0.007)
        _WaveB ("Wave B: tiling (xy), speed (zw)", Vector) = (0.27, 0.23, -0.009, 0.014)

        [Header(Surface)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.93
        _FresnelPower ("Fresnel power", Range(1, 8)) = 4.5
        _ReflectionStrength ("Reflection strength", Range(0, 2)) = 1
        _SpecularStrength ("Specular strength", Range(0, 4)) = 1.2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                float _DepthFade;
                float _EdgeFade;
                float4 _NormalMap_ST;
                half _NormalStrength;
                float4 _WaveA;
                float4 _WaveB;
                half _Smoothness;
                half _FresnelPower;
                half _ReflectionStrength;
                half _SpecularStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fogFactor : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                output.fogFactor = ComputeFogFactor(p.positionCS.z);
                return output;
            }

            // Blinn-Phong highlight with an exponent derived from smoothness
            half3 Highlight(Light light, half3 normalWS, half3 viewWS, half exponent)
            {
                half3 h = SafeNormalize(light.direction + viewWS);
                half nh = saturate(dot(normalWS, h));
                half atten = light.distanceAttenuation * light.shadowAttenuation;
                return light.color * (pow(nh, exponent) * atten);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);

                // How much water is in front of the scene behind this pixel
                float sceneEyeDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfaceEyeDepth = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float waterDepth = max(0.0, sceneEyeDepth - surfaceEyeDepth);

                // Two normal-map layers scrolling in different directions (world-space UVs, so no seams)
                float2 uvA = input.positionWS.xz * _WaveA.xy + _Time.y * _WaveA.zw;
                float2 uvB = input.positionWS.xz * _WaveB.xy + _Time.y * _WaveB.zw;
                half3 nA = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvA), _NormalStrength);
                half3 nB = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvB), _NormalStrength);
                half3 nT = normalize(half3(nA.xy + nB.xy, nA.z * nB.z));
                half3 normalWS = normalize(half3(nT.x, nT.z, nT.y));   // the surface is horizontal: tangent = X, bitangent = Z

                half3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // Body colour: shallow water shows the bed, deep water goes dark and opaque
                half depth01 = saturate(waterDepth / _DepthFade);
                half4 body = lerp(_ShallowColor, _DeepColor, depth01);

                half4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half3 ambient = SampleSH(half3(0, 1, 0));
                half nl = saturate(dot(normalWS, mainLight.direction));
                half3 lit = body.rgb * (ambient + mainLight.color * (nl * mainLight.shadowAttenuation));

                half exponent = exp2(10.0 * _Smoothness + 1.0);
                half3 specular = Highlight(mainLight, normalWS, viewWS, exponent);

                #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = screenUV;
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
                    half atten = light.distanceAttenuation * light.shadowAttenuation;
                    lit += body.rgb * light.color * (saturate(dot(normalWS, light.direction)) * atten);
                    specular += Highlight(light, normalWS, viewWS, exponent);
                LIGHT_LOOP_END
                #endif

                // Fresnel: looking along the surface reflects the sky, looking down sees into the water
                half fresnel = pow(1.0 - saturate(dot(normalWS, viewWS)), _FresnelPower);
                fresnel = saturate(0.02 + 0.98 * fresnel) * _ReflectionStrength;
                half3 reflectDir = reflect(-viewWS, normalWS);
                half3 reflection = GlossyEnvironmentReflection(reflectDir, input.positionWS, 1.0 - _Smoothness, 1.0, screenUV);

                half3 color = lerp(lit, reflection, saturate(fresnel)) + specular * _SpecularStrength;
                half alpha = saturate(max(body.a, fresnel) + dot(specular, half3(0.33, 0.33, 0.33)));
                alpha *= saturate(waterDepth / _EdgeFade);   // soft shoreline instead of a hard cut

                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}

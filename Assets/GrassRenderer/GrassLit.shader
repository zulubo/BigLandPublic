Shader "Nature/GrassLit"
{
    Properties
    {
        _Color("Color", Color) = (1,1,1,1)
        _MainTex("Main Texture", 2D) = "white" {}
        _Specular("Specular", Range(0,1)) = 0.1
        _Smoothness("Smoothness", Range(0,1)) = 0.25
        _NormalMap("Normal Texture", 2D) = "bump" {}
        _NormalStrength("Normal Strength", Float) = 1

        _BladeSizeVariation("Blade Size Variation", Range(0,1)) = 0.3
        _Stiffness("Stiffness", Float) = 1.0
        _Weight("Weight", Float) = 1.0
    }
    SubShader
    {
        Cull Off

        Pass
        {
            CGPROGRAM
            #define LIGHTING_PASS
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "Grass.cginc"

            float4 frag(v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.tex) * i.color;
            }
            ENDCG
        }

        Pass
        {
            Tags{ "LightMode" = "Deferred" }

            CGPROGRAM
            #define DEFERRED_PASS
            #define LIGHTING_PASS
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "Grass.cginc"

            // Data that my fragmentshader has to safe
            struct fragmentdata
            {
                half4 colorOcclusion : SV_Target0;
                half4 specularSmoothness : SV_Target1;
                half4 normal : SV_Target2;
            };

            void frag(v2f i,
                out half4 outGBuffer0 : SV_Target0,
                out half4 outGBuffer1 : SV_Target1,
                out half4 outGBuffer2 : SV_Target2,
                out half4 outEmission : SV_Target3,
                fixed vface : VFACE)
            {
                if (vface<0.5) 
                {
                    i.tangentToWorldAndPackedData[0].xyz *= -1;
                    i.tangentToWorldAndPackedData[1].xyz *= -1;
                    i.tangentToWorldAndPackedData[2].xyz *= -1;
                }

                FRAGMENT_SETUP(s)
                UNITY_SETUP_INSTANCE_ID(i);

                // surface
                s.smoothness = _Smoothness;
                s.specColor = _Specular;
                s.diffColor *= 1 - _Specular;
                half3 normalTangent = lerp(float3(0,0,1), UnpackNormal(tex2D(_NormalMap, i.tex.xy)),_NormalStrength);
                half3 tangent = i.tangentToWorldAndPackedData[0].xyz;
                half3 binormal = i.tangentToWorldAndPackedData[1].xyz;
                half3 normal = i.tangentToWorldAndPackedData[2].xyz;
                s.normalWorld = NormalizePerPixelNormal(tangent * normalTangent.x + binormal * normalTangent.y + normal * normalTangent.z);

                // no analytic lights in this pass
                UnityLight dummyLight = DummyLight();
                half atten = 1;

                // only GI
                half occlusion = Occlusion(i.tex.xy);
#if UNITY_ENABLE_REFLECTION_BUFFERS
                bool sampleReflectionsInDeferred = false;
#else
                bool sampleReflectionsInDeferred = true;
#endif

                UnityGI gi = FragmentGI(s, occlusion, i.ambientOrLightmapUV, atten, dummyLight, sampleReflectionsInDeferred);
                gi.indirect.diffuse = i.ambientOrLightmapUV.rgb * s.diffColor;

                half3 emissiveColor = UNITY_BRDF_PBS(s.diffColor, s.specColor, s.oneMinusReflectivity, s.smoothness, s.normalWorld, -s.eyeVec, gi.light, gi.indirect).rgb;

#ifdef _EMISSION
                // emissiveColor += Emission(i.tex.xy);
#endif

#ifndef UNITY_HDR_ON
                //emissiveColor.rgb = exp2(-emissiveColor.rgb);
#endif

                UnityStandardData data;
                data.diffuseColor = s.diffColor;
                data.occlusion = occlusion;
                data.specularColor = s.specColor;
                data.smoothness = s.smoothness;
                data.normalWorld = lerp(s.normalWorld, float3(0, 1, 0), 0.6);

                UnityStandardDataToGbuffer(data, outGBuffer0, outGBuffer1, outGBuffer2);

                outGBuffer2.a = 0;

                // Emissive lighting buffer
                outEmission = half4(gi.indirect.diffuse, 1);

            }
            ENDCG
        }

        Pass
        {
            Tags {"LightMode" = "ShadowCaster"}

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #define SHADOW_PASS
            #include "UnityCG.cginc"
            #include "Grass.cginc"

            float4 frag(v2f i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
}
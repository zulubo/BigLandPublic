// Unity built-in shader source. Copyright (c) 2016 Unity Technologies. MIT license (see license.txt)

Shader "Hidden/GGX-DeferredShading" {
Properties {
    _LightTexture0 ("", any) = "" {}
    _LightTextureB0 ("", 2D) = "" {}
    _ShadowMapTexture ("", any) = "" {}
    _SrcBlend ("", Float) = 1
    _DstBlend ("", Float) = 1
}
SubShader {

// Pass 1: Lighting pass
//  LDR case - Lighting encoded into a subtractive ARGB8 buffer
//  HDR case - Lighting additively blended into floating point buffer
Pass {
    ZWrite Off
    Blend [_SrcBlend] [_DstBlend]

CGPROGRAM
#pragma target 3.0
#pragma vertex vert_deferred
#pragma fragment frag
#pragma multi_compile_lightpass
#pragma multi_compile ___ UNITY_HDR_ON

#pragma exclude_renderers nomrt

#include "UnityCG.cginc"
#include "UnityDeferredLibrary.cginc"
#include "UnityPBSLighting.cginc"
#include "UnityStandardUtils.cginc"
#include "UnityGBuffer.cginc"
#include "UnityStandardBRDF.cginc"

sampler2D _CameraGBufferTexture0;
sampler2D _CameraGBufferTexture1;
sampler2D _CameraGBufferTexture2;



float4 BRDF (float3 diffColor, float3 specColor, float translucent, float oneMinusReflectivity, float smoothness,
    float3 normal, float3 viewDir,
    UnityLight light)
{
    float perceptualRoughness = SmoothnessToPerceptualRoughness (smoothness);
    float3 floatDir = Unity_SafeNormalize (float3(light.dir) + viewDir);

    float nv = dot(normal, viewDir);    // This abs allow to limit artifact

    float nl = saturate(dot(normal, light.dir));
    float nh = saturate(dot(normal, floatDir));

    float lh = saturate(dot(light.dir, floatDir));

    // Diffuse term
    float diffuseTerm = max(0, DisneyDiffuse(nv, nl, lh, perceptualRoughness) * nl);
    diffuseTerm = lerp(diffuseTerm, 1, translucent * 0.5);

    //return diffuseTerm;
    //Diffuse = DisneyDiffuse(NoV, NoL, LoH, SmoothnessToPerceptualRoughness (smoothness)) * NoL;
    // Specular term
    // HACK: theoretically we should divide diffuseTerm by Pi and not multiply specularTerm!
    // BUT 1) that will make shader look significantly darker than Legacy ones
    // and 2) on engine side "Non-important" lights have to be divided by Pi too in cases when they are injected into ambient SH
    float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
#if UNITY_BRDF_GGX
    // GGX with roughtness to 0 would mean no specular at all, using max(roughness, 0.002) here to match HDrenderloop roughtness remapping.
    roughness = max(roughness, 0.002);
    float V = 1;// SmithJointGGXVisibilityTerm(nl, nv, roughness);
    float D = GGXTerm (nh, roughness);
#else
    // Legacy
    float V = 1;// SmithBeckmannVisibilityTerm(nl, nv, roughness);
    float D = NDFBlinnPhongNormalizedTerm (nh, PerceptualRoughnessToSpecPower(perceptualRoughness));
#endif

    float specularTerm = V * D * UNITY_PI; // Torrance-Sparrow model, Fresnel is applied later

#   ifdef UNITY_COLORSPACE_GAMMA
        specularTerm = sqrt(max(1e-4h, specularTerm));
#   endif

    // specularTerm * nl can be NaN on Metal in some cases, use max() to make sure it's a sane value
    specularTerm = max(0, specularTerm * nl);
#if defined(_SPECULARHIGHLIGHTS_OFF)
    specularTerm = 0.0;
#endif

    // To provide true Lambert lighting, we need to be able to kill specular completely.
    specularTerm *= any(specColor);

    float3 color = (diffColor * diffuseTerm + specularTerm * FresnelTerm (specColor, lh)) * light.color;

    return float4(color, 1);
}

void UnityDeferredCalculateLightParamsPBR(
    unity_v2f_deferred i,
    out float3 outWorldPos,
    out float2 outUV,
    out half3 outLightDir,
    out float outAtten,
    out float outFadeDist)
{
    i.ray = i.ray * (_ProjectionParams.z / i.ray.z);
    float2 uv = i.uv.xy / i.uv.w;

    // read depth and reconstruct world position
    float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
    depth = Linear01Depth(depth);
    float4 vpos = float4(i.ray * depth, 1);
    float3 wpos = mul(unity_CameraToWorld, vpos).xyz;

    float fadeDist = UnityComputeShadowFadeDistance(wpos, vpos.z);

    // spot light case
#if defined (SPOT)
    float3 tolight = _LightPos.xyz - wpos;
    half3 lightDir = normalize(tolight);

    float4 uvCookie = mul(unity_WorldToLight, float4(wpos, 1));
    // negative bias because http://aras-p.info/blog/2010/01/07/screenspace-vs-mip-mapping/
    float atten = tex2Dbias(_LightTexture0, float4(uvCookie.xy / uvCookie.w, 0, -8)).w;
    atten *= uvCookie.w < 0;
    float att = dot(tolight, tolight) * _LightPos.w;
    atten *= tex2D(_LightTextureB0, att.rr).r;

    atten *= UnityDeferredComputeShadow(wpos, fadeDist, uv);

    // directional light case
#elif defined (DIRECTIONAL) || defined (DIRECTIONAL_COOKIE)
    half3 lightDir = -_LightDir.xyz;
    float atten = 1.0;

    atten *= UnityDeferredComputeShadow(wpos, fadeDist, uv);

#if defined (DIRECTIONAL_COOKIE)
    atten *= tex2Dbias(_LightTexture0, float4(mul(unity_WorldToLight, half4(wpos, 1)).xy, 0, -8)).w;
#endif //DIRECTIONAL_COOKIE

    // point light case
#elif defined (POINT) || defined (POINT_COOKIE)
    float3 tolight = wpos - _LightPos.xyz;
    half3 lightDir = -normalize(tolight);

    float att = dot(tolight, tolight) * _LightPos.w;

    // custom PBR clamped falloff
    float atten =  saturate(1 - att.r) / att.r * 0.1;
    float softMax = 50;
    atten = clamp((softMax * atten) / (atten + softMax), 0, softMax);

    atten *= UnityDeferredComputeShadow(tolight, fadeDist, uv);

#if defined (POINT_COOKIE)
    atten *= texCUBEbias(_LightTexture0, float4(mul(unity_WorldToLight, half4(wpos, 1)).xyz, -8)).w;
#endif //POINT_COOKIE
#else
    half3 lightDir = 0;
    float atten = 0;
#endif

    outWorldPos = wpos;
    outUV = uv;
    outLightDir = lightDir;
    outAtten = atten;
    outFadeDist = fadeDist;
}

float4 CalculateLight (unity_v2f_deferred i)
{
    float3 wpos;
    float2 uv;
    float atten, fadeDist;
    UnityLight light;
    UNITY_INITIALIZE_OUTPUT(UnityLight, light);
    UnityDeferredCalculateLightParamsPBR(i, wpos, uv, light.dir, atten, fadeDist);

    light.color = _LightColor.rgb * atten;

    // unpack Gbuffer
    float4 gbuffer0 = tex2D (_CameraGBufferTexture0, uv);
    float4 gbuffer1 = tex2D (_CameraGBufferTexture1, uv);
    float4 gbuffer2 = tex2D(_CameraGBufferTexture2, uv);
    UnityStandardData data = UnityStandardDataFromGbuffer(gbuffer0, gbuffer1, gbuffer2);
    half translucent = 1 - gbuffer2.a;

    float3 eyeVec = normalize(wpos-_WorldSpaceCameraPos);
    float oneMinusReflectivity = 1 - SpecularStrength(data.specularColor.rgb);

    float4 res = BRDF (data.diffuseColor, data.specularColor, translucent, oneMinusReflectivity, data.smoothness, data.normalWorld, -eyeVec, light);

    return res;
}

#ifdef UNITY_HDR_ON
half4
#else
fixed4
#endif
frag (unity_v2f_deferred i) : SV_Target
{
    float4 c = CalculateLight(i);
    #ifdef UNITY_HDR_ON
    return c;
    #else
    return exp2(-c);
    #endif
}

ENDCG
}


// Pass 2: Final decode pass.
// Used only with HDR off, to decode the logarithmic buffer into the main RT
Pass {
    ZTest Always Cull Off ZWrite Off
    Stencil {
        ref [_StencilNonBackground]
        readmask [_StencilNonBackground]
        // Normally just comp would be sufficient, but there's a bug and only front face stencil state is set (case 583207)
        compback equal
        compfront equal
    }

CGPROGRAM
#pragma target 3.0
#pragma vertex vert
#pragma fragment frag
#pragma exclude_renderers nomrt

#include "UnityCG.cginc"

sampler2D _LightBuffer;
struct v2f {
    float4 vertex : SV_POSITION;
    float2 texcoord : TEXCOORD0;
};

v2f vert (float4 vertex : POSITION, float2 texcoord : TEXCOORD0)
{
    v2f o;
    o.vertex = UnityObjectToClipPos(vertex);
    o.texcoord = texcoord.xy;
#ifdef UNITY_SINGLE_PASS_STEREO
    o.texcoord = TransformStereoScreenSpaceTex(o.texcoord, 1.0f);
#endif
    return o;
}

fixed4 frag (v2f i) : SV_Target
{
    return -log2(tex2D(_LightBuffer, i.texcoord));
}
ENDCG
}

}
Fallback Off
}

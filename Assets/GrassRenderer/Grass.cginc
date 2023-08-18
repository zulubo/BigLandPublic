

#ifdef LIGHTING_PASS
#include "UnityStandardCore.cginc"
#endif


struct v2f
{
#ifdef SHADOW_PASS
    V2F_SHADOW_CASTER;
#else
    float4 pos : SV_POSITION;
    float4 tex : TEXCOORD0;
    float4 color : COLOR0;
#endif
#ifdef LIGHTING_PASS
    float3 eyeVec                         : TEXCOORD1;
    float4 tangentToWorldAndPackedData[3] : TEXCOORD2;    // [3x3:tangentToWorld | 1x3:viewDirForParallax or worldPos]
    half4 ambientOrLightmapUV             : TEXCOORD5;    // SH or Lightmap UVs

#if UNITY_REQUIRE_FRAG_WORLDPOS && !UNITY_PACK_WORLDPOS_WITH_TANGENT
    float3 posWorld                     : TEXCOORD6;
#endif
#endif
};

struct grassStruct {
    uint packedXZR; // 10X 10Z, 12Random
    float height;
    //uint color; // R8 G8 B8
};

uniform float _BladeSizeVariation;
uniform float _Stiffness;
uniform float _Weight;

uniform float4x4 _ObjectToWorld;
uniform float _NumInstances;
uniform float2 _ChunkSize;
uniform float _BladeSize;
StructuredBuffer<grassStruct> GrassBuffer;
uniform float _CullDist;
uniform float3 _ViewPos;
uniform half _Specular;
uniform half _Smoothness;
uniform sampler2D _NormalMap;
uniform half _NormalStrength;

#define tau 6.28318
#define halfpi 1.570796

float2 rotate(float2 v, float angle)
{
    float s = sin(angle);
    float c = cos(angle);
    float2x2 rotationMatrix = float2x2(c, -s, s, c);
    return mul(v, rotationMatrix);
}

void rotate(inout appdata_full v, float angle)
{
    float s = sin(angle);
    float c = cos(angle);
    float2x2 rotationMatrix = float2x2(c, -s, s, c);
    v.vertex.xz = mul(v.vertex.xz, rotationMatrix);
    v.normal.xz = mul(v.normal.xz, rotationMatrix);
    v.tangent.xz = mul(v.tangent.xz, rotationMatrix);
}

float3 sway(float3 v, float2 distance) 
{
    float d = length(distance);
    if (d > 0)
    {
        float2 dir = distance / d;
        float cd = saturate(0.5 * d) - 1;
        d = saturate(1 - cd * cd); // smooth clamping

        float h = v.y;
        v.y += h * cos(h * d * halfpi);
        v.xz += h * sin(h * d * halfpi) * dir;
    }
    return v;
}

void sway(inout appdata_full v, float2 distance)
{
    float3 bitangent = cross(v.normal, v.tangent);

    // perform sway function on slightly offset points
    float3 posPlusTangent = sway(v.vertex.xyz + v.tangent * 0.01, distance);
    float3 posPlusBitangent = sway(v.vertex.xyz + bitangent * 0.01, distance);
    v.vertex.xyz = sway(v.vertex.xyz, distance);

    // reconstruct normal from swayed points
    v.tangent = float4(normalize(posPlusTangent - v.vertex.xyz), 0);
    bitangent = posPlusBitangent - v.vertex.xyz;
    v.normal = normalize(cross(v.tangent, bitangent));
}



v2f vert(appdata_full v, uint instanceID : SV_InstanceID)
{
    v2f o;

    // unpack data from grass struct
    grassStruct packed = GrassBuffer[instanceID];
    float posx = float(packed.packedXZR & 0x3FF) / 1023.0 * _ChunkSize.x;
    float posz = float((packed.packedXZR >> 10) & 0x3FF) / 1023.0 * _ChunkSize.y;
    float rot = float((packed.packedXZR >> 20) & 0xF) / 15.0 * tau;
    float scl = float((packed.packedXZR >> 24) & 0xF) / 15.0;
    scl = lerp(1, scl, _BladeSizeVariation);
    float phase = float((packed.packedXZR >> 28) & 0xF) / 15.0 * tau;
    //float3 color = float3(float(packed.color & 0xFF) / 255.0, float((packed.color >> 8) & 0xFF) / 255.0, float((packed.color >> 16) & 0xFF) / 255.0);

    // calculate blade size
    float bladeSize = _BladeSize * scl;

    // rotate each blade randomly
    rotate(v, rot);

    // slump blades over depending on size
    float2 slump = rotate(float2(0, _Weight * scl), rot);

    // apply wind sway
    float swayFreq = tau / bladeSize * _Stiffness;
    float wind = sin(_Time.g * swayFreq + phase);
    sway(v, float3(0.1, 0, 0) * wind + slump);

    // shrink blades in distance
    float3 bladePos = float3(posx, packed.height, posz);
    float3 offset = bladePos - _ViewPos;
    offset.y *= 0.1; // less vertical fading
    float cullScl = 1 - pow(saturate(length(offset) / _CullDist / scl), 3);

    // apply scaling
    v.vertex.xyz *= bladeSize * cullScl;

    // apply final transformations
    v.vertex.xyz += bladePos;
    v.vertex.xyz = mul(_ObjectToWorld, v.vertex);
    o.pos = mul(UNITY_MATRIX_VP, v.vertex);

#ifndef SHADOW_PASS
    o.color = _Color;
    o.tex = v.texcoord;
#endif

#ifdef LIGHTING_PASS
    float4 posWorld = v.vertex;
#if UNITY_REQUIRE_FRAG_WORLDPOS
#if UNITY_PACK_WORLDPOS_WITH_TANGENT
    o.tangentToWorldAndPackedData[0].w = posWorld.x;
    o.tangentToWorldAndPackedData[1].w = posWorld.y;
    o.tangentToWorldAndPackedData[2].w = posWorld.z;
#else
    o.posWorld = posWorld.xyz;
#endif
#endif
    //o.pos = UnityObjectToClipPos(v.vertex);

    o.tex = v.texcoord;
    o.eyeVec = NormalizePerVertexNormal(posWorld.xyz - _WorldSpaceCameraPos);
    float3 normalWorld = UnityObjectToWorldNormal(v.normal);
    float4 tangentWorld = float4(UnityObjectToWorldDir(v.tangent.xyz), v.tangent.w);

    float3x3 tangentToWorld = CreateTangentToWorldPerVertex(normalWorld, tangentWorld.xyz, tangentWorld.w);
    o.tangentToWorldAndPackedData[0].xyz = tangentToWorld[0];
    o.tangentToWorldAndPackedData[1].xyz = tangentToWorld[1];
    o.tangentToWorldAndPackedData[2].xyz = tangentToWorld[2];

    o.ambientOrLightmapUV = 0;
#ifdef UNITY_SHOULD_SAMPLE_SH
    o.ambientOrLightmapUV.rgb = ShadeSHPerVertex(normalWorld, o.ambientOrLightmapUV.rgb);
#endif
#endif
    return o;
}

Shader "Nature/GrassUnlit"
{
    Properties
    {
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
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            uniform float _BladeSizeVariation;
            uniform float _Stiffness;
            uniform float _Weight;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR0;
            };

            struct grassStruct {
                uint packedXZR; // 10X 10Z, 12Random
                float height;
                //uint color; // R8 G8 B8
            };

            uniform float4x4 _ObjectToWorld;
            uniform float _NumInstances;
            uniform float2 _ChunkSize;
            uniform float _BladeSize;
            StructuredBuffer<grassStruct> GrassBuffer;
            uniform float _CullDist;
            uniform float3 _ViewPos;

#define tau 6.28318
#define halfpi 1.570796

            float2 rotate(float2 p, float angle) 
            {
                float s = sin(angle);
                float c = cos(angle);
                float2x2 rotationMatrix = float2x2(c, -s, s, c);
                return mul(p, rotationMatrix);
            }

            float3 sway(float3 v, float2 distance) 
            {
                float d = length(distance);
                if (d == 0) return v;
                float2 dir = distance / d;
                float cd = saturate(0.5 * d) - 1;
                d = saturate(1 - cd * cd); // smooth clamping

                float h = v.y;
                v.y += h * cos(h * d * halfpi);
                v.xz += h * sin(h * d * halfpi) * dir;

                return v;
            }


            v2f vert(appdata_base v, uint instanceID : SV_InstanceID)
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
                v.vertex.xz = rotate(v.vertex.xz, rot);

                // slump blades over depending on size
                float2 slump = rotate(float2(0, _Weight * scl), rot);

                // apply wind sway
                float swayFreq = tau / bladeSize * _Stiffness;
                float wind = sin(_Time.g * swayFreq + phase);
                v.vertex.xyz = sway(v.vertex.xyz, float3(0.1, 0, 0) * wind + slump);

                // shrink blades in distance
                float3 bladePos = float3(posx, packed.height, posz);
                float cullScl = 1 - pow(saturate(length(bladePos - _ViewPos) / _CullDist / scl), 5);

                // apply scaling
                v.vertex.xyz *= bladeSize * cullScl;

                // apply final transformations
                v.vertex.xyz += bladePos;
                v.vertex.xyz = mul(_ObjectToWorld, v.vertex);
                o.pos = mul(UNITY_MATRIX_VP, v.vertex);
                o.color = 1;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}
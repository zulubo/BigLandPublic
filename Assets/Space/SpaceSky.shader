Shader "Skybox/SpaceSkybox"
{
    Properties
    {
        [HDR] _Color("Background Color",Color)=(1,1,1,1)
        _MainTex("Cubemap", CUBE) = "black" {}
        [HDR] _StarsColor("Stars Color",Color) = (1,1,1,1)
        _StarsTex("Stars Texture", 2D) = "black" {}
        [Toggle(ENABLE_RING)]_RingEnable("Ring Enabled", Float) = 1
        [HDR]_RingColor("Ring Color", Color) = (1,1,1,1)
        _RingTex("Ring Texture", 2D) = "white" {}
        _RingRadius("Ring Radius", Float) = 1.5
        _RingWidth ("Ring Width", Float) = 1.5
        _SunRadius("Sun Radius", Range(0.0001, 0.1)) = 0.01
        _SunBrightness("Sun Brightness", Float) = 10
        
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ ENABLE_RING

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 skyView : TEXOORD0;
#if ENABLE_RING
                float3 ringCamPos : TEXOORD1;
                float3 ringView : TEXOORD2;
#endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 _Color;
            samplerCUBE _MainTex;

            float4 _StarsColor;
            sampler2D _StarsTex;
            float4 _StarsTex_ST;

#if ENABLE_RING
            float4 _RingColor;
            sampler2D _RingTex;
            half _RingRadius;
            half _RingWidth;
            float3 _RingSunDir;
#endif

            float4 _SunColor;
            float3 _SunDir;
            half _SunRadius;
            half _SunBrightness;

            float4x4 worldToSkySpace;
            float4x4 worldToRingSpace;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.skyView = mul(worldToSkySpace, float4(v.vertex.xyz, 0));
#if ENABLE_RING
                o.ringCamPos = mul(worldToRingSpace, float4(_WorldSpaceCameraPos, 1));
                o.ringView = mul(worldToRingSpace, float4(v.vertex.xyz,0));
#endif
                return o;
            }

            float alerp(float4 A, float4 B, float4 T)
            {
                return (T - A) / (B - A);
            }

#if ENABLE_RING
            float4 ring(float3 cameraPos, float3 viewDir) {
                viewDir = normalize(viewDir);
                float dist = cameraPos.y / -viewDir.y;
                if (dist < 0) return 0;
                float3 intersectPos = cameraPos + viewDir * dist;
                float r = length(intersectPos.xz);
                float ruv = (r - _RingRadius) / _RingWidth;
                if (ruv < 0 || ruv > 1) return 0;

                float planetAngle = 1 * asin(1 / r);
                float sunAngle = acos(dot(_RingSunDir, -normalize(intersectPos)));
                float sunRad = _SunRadius;
                float shadow = saturate(alerp(sunAngle + sunRad, sunAngle - sunRad, planetAngle));

                float4 col = tex2D(_RingTex, float2(ruv, 0)) * _RingColor;
                col.rgb *= _SunColor * shadow;
                return col;
            }
#endif

            float4 sun(float3 viewDir)
            {
                float d = 1 - dot(viewDir, _SunDir) - _SunRadius * 0.01;
                d = d < 0 ? 1 : 0.001/(1000*d+0.1);
                return d * float4(_SunColor.rgb * _SunBrightness, 1);
            }

            float3 hash3(float2 p)
            {
                float3 q = float3(dot(p, float2(127.1, 311.7)),
                    dot(p, float2(269.5, 183.3)),
                    dot(p, float2(419.2, 371.9)));
                return frac(sin(q) * 43758.5453);
            }

            float voronoise(float2 p, float u, float v)
            {
                float k = 1.0 + 63.0 * pow(1.0 - v, 6.0);

                float2 i = floor(p);
                float2 f = frac(p);

                float2 a = float2(0.0, 0.0);
                half dm = 100;
                for (int y = -2; y <= 2; y++)
                    for (int x = -2; x <= 2; x++)
                    {
                        float2  g = float2(x, y);
                        float3  o = hash3(i + g) * float3(u, u, 1.0);
                        float2  d = g - f + o.xy;
                        dm = min(dm, length(d));
                        float w = pow(1.0 - smoothstep(0.0, 1.414, length(d)), k);
                        a += float2(o.z * w, w);
                    }
                float fw = fwidth(dm);
                return (1 - saturate(dm / fw / 2)) / fw * 0.1;
                return a.x / a.y;
            }

            float4 stars(float3 viewDir)
            {
                float2 uv = viewDir.xz;
                if (abs(viewDir.y) < 0.5)
                {
                    uv = abs(viewDir.x) > 0.5 ? viewDir.yz : viewDir.xy;
                }
                return tex2D(_StarsTex, uv * _StarsTex_ST.xy) * _StarsColor;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                i.skyView = normalize(i.skyView);

                fixed4 col = _Color * texCUBE(_MainTex, i.skyView);
                
                float4 starsCol = stars(i.skyView);
                col += starsCol;

                col += sun(normalize(i.skyView));
#if ENABLE_RING
                float4 ringCol = ring(i.ringCamPos, i.ringView);
                col = lerp(col, ringCol, ringCol.a);
#endif
                return col;
            }
            ENDCG
        }
    }
}

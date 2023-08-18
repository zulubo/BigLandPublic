Shader "Hidden/TemporalUpsampleReproject"
{
    Properties
    {
        [NoScaleOffset] _MainTex("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
#include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            uniform sampler2D _MainTex;
            uniform int2 _MainTexDimensions;
            uniform sampler2D _NewSample;
            uniform int2 _NewSampleDimensions;
            uniform sampler2D _MotionVectors;
            uniform int2 sampleOffset;
            uniform int scale;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            bool isSamplePixel(float2 uv)
            {
                return int(uv.x * _MainTexDimensions.x % scale) == sampleOffset.x &&  int(uv.y * _MainTexDimensions.y % scale) == sampleOffset.y;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (isSamplePixel(i.uv))
                { // sample fresh raymarched pixel
                    float2 sampleUV = (floor(i.uv * _NewSampleDimensions) + 0.5) / _NewSampleDimensions;
                    return tex2Dlod(_NewSample, float4(sampleUV, 0, 0));;
                }
                else 
                { // reproject old pixels using motion vectors
                    float2 vel = tex2Dlod(_MotionVectors, float4(i.uv, 0, 0));
                    float2 reprojectedUV = i.uv - vel;
                    float4 oldColor = tex2Dlod(_MainTex, float4(reprojectedUV, 0, 0));
                    float4 newColor = tex2Dlod(_NewSample, float4(i.uv - float2(sampleOffset) / _MainTexDimensions, 0, 0));
                    if (reprojectedUV.x < 0 || reprojectedUV.y < 0 || reprojectedUV.x > 1 || reprojectedUV.y > 1) 
                        return newColor;

                    float4 minColor = 9999.0, maxColor = -9999.0;
                    // Sample a 3x3 neighborhood to create a box in color space
                    for (int x = -1; x <= 1; ++x)
                    {
                        for (int y = -1; y <= 1; ++y)
                        {
                            float4 color = tex2Dlod(_NewSample, float4(i.uv + (float2(x, y) - float2(sampleOffset) / scale + 0.5) / _NewSampleDimensions, 0, 0)); // Sample neighbor
                            minColor = min(minColor, color); // Take min and max
                            maxColor = max(maxColor, color);
                        }
                    }
                    // Clamp previous color to min/max bounding box
                    oldColor = clamp(oldColor, minColor, maxColor);

                    return oldColor;
                }
            }
            ENDCG
        }
    }
}

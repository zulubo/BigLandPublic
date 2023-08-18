// shader for displaying the cloud dome rendered with CloudDome.shader
Shader "Hidden/CloudDomeDisplay"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent"}
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.screenPos = ComputeScreenPos(o.pos);

                /*
                // If we're not using single pass stereo rendering, then ComputeScreenPos will not give us the
                // correct coordinates needed when the texture contains a side-by-side stereo image.
                // In this case, we need to manually adjust the the screen coordinates, and we can determine
                // which eye is being rendered by observing the horizontal skew in the projection matrix.  If
                // the value is non-zero, then we assume that this render pass is part of a stereo camera, and
                // sign of the skew value tells us which eye.
#ifndef UNITY_SINGLE_PASS_STEREO
                if (unity_CameraProjection[0][2] < 0)
                {
                    o.screenPos.x = (o.screenPos.x * 0.5f);
                }
                else if (unity_CameraProjection[0][2] > 0)
                {
                    o.screenPos.x = (o.screenPos.x * 0.5f) + (o.screenPos.w * 0.5f);
                }
#endif*/

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return tex2Dproj(_MainTex, UNITY_PROJ_COORD(i.screenPos));
            }
            ENDCG
        }
    }
        Fallback "Mobile/Diffuse"
}

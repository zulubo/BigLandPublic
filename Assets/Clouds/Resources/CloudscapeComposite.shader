Shader "Hidden/CloudscapeComposite"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _CloudTex ("Cloud Texture", 2D) = "white" {}
    }
    SubShader
    {
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            sampler2D _CloudTex;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                fixed4 cloud = tex2D(_CloudTex, i.uv);
                col.rgb = col.rgb * (1-cloud.a) + cloud.rgb;
                return col;
            }
            ENDCG
        }
    }
}

Shader "FX/DigitalHUD"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}

        _Dispersion ("Dispersion", float) = 1.0

        [NoScaleOffset] _Noise("Noise Texture", 2D) = "black" {}
        _GlitchSpeed("Glitch Speed", Float) = 0.1
        _DispersionGlitch("Dispersion Noise", Range(0,1)) = 0.1
        _BlurGlitch("Blur Noise", Range(0,1)) = 0.1
        [Toggle] _UseAlpha("Use Texture Alpha", Float) = 0
    }
        SubShader
        {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay"}
        LOD 100

        Blend One One

        Cull Off
        ZTest Off
        ZWrite Off

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
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float4 glitch : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            sampler2D _MainTex;
            float4 _MainTex_ST;
            half _Dispersion;
            sampler2D _Noise;
            half _GlitchSpeed;
            half _DispersionGlitch;
            half _BlurGlitch;

            uniform half _UseAlpha;
            
            uniform int _FillMode;
            uniform half _Fill;


            half _RandomSeed;

            float alerp(float a, float b, float t) 
            {
                return (t - a) / (b - a);
            }

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                float2 seed = float2(frac(_RandomSeed * 28.7218), frac(_RandomSeed * -7.17845));
                float2 nuv = seed + _Time.g * _GlitchSpeed * float2(0.71943, 0.46201);
                o.glitch = tex2Dlod(_Noise, float4(nuv,0,0));
                o.glitch.x = saturate(alerp(1 - _DispersionGlitch, 1.001, o.glitch.x)) * _DispersionGlitch;
                o.glitch.y = saturate(alerp(1 - _BlurGlitch, 1.001, o.glitch.y)) * _BlurGlitch;

                o.color = v.color;
                return o;
            }

            float GetFill(float2 uv) 
            {
                float f = -1;
                [branch]
                if (_FillMode == 0)
                {
                    return 1;
                }
                else if (_FillMode == 1)
                { // left to right
                    f = uv.x;
                }
                else if (_FillMode == 2)
                { // right to left
                    f = 1 - uv.x;
                }
                else if (_FillMode == 3)
                { // bottom to top
                    f = uv.y;
                }
                else if (_FillMode == 4)
                { // top to bottom
                    f = 1 - uv.y;
                }
                else if (_FillMode == 5)
                { // radial clockwise
                    f = atan2(-uv.x + 0.5, -uv.y + 0.5) / 6.2831853 + 0.5;
                }
                else if (_FillMode == 6)
                { // radial counterclockwise
                    f = 1 - atan2(-uv.x + 0.5, -uv.y + 0.5) / 6.2831853 + 0.5;
                }

                float fw = fwidth(f);
                return saturate(alerp(_Fill + (fw * _Fill) * 2, _Fill - (fw * (1 - _Fill)) * 2, f));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // uv derivatives
                float2 uvdx = ddx(i.uv);
                float2 uvdy = ddy(i.uv);

                // tangent vectors
                float2 tx = float2(-sign(uvdy.y) * abs(uvdx.x), -sign(uvdx.y) * abs(uvdx.y)); 
                float2 ty = float2(-sign(uvdy.x) * abs(uvdy.x), -sign(uvdx.x) * abs(uvdy.y));

                //unity_CameraProjection.m11
                float scl = 1 * atan(unity_CameraProjection._m11) * _Dispersion * (1 + i.glitch.x * 6);
                tx *= scl;
                ty *= scl;

                float blur = 1 + i.glitch.y * 10;
                uvdx *= blur;
                uvdy *= blur;

                float2 ruv = i.uv + tx * 0.866 + ty * 0.5;
                float2 guv = i.uv + tx * -0.866 + ty * 0.5;
                float2 buv = i.uv + ty * -1;

                float2 ra = tex2D(_MainTex, ruv, uvdx, uvdy).ra;
                float2 ga = tex2D(_MainTex, guv, uvdx, uvdy).ga;
                float2 ba = tex2D(_MainTex, buv, uvdx, uvdy).ba;

                float r, g, b;

                [branch] if (_UseAlpha)
                {
                    r = ra.y * GetFill(ruv);
                    g = ga.y * GetFill(guv);
                    b = ba.y * GetFill(buv);
                }
                else 
                {
                    r = ra.x * ra.y * GetFill(ruv);
                    g = ga.x * ga.y * GetFill(guv);
                    b = ba.x * ba.y * GetFill(buv);
                }


                float a = max(r, max(g, b));

                float4 col = float4(r, g, b, a);

                col *= i.color.r * i.color.a;

                col.rgb *= _Color.rgb;
                col *= _Color.a;

                return col;
            }
            ENDCG
        }
    }
}

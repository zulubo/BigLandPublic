// shader for rendering an overcast sky
Shader "Nature/CloudDome"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Texture", 2D) = "white" {}
        _Scale("Texture Scale", Float) = 1
        [NoScaleOffset] _FlowMap("Flow Map", 2D) = "bump" {}
        _FlowScale("Flow Map Scale", Float) = 1
        _FlowStrength("Flow Map Strength", Float) = 1
        _FlowSpeed("Flow Speed (Clouds XY, Flow map ZW)", Vector) = (0,0,0,0)

        _Height("Detail Height", Float) = 0.2
        _Curvature("Horizon Curvature", Range(0,1)) = 0.1
        _Density("Cloud Density", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 ray : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            half _Scale;
            sampler2D _FlowMap;
            half _FlowScale;
            half _FlowStrength;
            float4 _FlowSpeed;
            half _Height;
            half _Curvature;
            half _Density;

            float3 offset;
            float3 cameraPos;
            float3 worldCameraPos;
            float4x4 inverseProjectionMatrix;
            float4x4 cameraToWorldMatrix;
            float4x4 worldToObjectMatrix;
            float4x4 objectToWorldMatrix;

            float3 _LightDir;
            float3 _LightColor;

            sampler3D AerialPerspectiveLuminance;
            sampler3D AerialPerspectiveTransmittance;
            float AerialPerspectiveNearClip;
            float AerialPerspectiveFarClip;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.screenPos = ComputeScreenPos(o.vertex);

                float far = _ProjectionParams.z;
                float2 orthoSize = unity_OrthoParams.xy;
                float isOrtho = unity_OrthoParams.w; // 0: perspective, 1: orthographic

#if UNITY_UV_STARTS_AT_TOP
                o.vertex.y *= -1;
#endif
                // Perspective: view space vertex position of the far plane
                float3 rayPers = mul(inverseProjectionMatrix, o.vertex).xyz;

                // Orthographic: view space vertex position
                float3 rayOrtho = float3(orthoSize * o.vertex.xy, 0);

                o.ray = o.ray = mul(worldToObjectMatrix, mul(cameraToWorldMatrix, lerp(rayPers, rayOrtho, isOrtho)));
#if UNITY_UV_STARTS_AT_TOP
                o.vertex.y *= -1;
#endif

                return o;
            }

            float curvature(float2 position) 
            {
                // parabolic curvature toward the horizon
                position -= cameraPos.xz;
                return -dot(position, position) * _Curvature * 0.1;
            }

            float sampleDensity(float3 position) 
            {
                position -= offset;
                position.y -= curvature(position.xz);
                float2 flow = tex2Dlod(_FlowMap, float4(position.xz * _FlowScale - _Time.g * _FlowSpeed.zw, 0, 0)) * _FlowStrength;
                float surface = tex2Dlod(_MainTex, float4(position.xz * _Scale - flow - _Time.g * _FlowSpeed.xy, 0, 0)) * -_Height;
                return (position.y > surface) * (position.y < 0) * _Density;
            }

            sampler2D _BlueNoise;
            float4 _BlueNoiseJitter;
            float4 _ScreenNoiseRes;

            /*
            // From https://www.shadertoy.com/view/4sjBDG
            float calculatePhase(float costh)
            {
                // This function was optimized to minimize (delta*delta)/reference in order to capture
                // the low intensity behavior.
                float bestParams[10];
                bestParams[0] = 9.805233e-06;
                bestParams[1] = -6.500000e+01;
                bestParams[2] = -5.500000e+01;
                bestParams[3] = 8.194068e-01;
                bestParams[4] = 1.388198e-01;
                bestParams[5] = -8.370334e+01;
                bestParams[6] = 7.810083e+00;
                bestParams[7] = 2.054747e-03;
                bestParams[8] = 2.600563e-02;
                bestParams[9] = -4.552125e-12;

                float p1 = costh + bestParams[3];
                float4 expValues = exp(float4(bestParams[1] * costh + bestParams[2], bestParams[5] * p1 * p1, bestParams[6] * costh, bestParams[9] * costh));
                float4 expValWeight = float4(bestParams[0], bestParams[4], bestParams[7], bestParams[8]);
                return dot(expValues, expValWeight);
            }*/
            float calculatePhase(float costh)
            {
                float g = -0.6;
                float g2 = g * g;
                return (1 - g2) / pow(1 + g2 - 2 * g * cos((costh / 2 + 0.5) * 3.141592), 1.5);
            }


#define MAX_RAYMARCH_STEPS_LIGHTING 16

            float lightingRaymarch(float3 pos, float phaseFunction, float dC, float mu, float random) {
                float3 rayDir = _LightDir;
                float step = _Height / MAX_RAYMARCH_STEPS_LIGHTING;
                pos += rayDir * step * random;

                float lightRayDen = 0;

                for (uint s = 0; s < MAX_RAYMARCH_STEPS_LIGHTING; s++)
                {
                    pos += rayDir * step;
                    lightRayDen += max(sampleDensity(pos), 0) * step;
                }

                float scatterAmount = 1;// lerp(0.008, 1.0, smoothstep(0.96, 0.0, mu));
                float beersLaw = exp(-lightRayDen) + 0.5 * scatterAmount * exp(-0.2 * lightRayDen) + scatterAmount * 0.2 * exp(-lightRayDen);
                return beersLaw * phaseFunction;// *lerp(0.05 + 1.5 * pow(min(1.0, dC * 8.5), 0.5), 1.0, clamp(lightRayDen * 0.4, 0.0, 1.0));
            }

            // Remap a value from the range [minOld, maxOld] to [0, 1]
            float remap01(float minOld, float maxOld, float val) {
                return saturate((val - minOld) / (maxOld - minOld));
            }

#define MAX_RAYMARCH_STEPS 32

            float3 raymarch(float3 rayOrigin, float3 rayDir, float2 screenUV)
            {
                float3 pos = rayOrigin;

                if (rayDir.y < 0.9) {
                    // solve ray intersection with sky curvature parabola
                    float c = -_Curvature * 0.1 - 0.00001;
                    float run = length(rayDir.xz);
                    float slope = rayDir.y / run;
                    float a = (slope + sqrt(slope * slope + 4 * c * (pos.y + _Height))) / (2 * c);
                    float b = (slope - sqrt(slope * slope + 4 * c * (pos.y + _Height))) / (2 * c);

                    float intersect = max(0, max(a, b));

                    pos.xz += rayDir.xz / run * intersect;
                    pos.y += slope * intersect;
                }
                else {
                    // for high angles, use plane intersection to avoid weird artifacts
                    pos += rayDir * (-_Height - pos.y) / rayDir.y;
                }

                float4 noise = tex2Dlod(_BlueNoise, float4(screenUV * (_ScreenNoiseRes.xy / _ScreenNoiseRes.zw) + _BlueNoiseJitter,0,0));

                float fineStep = _Height / MAX_RAYMARCH_STEPS / (rayDir.y + 0.2 + noise.g * 0.1);
                float coarseStep = fineStep * 5;
                pos += rayDir * coarseStep * noise.r;
                bool fineStepping = false;
                float fineStepDist = 0;
                float lightRayDen = 0;
                float mu = dot(_LightDir, rayDir);
                float phaseFunction = calculatePhase(mu);
                float T = 1.;
                float3 color = 0;

                float3 surfacePos = 0;

                for (uint s = 0; s < MAX_RAYMARCH_STEPS; s++)
                {
                    float step = fineStepping ? fineStep : coarseStep;
                    pos += rayDir * step;

                    float density = sampleDensity(pos);

                    if (!fineStepping && density > 0) {
                        // back up when a coarse step enters cloud
                        pos -= rayDir * step;
                        fineStepDist = 0;
                        fineStepping = true;
                        s--;
                        continue;
                    }

                    if (fineStepping) fineStepDist += step;
                    if (fineStepping && fineStepDist > coarseStep && density <= 0) {
                        // switch back to coarse stepping
                        fineStepping = false;
                    }

                    if (density > 0.)
                    {
                        if (surfacePos.x == 0) surfacePos = pos; // surface at first depth sample

                        float intensity = lightingRaymarch(pos, phaseFunction, density, mu, noise.g);
                        float3 radiance = _LightColor * intensity;
                        radiance *= density;
                        float ed = exp(-density * step);
                        color += T * (radiance - radiance * ed) / density;   // By Seb Hillaire                  
                        T *= ed;
                        if (T <= 0.05)
                            break;
                    }
                }
                T = saturate(T / 0.95 - 0.05);

                // reconstruct depth to sample atmospheric scattering textures
                surfacePos = mul(objectToWorldMatrix, float4(surfacePos, 1));
                float sceneDepth = length(worldCameraPos - surfacePos);
                float depthT = remap01(AerialPerspectiveNearClip, AerialPerspectiveFarClip, sceneDepth);
                
                depthT = saturate(log10(9 * (depthT + 0.111111111)));

                float3 transmittance = tex3Dlod(AerialPerspectiveTransmittance, float4(screenUV, depthT, 0)).rgb;
                float3 luminance = tex3Dlod(AerialPerspectiveLuminance, float4(screenUV, depthT, 0)).rgb;

                color = color * transmittance + luminance;

                // background sky
                color += (T) * tex3Dlod(AerialPerspectiveLuminance, float4(screenUV, 1, 0)).rgb;

                return color;
            }


            fixed4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.ray).xyz;
                float3 col = raymarch(cameraPos, dir, i.screenPos);

                return float4(col, 1);
            }
            ENDCG
        }

        Pass
        { // MOTION VECTOR PASS
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 ray : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            half _Scale;
            sampler2D _FlowMap;
            half _FlowScale;
            half _FlowStrength;
            float4 _FlowSpeed;
            half _Height;
            half _Curvature;
            half _Density;

            float3 offset;
            float3 cameraPos;
            float3 worldCameraPos;
            float4x4 inverseProjectionMatrix;
            float4x4 cameraToWorldMatrix;
            float4x4 worldToCameraMatrix;
            float4x4 worldToObjectMatrix;
            float4x4 objectToWorldMatrix;
            float4x4 _CurrentVP;
            float4x4 _PreviousVP;

            float3 _LightDir;
            float3 _LightColor;

            sampler3D AerialPerspectiveLuminance;
            sampler3D AerialPerspectiveTransmittance;
            float AerialPerspectiveNearClip;
            float AerialPerspectiveFarClip;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.screenPos = ComputeScreenPos(o.vertex);

                float far = _ProjectionParams.z;
                float2 orthoSize = unity_OrthoParams.xy;
                float isOrtho = unity_OrthoParams.w; // 0: perspective, 1: orthographic

#if UNITY_UV_STARTS_AT_TOP
                o.vertex.y *= -1;
#endif
                // Perspective: view space vertex position of the far plane
                float3 rayPers = mul(inverseProjectionMatrix, o.vertex).xyz;

                // Orthographic: view space vertex position
                float3 rayOrtho = float3(orthoSize * o.vertex.xy, 0);

                o.ray = mul(worldToObjectMatrix, mul(cameraToWorldMatrix, lerp(rayPers, rayOrtho, isOrtho)));
#if UNITY_UV_STARTS_AT_TOP
                o.vertex.y *= -1;
#endif

                return o;
            }

            float curvature(float2 position)
            {
                // parabolic curvature toward the horizon
                position -= cameraPos.xz;
                return -dot(position, position) * _Curvature * 0.1;
            }

            float sampleDensity(float3 position)
            {
                position.y -= curvature(position.xz);
                position -= offset;
                float2 flow = tex2Dlod(_FlowMap, float4(position.xz * _FlowScale - _Time.g * _FlowSpeed.zw, 0, 0)) * _FlowStrength;
                float surface = tex2Dlod(_MainTex, float4(position.xz * _Scale - flow - _Time.g * _FlowSpeed.xy, 0, 0)) * -_Height;
                return (position.y > surface) * (position.y < 0) * _Density;
            }

            float3 sampleVelocity(float3 position) 
            {
                // avoid weird velocities in distance
                if (dot(position.xz - cameraPos.xz, position.xz - cameraPos.xz) > 100000000) return 0;

                position -= offset;
                return float3(_FlowSpeed.x / _Scale, 0, _FlowSpeed.y / _Scale);
            }

            sampler2D _BlueNoise;
            float4 _BlueNoiseJitter;
            float4 _ScreenNoiseRes;


            // Remap a value from the range [minOld, maxOld] to [0, 1]
            float remap01(float minOld, float maxOld, float val) {
                return saturate((val - minOld) / (maxOld - minOld));
            }

#define MAX_RAYMARCH_STEPS 16

            float3 raymarch(float3 rayOrigin, float3 rayDir, float2 screenUV)
            {
                float3 pos = rayOrigin;

                if (rayDir.y < 0.9) {
                    // solve ray intersection with sky curvature parabola
                    float c = -_Curvature * 0.1 - 0.00001;
                    float run = length(rayDir.xz);
                    float slope = rayDir.y / run;
                    float a = (slope + sqrt(slope * slope + 4 * c * (pos.y + _Height))) / (2 * c);
                    float b = (slope - sqrt(slope * slope + 4 * c * (pos.y + _Height))) / (2 * c);

                    float intersect = max(0, max(a, b));

                    pos.xz += rayDir.xz / run * intersect;
                    pos.y += slope * intersect;
                }
                else {
                    // for high angles, use plane intersection to avoid weird artifacts
                    pos += rayDir * (-_Height - pos.y) / rayDir.y;
                }

                float4 noise = tex2Dlod(_BlueNoise, float4(screenUV * (_ScreenNoiseRes.xy / _ScreenNoiseRes.zw) + _BlueNoiseJitter,0,0));

                float fineStep = _Height / MAX_RAYMARCH_STEPS / (rayDir.y + 0.2 + noise.g * 0.1);
                float coarseStep = fineStep * 5;
                pos += rayDir * coarseStep * noise.r;
                bool fineStepping = false;
                float fineStepDist = 0;
                for (uint s = 0; s < MAX_RAYMARCH_STEPS; s++)
                {
                    float step = fineStepping ? fineStep : coarseStep;
                    pos += rayDir * step;

                    float density = sampleDensity(pos);

                    if (!fineStepping && density > 0) {
                        // back up when a coarse step enters cloud
                        pos -= rayDir * step;
                        fineStepDist = 0;
                        fineStepping = true;
                        s--;
                        continue;
                    }

                    if (fineStepping) fineStepDist += step;
                    if (fineStepping && fineStepDist > coarseStep && density <= 0) {
                        // switch back to coarse stepping
                        fineStepping = false;
                    }

                    if (density > 0.)
                    {
                        return pos;
                    }
                }
                return 0;
            }


            fixed4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.ray).xyz;
                float3 pos = raymarch(cameraPos, dir, i.screenPos);
                if (dot(pos, pos) == 0) return 0;

                pos = mul(objectToWorldMatrix, float4(pos, 1));
                float depth = mul(worldToCameraMatrix, float4(pos, 1)).z;

                float3 velocity = mul(objectToWorldMatrix, float4(sampleVelocity(pos) * unity_DeltaTime.x * 0.5, 0));

                float4 currentPos = mul(_CurrentVP, float4(pos, 1));
                float4 oldPos = mul(_PreviousVP, float4(pos - velocity, 1));

                // motion vectors
                return float4(currentPos.xy / currentPos.w * 0.5 - oldPos.xy / oldPos.w * 0.5, 0, 0);
            }
            ENDCG
        }
    }
}

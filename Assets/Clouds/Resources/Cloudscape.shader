// Shader for rendering volumetric clouds
Shader "Hidden/Cloudscape"
{
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

            uniform float3 planetPosition;
            uniform float3 cloudHeights;
            uniform float maxStepLength;
            uniform float stepLengthExponent;
            uniform sampler3D shapeNoise;
            uniform sampler3D detailNoise;
            uniform sampler2D heightFalloff;
            uniform half _Density;
            uniform half shapeScale;
            uniform half detailScale;
            uniform half detailStrength;
            
            uniform float3 offset;
            uniform float3 cameraPos;
            uniform float3 worldCameraPos;
            uniform float4x4 inverseProjectionMatrix;
            uniform float4x4 cameraToWorldMatrix;
            uniform float4x4 worldToCameraMatrix;
            uniform float4x4 worldToObjectMatrix;
            uniform float4x4 objectToWorldMatrix;
            uniform sampler2D _CameraDepthTexture;

            uniform float3 _LightDir;
            uniform float3 _LightColor;
            uniform float3 _AmbientLight;
            uniform half _AmbientOcclusion;

            uniform sampler3D AerialPerspectiveLuminance;
            uniform sampler3D AerialPerspectiveTransmittance;
            uniform float AerialPerspectiveNearClip;
            uniform float AerialPerspectiveFarClip;

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

                o.ray = o.ray = mul(cameraToWorldMatrix, lerp(rayPers, rayOrtho, isOrtho));
#if UNITY_UV_STARTS_AT_TOP
                o.vertex.y *= -1;
#endif
                return o;
            }

            // returns (near, far)
            // returns -1 when sphere is missed
            float2 rayIntersectSphere(float3 rayPos, float3 rayDir, float radius) {
                float b = dot(rayPos, rayDir);
                float c = dot(rayPos, rayPos) - radius * radius;
                if (c > 0 && b > 0) {
                    return -1;
                }

                float discr = b * b - c;
                if (discr < 0) {
                    return -1;
                }

                discr = sqrt(discr);

                return float2(-b - discr, -b + discr);
            }

            // main density function for clouds
            float sampleDensity(float3 position) 
            {
                float h = (length(position) - cloudHeights.x) / cloudHeights.z;
                if (h < 0 || h > 1) return 0;

                float3 noise = tex3Dlod(shapeNoise, float4(position / shapeScale, 0)).rgb;
                float d = noise.r * 0.625 + noise.g * 0.25 + noise.b * 0.125;
                noise = tex3Dlod(detailNoise, float4(position / detailScale, 0)).rgb;
                d += (noise.r * 0.625 + noise.g * 0.25 + noise.b * 0.125 - 1) * detailStrength;
                d += tex2Dlod(heightFalloff, float4(h, 0, 0, 0)) * 2 - 1;
                d = saturate(d);
                return d * _Density;
            }

            float sampleDensityLowDetail(float3 position)
            {
                float h = (length(position) - cloudHeights.x) / cloudHeights.z;
                if (h < 0 || h > 1) return 0;

                float3 noise = tex3Dlod(shapeNoise, float4(position / shapeScale, 0)).rgb;
                float d = noise.r * 0.625 + noise.g * 0.25 + noise.b * 0.125;
                d += tex2Dlod(heightFalloff, float4(h, 0, 0, 0)) * 2 - 1;
                d = saturate(d);

                return d * _Density;
            }

            sampler2D _BlueNoise;
            float4 _BlueNoiseJitter;
            float4 _ScreenNoiseRes;

            // Henyey-Greenstein phase function
            float calculatePhase(float costh) 
            {
                float g = -0.4;
                float g2 = g * g;
                return (1 - g2) / pow(1 + g2 - 2 * g * cos((costh / 2 + 0.5) * 3.141592), 1.5);
            }
            
#define MAX_RAYMARCH_STEPS_LIGHTING 16
            float lightingRaymarch(float3 pos, float phaseFunction, float dC, float mu, float random) 
            {
                // ray towards sun
                float3 rayDir = _LightDir;
                float step = cloudHeights.z / 6 / MAX_RAYMARCH_STEPS_LIGHTING;
                pos += rayDir * step * random;

                float lightRayDen = 0;

                for (uint s = 0; s < MAX_RAYMARCH_STEPS_LIGHTING; s++)
                {
                    pos += rayDir * step;
                    lightRayDen += max(sampleDensity(pos), 0) * step;
                }

                // lighting math!
                float scatterAmount = lerp(0.01, 1.0, smoothstep(0.96, 0.0, mu));
                float beersLaw = exp(-lightRayDen) + 0.5 * scatterAmount * exp(-0.1 * lightRayDen) + scatterAmount * 0.4 * exp(-0.02 * lightRayDen);
                return beersLaw * phaseFunction * lerp(0.05 + 1.5 * pow(min(1.0, dC * 8.5), 0.5), 1.0, clamp(lightRayDen * 0.4, 0.0, 1.0));
            }

            // Remap a value from the range [minOld, maxOld] to [0, 1]
            float remap01(float minOld, float maxOld, float val) {
                return saturate((val - minOld) / (maxOld - minOld));
            }

#define MAX_RAYMARCH_STEPS 96

            float4 raymarch(float3 rayOrigin, float3 rayDir, float2 screenUV)
            {
                float3 pos = rayOrigin - planetPosition;
                float startD = 0;
                float endD = 0;
                float originRadSqr = dot(pos, pos);
                float2 lower = rayIntersectSphere(pos, rayDir, cloudHeights.x);
                float2 upper = rayIntersectSphere(pos, rayDir, cloudHeights.y);
                if (originRadSqr < cloudHeights.x * cloudHeights.x) 
                { // under clouds
                    startD = lower.y;
                    endD = upper.y;
                }
                else if (originRadSqr < cloudHeights.y * cloudHeights.y)
                { // inside clouds
                    startD = 0;
                    endD = lower == -1 ? upper.y : min(lower.x, upper.y);
                }
                else 
                { // above clouds
                    startD = upper.x;
                    endD = lower == -1 ? upper.y : min(lower.x, upper.y);
                }

                pos += rayDir * startD;

                float linearRayLength = -mul(worldToCameraMatrix, float4(cameraPos + rayDir,1)).z;
                float d = startD;
                float rd = 0;

                float4 noise = tex2Dlod(_BlueNoise, float4((screenUV + _BlueNoiseJitter) * (_ScreenNoiseRes.xy / _ScreenNoiseRes.zw), 0, 0));

                float fineStep = min(maxStepLength, (endD - startD) / MAX_RAYMARCH_STEPS);
                float coarseStep = fineStep * 4;
                pos += rayDir * coarseStep * noise.x;
                bool fineStepping = false;
                float fineStepDist = 0;
                float mu = dot(_LightDir, rayDir);
                float phaseFunction = calculatePhase(mu);
                float transparency = 1.;
                float3 color = 0;

                float sceneDepth = LinearEyeDepth(tex2Dlod(_CameraDepthTexture, float4(screenUV,0,0)));

                for (uint s = 0; s < MAX_RAYMARCH_STEPS; s++)
                {
                    float step = fineStepping ? fineStep : coarseStep;
                    step *= pow(stepLengthExponent, rd / cloudHeights.z);
                    pos += rayDir * step;
                    d += step;
                    rd += step;

                    // exit if ray intersects depth texture
                    if (d * linearRayLength > sceneDepth)
                        break;

                    if (!fineStepping) 
                    {
                        if (sampleDensityLowDetail(pos) > 0.)
                        {
                            // back up when a coarse step enters cloud
                            pos -= rayDir * step;
                            d -= step;
                            rd -= step;
                            fineStepDist = 0;
                            fineStepping = true;
                            s--;
                            continue;
                        }
                    }

                    float density = sampleDensity(pos);

                    if (fineStepping) fineStepDist += step;
                    if (fineStepping && fineStepDist > coarseStep && density <= 0) {
                        // switch back to coarse stepping
                        fineStepping = false;
                    }

                    if (density > 0.)
                    { // inside cloud!
                        density = saturate(density);

                        float intensity = lightingRaymarch(pos, phaseFunction, density, mu, noise.g);
                        float3 radiance = _LightColor * intensity;
                        float3 ambient = _AmbientLight * saturate(exp(-sampleDensityLowDetail(pos) / _Density * _AmbientOcclusion));
                        radiance += ambient;

                        float eyeDepth = -mul(worldToCameraMatrix, float4(pos + planetPosition, 1)).z;
                        float atmosphereDepth = remap01(AerialPerspectiveNearClip, AerialPerspectiveFarClip, eyeDepth);
                        // custom depth remapping for atmosphere textures
                        atmosphereDepth = saturate(log10(9 * (atmosphereDepth + 0.111111111)));
                        // sample atmospheric scattering 
                        float3 transmittance = tex3Dlod(AerialPerspectiveTransmittance, float4(screenUV, atmosphereDepth, 0)).rgb;
                        float3 luminance = tex3Dlod(AerialPerspectiveLuminance, float4(screenUV, atmosphereDepth, 0)).rgb;
                        radiance = radiance * transmittance + luminance;

                        float extinction = exp(-density * step);
                        radiance = (radiance - radiance * extinction);

                        color += transparency * radiance;
                        transparency *= extinction;
                        if (transparency <= 0.05)
                            break;
                    }
                }
                transparency = saturate(transparency / 0.95 - 0.05);
                return float4(color, 1 - transparency);
            }


            fixed4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.ray).xyz;
                float4 col = raymarch(cameraPos, dir, i.screenPos);
                return col;
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

            uniform float3 planetPosition;
            uniform float3 cloudHeights;
            uniform float maxStepLength;
            uniform float stepLengthExponent;
            sampler3D shapeNoise;
            sampler3D detailNoise;
            sampler2D heightFalloff;
            half _Density;
            half shapeScale;
            half detailScale;
            half detailStrength;

            float3 offset;
            float3 cameraPos;
            float3 worldCameraPos;
            float4x4 inverseProjectionMatrix;
            float4x4 cameraToWorldMatrix;
            float4x4 worldToCameraMatrix;
            float4x4 _CurrentVP;
            float4x4 _PreviousVP;

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

                o.ray = mul(cameraToWorldMatrix, lerp(rayPers, rayOrtho, isOrtho));
#if UNITY_UV_STARTS_AT_TOP
                o.vertex.y *= -1;
#endif

                return o;
            }

            // returns (near, far)
            // returns -1 when sphere is missed
            float2 rayIntersectSphere(float3 rayPos, float3 rayDir, float radius) {
                float b = dot(rayPos, rayDir);
                float c = dot(rayPos, rayPos) - radius * radius;
                if (c > 0 && b > 0) {
                    return -1;
                }

                float discr = b * b - c;
                if (discr < 0) {
                    return -1;
                }

                discr = sqrt(discr);

                return float2(-b - discr, -b + discr);
            }

            float sampleDensity(float3 position)
            {
                float h = (length(position) - cloudHeights.x) / cloudHeights.z;
                if (h < 0 || h > 1) return 0;

                float3 noise = tex3Dlod(shapeNoise, float4(position / shapeScale, 0)).rgb;
                float d = noise.r * 0.625 + noise.g * 0.25 + noise.b * 0.125;
                noise = tex3Dlod(detailNoise, float4(position / detailScale, 0)).rgb;
                d += (noise.r * 0.625 + noise.g * 0.25 + noise.b * 0.125 - 1) * detailStrength;
                d += tex2Dlod(heightFalloff, float4(h, 0, 0, 0)) * 2 - 1;
                d = saturate(d);
                return d * _Density;
            }

            float sampleDensityLowDetail(float3 position)
            {
                float h = (length(position) - cloudHeights.x) / cloudHeights.z;
                if (h < 0 || h > 1) return 0;

                float3 noise = tex3Dlod(shapeNoise, float4(position / shapeScale, 0)).rgb;
                float d = noise.r * 0.625 + noise.g * 0.25 + noise.b * 0.125;
                d += tex2Dlod(heightFalloff, float4(h, 0, 0, 0)) * 2 - 1;
                d = saturate(d);

                return d * _Density;
            }

            float3 sampleVelocity(float3 position) 
            {
                return 0;
            }

            sampler2D _BlueNoise;
            float4 _BlueNoiseJitter;
            float4 _ScreenNoiseRes;


            // Remap a value from the range [minOld, maxOld] to [0, 1]
            float remap01(float minOld, float maxOld, float val) {
                return saturate((val - minOld) / (maxOld - minOld));
            }

#define MAX_RAYMARCH_STEPS 24

            float3 raymarch(float3 rayOrigin, float3 rayDir, float2 screenUV)
            {
                float3 pos = rayOrigin - planetPosition;
                float startD = 0;
                float endD = 0;
                float originRadSqr = dot(pos, pos);
                float2 lower = rayIntersectSphere(pos, rayDir, cloudHeights.x);
                float2 upper = rayIntersectSphere(pos, rayDir, cloudHeights.y);
                if (originRadSqr < cloudHeights.x * cloudHeights.x)
                { // under clouds
                    startD = lower.y;
                    endD = upper.y;
                }
                else if (originRadSqr < cloudHeights.y * cloudHeights.y)
                { // inside clouds
                    startD = 0;
                    endD = lower == -1 ? upper.y : min(lower.x, upper.y);
                }
                else
                { // above clouds
                    startD = upper.x;
                    endD = lower == -1 ? upper.y : min(lower.x, upper.y);
                }

                pos += rayDir * startD;

                float4 noise = tex2Dlod(_BlueNoise, float4((screenUV + _BlueNoiseJitter) * (_ScreenNoiseRes.xy / _ScreenNoiseRes.zw), 0, 0));

                float fineStep = cloudHeights.z / MAX_RAYMARCH_STEPS / (abs(rayDir.y) + 0.4 + noise.g * 0.2);
                float coarseStep = fineStep * 5;
                pos += rayDir * coarseStep * noise.r;
                bool fineStepping = false;
                float fineStepDist = 0;
                float rd = 0;

                for (uint s = 0; s < MAX_RAYMARCH_STEPS; s++)
                {
                    float step = fineStepping ? fineStep : coarseStep;
                    step *= pow(stepLengthExponent, rd / cloudHeights.z);
                    pos += rayDir * step;
                    rd += step;

                    if (!fineStepping)
                    {
                        if (sampleDensityLowDetail(pos) > 0.)
                        {
                            // back up when a coarse step enters cloud
                            pos -= rayDir * step;
                            fineStepDist = 0;
                            fineStepping = true;
                            s--;
                            continue;
                        }
                    }

                    float density = sampleDensity(pos);

                    if (fineStepping) fineStepDist += step;
                    if (fineStepping && fineStepDist > coarseStep && density <= 0) {
                        // switch back to coarse stepping
                        fineStepping = false;
                    }

                    if (density > 0.)
                    {
                        return pos + planetPosition;
                    }
                }
                return 0;
            }


            fixed4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.ray).xyz;
                float3 pos = raymarch(cameraPos, dir, i.screenPos);
                if (dot(pos, pos) == 0) pos = cameraPos + dir * 10000000;

                float depth = mul(worldToCameraMatrix, float4(pos, 1)).z;

                float3 velocity = sampleVelocity(pos) * unity_DeltaTime.x * 0.5;

                float4 currentPos = mul(_CurrentVP, float4(pos, 1));
                float4 oldPos = mul(_PreviousVP, float4(pos - velocity, 1));

                // motion vectors
                return float4(currentPos.xy / currentPos.w * 0.5 - oldPos.xy / oldPos.w * 0.5, depth, 0);
            }
            ENDCG
        }
    }
}

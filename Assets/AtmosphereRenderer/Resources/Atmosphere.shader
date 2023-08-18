Shader "Hidden/Atmosphere"
{
	HLSLINCLUDE


	#include "AtmosphereCommon.hlsl"
	#include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

	sampler2D _MainTex;
	sampler2D _CameraDepthTexture;

	sampler2D TransmittanceLUT;
	//int3 AerialPerspectiveSize;
	sampler3D AerialPerspectiveLuminance;
	sampler3D AerialPerspectiveTransmittance;
	sampler2D Sky;
	//sampler2D _BlueNoise;
	float4 BlueNoise_TexelSize;

	float3 planetPosition;

	// Sun disc
	float sunDiscSize;
	float sunDiscBlurA;
	float sunDiscBlurB;

	//volume planes
	float nearClip;
	float farClip;

	// Other
	float ditherStrength;
	float aerialPerspectiveStrength;
	float skyTransmittanceWeight;

	struct appdata {
		float4 vertex : POSITION;
		float4 uv : TEXCOORD0;
	};

	struct v2f {
		float4 pos : SV_POSITION;
		float2 uv : TEXCOORD0;
		float2 auv : TEXCOORD1;
		float2 texcoordStereo : TEXCOORD2;
		float3 viewVector : TEXCOORD3;
	};

	float4 viewBounds;

	float4x4 unity_CameraInvProjection;
	float4x4 unity_CameraToWorld;

	v2f vert(appdata v) 
	{
		v2f o;
		o.pos = float4(v.vertex.xy, 0.0, 1.0);
		o.uv = TransformTriangleVertexToUV(v.vertex.xy);
#if UNITY_UV_STARTS_AT_TOP
		o.uv = o.uv * float2(1.0, -1.0) + float2(0.0, 1.0);
#endif
		o.auv = (o.uv - viewBounds.xy) / viewBounds.zw;

		float3 viewVector = mul(unity_CameraInvProjection, float4(o.uv.xy * 2 - 1, 0, 1));
		o.viewVector = mul(unity_CameraToWorld, float4(viewVector, 0));

		o.texcoordStereo = TransformStereoScreenSpaceTex(o.uv, 1.0);


		return o;
	}


	// Thanks to https://www.shadertoy.com/view/slSXRW
	float3 sunDiscWithBloom(float3 rayDir, float3 sunDir) {
		const float sunSolidAngle = sunDiscSize * PI / 180.0;
		const float minSunCosTheta = cos(sunSolidAngle);

		float cosTheta = dot(rayDir, sunDir);
		if (cosTheta >= minSunCosTheta) return 1;

		float offset = minSunCosTheta - cosTheta;
		float gaussianBloom = exp(-offset * 1000 * sunDiscBlurA) * 0.5;
		float invBloom = 1.0 / (0.02 + offset * 100 * sunDiscBlurB) * 0.01;
		return gaussianBloom + invBloom;
	}


	// Remap noise to triangular distribution
	// See pg. 45 to 57 of www.gdcvault.com/play/1023002/Low-Complexity-High-Fidelity-INSIDE
	// Thanks to https://www.shadertoy.com/view/MslGR8 See also https://www.shadertoy.com/view/4t2SDh
	float remap_noise_tri_erp(float v)
	{
		float r2 = 0.5 * v;
		float f1 = sqrt(r2);
		float f2 = 1.0 - sqrt(r2 - 0.25);
		return (v < 0.5) ? f1 : f2;
	}

	float3 getBlueNoise(float2 uv) {
		float2 screenSize = _ScreenParams.xy;

		uv = (uv * screenSize) * BlueNoise_TexelSize.xy;

		float3 blueNoise = tex2D(_BlueNoise, uv).rgb;
		float3 m = 0;
		m.r = remap_noise_tri_erp(blueNoise.r);
		m.g = remap_noise_tri_erp(blueNoise.g);
		m.b = remap_noise_tri_erp(blueNoise.b);

		float3 weightedNoise = (m * 2.0 - 0.5);
		return weightedNoise;
	}

	float3 blueNoiseDither(float3 col, float2 uv, float strength) {
		float3 weightedNoise = getBlueNoise(uv) / 255.0 * strength;

		return col + weightedNoise;
	}

	// Remap a value from the range [minOld, maxOld] to [0, 1]
	float remap01(float minOld, float maxOld, float val) {
		return saturate((val - minOld) / (maxOld - minOld));
	}

	float SampleDepth(float2 uv)
	{
		return Linear01Depth(tex2D(_CameraDepthTexture, UnityStereoTransformScreenSpaceTex(uv)));
		//return d * _ProjectionParams.z;// +CheckBounds(uv, d);
	}

	float4 frag(v2f i) : SV_Target
	{
		//return float4(1,0,0,1);

		float4 originalCol = tex2D(_MainTex, i.uv);

		float viewLength = length(i.viewVector);
		float3 viewDir = i.viewVector / viewLength;
		float rawDepth = SampleDepth(i.uv);
		float sceneDepth = rawDepth * _ProjectionParams.z * viewLength;

		if (i.auv.x < 0 || i.auv.x > 1 || i.auv.y < 0 || i.auv.y > 1) {
			return originalCol;
		}
		//return float4(i.auv,0, 1);

		//return float4(SAMPLE_TEXTURE3D(AerialPerspectiveLuminance, samplerAerialPerspectiveLuminance, float4(i.auv, aerialPerspectiveStrength, 0)).rgb, 1);

		float3 rayOrigin = _WorldSpaceCameraPos - planetPosition;
		float3 rayDir = viewDir;
		float depthT = remap01(nearClip, farClip, sceneDepth) * 1;
		// remap depth to use log curve
		depthT = saturate(log10(9 * (depthT + 0.111111111)));
		
		// Draw sky since no 'terrestial' object has been rendered here.
		// Non terrestial objects would be stuff like stars and moon, which sky should be drawn on top of
		// (sky is rendered each frame into small texture, so just need to composite here)
		if (sceneDepth >= farClip || rawDepth >= 1)
		{
			float3 skyLum = tex2D(Sky, i.auv).rgb;
			// Composite sun disc (not included in sky texture because resolution is too low for good results)
			//float3 sunDisc = sunDiscWithBloom(rayDir, dirToSun) * (sceneDepth >= farClip);
			float3 transmittance = getSunTransmittanceLUT(TransmittanceLUT, rayOrigin, rayDir);

			//skyLum += sunDisc * transmittance;

			skyLum = skyLum;

			skyLum = originalCol.rgb * lerp(dot(transmittance,1 / 3.0), transmittance, skyTransmittanceWeight) + skyLum;

			// Apply dithering to try combat banding
			skyLum = blueNoiseDither(skyLum, i.uv, ditherStrength);

			return float4(skyLum, 1);
		}

		float2 hitInfo = raySphere(0, atmosphereRadius, rayOrigin, rayDir);
		float dstToAtmosphere = hitInfo.x;
		float dstThroughAtmosphere = hitInfo.y;

		// View ray goes through atmosphere (and not blocked by anything in front of it)
		if (dstThroughAtmosphere > 0 && dstToAtmosphere < sceneDepth) {
			float3 inPoint = rayOrigin + rayDir * (dstToAtmosphere);
			float3 outPoint = rayOrigin + rayDir * min(dstToAtmosphere + dstThroughAtmosphere, sceneDepth);

			float3 transmittance = tex3Dlod(AerialPerspectiveTransmittance, float4(i.auv, depthT, 0)).rgb;
			float3 luminance = tex3Dlod(AerialPerspectiveLuminance, float4(i.auv, depthT, 0)).rgb;

			//luminance = luminance;
			luminance = originalCol.rgb * transmittance + luminance;


			float3 outputCol = blueNoiseDither(luminance, i.uv, ditherStrength);
			outputCol = lerp(originalCol.rgb, outputCol, aerialPerspectiveStrength);
			return float4(outputCol, 1);
		}

		// Not looking at atmosphere, return original colour
		return float4(originalCol.rgb, 1);
	}

	ENDHLSL

	SubShader
	{
		Cull Off ZWrite Off ZTest Always

		Pass
		{
			HLSLPROGRAM

			#pragma vertex vert
			#pragma fragment frag

			ENDHLSL
		}
	}
}
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;
using System.Collections.Generic;

[System.Serializable]
[PostProcess(typeof(AtmosphereEffectRenderer), PostProcessEvent.BeforeTransparent, "Custom/Atmosphere")]
public class AtmosphereEffect : PostProcessEffectSettings
{
    [Header("Transmittance LUT (2D)")]
    public Vector2Parameter transmittanceLUTSize = new Vector2Parameter { value = new Vector2(32,32) };

    [Header("Aerial Perpspective LUT")]
    public IntParameter numAerialScatteringSteps = new IntParameter { value = 8 };
    public Vector3Parameter aerialPerspectiveLUTSize = new Vector3Parameter { value = new Vector3(32,32,32) };


    [Header("Sky Texture")]
    // Num raymarch steps when drawing the sky (this is drawn small and upscaled, so can afford to be fairly high)
    public IntParameter numSkyScatteringSteps = new IntParameter { value = 8 };
    // Note: since sky colours change quite smoothly this can be very small (e.g. 128x64)
    // However, the vertical resolution should be increased (~128x256) so that earth shadow isn't too jaggedy
    public Vector2Parameter skyRenderSize = new Vector2Parameter { value = new Vector2(128,256) };

[Header("Later")]
    public FloatParameter ditherStrength = new FloatParameter { value = 0.8f };
    public BoolParameter filterMode = new BoolParameter { value = false };

    public override bool IsEnabledAndSupported(PostProcessRenderContext context)
    {
        return enabled && Atmosphere.atmospheres.Count > 0;
    }
}
class AtmosphereEffectRenderer : PostProcessEffectRenderer<AtmosphereEffect>
{
    public override DepthTextureMode GetCameraFlags() => DepthTextureMode.Depth;

    const string ProfilerTag = "Atmosphere Pass";

    int atmosphereBufferID = Shader.PropertyToID("AtmosphereBuffer");

    public class AtmosphereRenderData
    {
        public Camera camera;
        public RenderTexture sky;
        public RenderTexture aerialPerspectiveLuminance;
        public RenderTexture aerialPerspectiveTransmittance;
        public float nearPlane;
        public float farPlane;

    }
    public static Dictionary<Camera, AtmosphereRenderData> perCameraAtmosphere = new Dictionary<Camera, AtmosphereRenderData>();

    static Shader atmosphereShader
    {
        get
        {
            if (_atmosphereShader == null)
            {
                _atmosphereShader = (Shader)Resources.Load("Atmosphere", typeof(Shader));
            }
            return _atmosphereShader;
        }
    }
    static Shader _atmosphereShader;
    static ComputeShader skyRenderCompute
    {
        get
        {
            if (_skyRenderCompute == null)
            {
                _skyRenderCompute = (ComputeShader)Resources.Load("SkyRender", typeof(ComputeShader));
            }
            return _skyRenderCompute;
        }
    }
    static ComputeShader _skyRenderCompute;
    static ComputeShader aerialPerspectiveLUTCompute
    {
        get
        {
            if (_aerialPerspectiveLUTCompute == null)
            {
                _aerialPerspectiveLUTCompute = (ComputeShader)Resources.Load("AerialPerspective", typeof(ComputeShader));
            }
            return _aerialPerspectiveLUTCompute;
        }
    }
    static ComputeShader _aerialPerspectiveLUTCompute;
    static ComputeShader transmittanceLUTCompute
    {
        get
        {
            if (_transmittanceLUTCompute == null)
            {
                _transmittanceLUTCompute = (ComputeShader)Resources.Load("TransmittanceDepthLUT", typeof(ComputeShader));
            }
            return _transmittanceLUTCompute;
        }
    }
    static ComputeShader _transmittanceLUTCompute;
    static Texture2D blueNoise
    {
        get
        {
            if (_blueNoise == null)
            {
                _blueNoise = (Texture2D)Resources.Load("BlueNoise", typeof(Texture2D));
            }
            return _blueNoise;
        }
    }
    static Texture2D _blueNoise;

    //Material material;

    public class ShaderParamID
    {
        public static readonly int topLeftDir = Shader.PropertyToID("topLeftDir");
        public static readonly int topRightDir = Shader.PropertyToID("topRightDir");
        public static readonly int bottomLeftDir = Shader.PropertyToID("bottomLeftDir");
        public static readonly int bottomRightDir = Shader.PropertyToID("bottomRightDir");
        public static readonly int camPos = Shader.PropertyToID("camPos");
        public static readonly int nearClip = Shader.PropertyToID("nearClip");
        public static readonly int farClip = Shader.PropertyToID("farClip");

        public static readonly int dirToSun = Shader.PropertyToID("dirToSun");
        public static readonly int sunColor = Shader.PropertyToID("sunColor");
        public static readonly int overcast = Shader.PropertyToID("overcast");
        public static readonly int planetPosition = Shader.PropertyToID("planetPosition");
        public static readonly int transmittanceLUT = Shader.PropertyToID("TransmittanceLUT");
        public static readonly int sky = Shader.PropertyToID("Sky");

        public static readonly int numScatteringSteps = Shader.PropertyToID("numScatteringSteps");
        public static readonly int size = Shader.PropertyToID("size");

        public static readonly int AerialPerspectiveLuminance = Shader.PropertyToID("AerialPerspectiveLuminance");
        public static readonly int AerialPerspectiveTransmittance = Shader.PropertyToID("AerialPerspectiveTransmittance");

        public static readonly int blueNoise = Shader.PropertyToID("_BlueNoise");
        public static readonly int ditherStrength = Shader.PropertyToID("ditherStrength");
        public static readonly int AerialPerspectiveSize = Shader.PropertyToID("AerialPerspectiveSize");

        public static readonly int planetAngularSize = Shader.PropertyToID("planetAngularSize");

        public static readonly int viewBounds = Shader.PropertyToID("viewBounds");
    }

    public override void Init()
    {
        //if (material == null)
        //{
        //    material = new Material(settings.atmosphereShader);
        //}

        //if (sky == null)
        

        //if (aerialPerspectiveLuminance == null || aerialPerspectiveTransmittance == null)
        {

        }
    }
    ComputeShaderUtility.GaussianBlur blur;
    public override void Render(PostProcessRenderContext context)
    {
        if (Atmosphere.atmospheres.Count == 0) return;

        perCameraAtmosphere.TryGetValue(context.camera, out AtmosphereRenderData data);
        
        if(data == null)
        {
            data = new AtmosphereRenderData();
            perCameraAtmosphere[context.camera] = data;
        }

        {
            var skyFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;
            ComputeHelper.CreateRenderTexture(ref data.sky, (int)settings.skyRenderSize.value.x, (int)settings.skyRenderSize.value.y, FilterMode.Bilinear, skyFormat, "Sky", useMipMaps: true);
        }
        var aerialFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;
        var transmittanceFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_UNorm;
        ComputeHelper.CreateRenderTexture3D(ref data.aerialPerspectiveLuminance, (int)settings.aerialPerspectiveLUTSize.value.x,
            (int)settings.aerialPerspectiveLUTSize.value.y,
            (int)settings.aerialPerspectiveLUTSize.value.z, aerialFormat, TextureWrapMode.Clamp, "Aerial Perspective");
        ComputeHelper.CreateRenderTexture3D(ref data.aerialPerspectiveTransmittance, (int)settings.aerialPerspectiveLUTSize.value.x,
            (int)settings.aerialPerspectiveLUTSize.value.y,
            (int)settings.aerialPerspectiveLUTSize.value.z, transmittanceFormat, TextureWrapMode.Clamp, "Transmittance LUT 3D");

        var camera = context.camera;
        var planes = GeometryUtility.CalculateFrustumPlanes(camera);

        for (int i = 0; i < Atmosphere.atmospheres.Count; i++)
        {
            var a = Atmosphere.atmospheres[i];
            if (a == null) { Atmosphere.atmospheres.RemoveAt(i); i--; continue; }

            if (!GeometryUtility.TestPlanesAABB(planes, a.bounds)) continue; // frustum culling

            //Bounds viewBounds = a.ScreenSpaceBounds(camera);
            // flip vertical
            //var vbc = viewBounds.center;
            //vbc.y = 1 - vbc.y;
            //viewBounds.center = vbc;
            Bounds viewBounds = new Bounds(new Vector3(0.5f, 0.5f, 0), new Vector3(1, 1, 0));

            // rendering near plane is parallel to camera near plane, touching edge of atmosphere
            float planetDist = Vector3.Distance(camera.transform.position, a.planetPosition);
            data.nearPlane = Mathf.Max(0, planetDist - a.atmosphereRadius);
            //float farPlane = camera.nearClipPlane + Mathf.Min(a.atmosphereParameters.maxAerialPerspectiveDist, Mathf.Max(nearPlane - camera.nearClipPlane, planes[4].GetDistanceToPoint(a.planetPosition) + a.atmosphereRadius));
            data.farPlane = Mathf.Min(a.atmosphereParameters.maxAerialPerspectiveDist, Mathf.Max(data.nearPlane, planetDist + a.atmosphereRadius));

            
            Vector3 dirToSun = a.sun != null ? -a.sun.direction : (RenderSettings.sun != null ? -RenderSettings.sun.transform.forward : Vector3.forward);

            CommandBuffer cmd = context.command;
            // sky compute shader
            SetRaymarchParams(cmd, camera, viewBounds, a.planetPosition, skyRenderCompute);
            cmd.SetComputeVectorParam(skyRenderCompute, ShaderParamID.dirToSun, dirToSun);
            cmd.SetComputeVectorParam(skyRenderCompute, ShaderParamID.sunColor, a.sun.baseColor * a.sun.baseIntensity);
            cmd.SetComputeFloatParam(skyRenderCompute, ShaderParamID.overcast, a.overcast);
            cmd.SetComputeIntParam(skyRenderCompute, ShaderParamID.numScatteringSteps, settings.numSkyScatteringSteps);
            cmd.SetComputeIntParams(skyRenderCompute, ShaderParamID.size, (int)settings.skyRenderSize.value.x, (int)settings.skyRenderSize.value.y);
            cmd.SetComputeTextureParam(skyRenderCompute, 0, ShaderParamID.sky, data.sky);
            cmd.SetComputeTextureParam(skyRenderCompute, 0, ShaderParamID.transmittanceLUT, a.transmittanceLUT);
            cmd.SetComputeTextureParam(skyRenderCompute, 0, ShaderParamID.blueNoise, blueNoise);
            a.sharedAtmosphereValues.ApplyCommand(cmd, skyRenderCompute);
            Dispatch(cmd, skyRenderCompute, 0, (int)settings.skyRenderSize.value.x, (int)settings.skyRenderSize.value.y, 1);
            if (blur == null) blur = new ComputeShaderUtility.GaussianBlur();
            blur.Blur(cmd, data.sky, 3, 2, true);
            cmd.GenerateMips(data.sky);

            SetRaymarchParams(cmd, camera, viewBounds, a.planetPosition, aerialPerspectiveLUTCompute);
            a.sharedAtmosphereValues.ApplyCommand(cmd, aerialPerspectiveLUTCompute);
            cmd.SetComputeVectorParam(aerialPerspectiveLUTCompute, ShaderParamID.dirToSun, dirToSun);
            cmd.SetComputeVectorParam(aerialPerspectiveLUTCompute, ShaderParamID.sunColor, a.sun.baseColor * a.sun.baseIntensity);
            cmd.SetComputeFloatParam(aerialPerspectiveLUTCompute, ShaderParamID.overcast, a.overcast);
            cmd.SetComputeFloatParam(aerialPerspectiveLUTCompute, ShaderParamID.nearClip, data.nearPlane);
            cmd.SetComputeFloatParam(aerialPerspectiveLUTCompute, ShaderParamID.farClip, data.farPlane);
            cmd.SetComputeTextureParam(aerialPerspectiveLUTCompute, 0, ShaderParamID.AerialPerspectiveLuminance, data.aerialPerspectiveLuminance);
            cmd.SetComputeTextureParam(aerialPerspectiveLUTCompute, 0, ShaderParamID.AerialPerspectiveTransmittance, data.aerialPerspectiveTransmittance);
            cmd.SetComputeTextureParam(aerialPerspectiveLUTCompute, 0, ShaderParamID.transmittanceLUT, a.transmittanceLUT);
            cmd.SetComputeVectorParam(aerialPerspectiveLUTCompute, ShaderParamID.size, settings.aerialPerspectiveLUTSize);
            cmd.SetComputeIntParam(aerialPerspectiveLUTCompute, ShaderParamID.numScatteringSteps, settings.numAerialScatteringSteps);
            cmd.SetComputeTextureParam(aerialPerspectiveLUTCompute, 0, ShaderParamID.blueNoise, blueNoise);
            Dispatch(cmd, aerialPerspectiveLUTCompute, 0, (int)settings.aerialPerspectiveLUTSize.value.x,
                (int)settings.aerialPerspectiveLUTSize.value.y,
                (int)settings.aerialPerspectiveLUTSize.value.z);

            PropertySheet sheet = context.propertySheets.Get(atmosphereShader);

            sheet.properties.SetVector(ShaderParamID.viewBounds, new Vector4(viewBounds.min.x, 1 - viewBounds.max.y, viewBounds.size.x, viewBounds.size.y));
            sheet.properties.SetVector(ShaderParamID.dirToSun, dirToSun);
            sheet.properties.SetFloat(ShaderParamID.nearClip, data.nearPlane);
            sheet.properties.SetFloat(ShaderParamID.farClip, data.farPlane);
            sheet.properties.SetTexture(ShaderParamID.AerialPerspectiveLuminance, data.aerialPerspectiveLuminance);
            sheet.properties.SetTexture(ShaderParamID.AerialPerspectiveTransmittance, data.aerialPerspectiveTransmittance);
            sheet.properties.SetTexture(ShaderParamID.sky, data.sky);
            sheet.properties.SetTexture(ShaderParamID.transmittanceLUT, a.transmittanceLUT);
            sheet.properties.SetTexture(ShaderParamID.blueNoise, blueNoise);
            sheet.properties.SetFloat(ShaderParamID.ditherStrength, settings.ditherStrength);
            sheet.properties.SetVector(ShaderParamID.planetPosition, a.planetPosition);
            sheet.properties.SetVector(ShaderParamID.AerialPerspectiveSize, settings.aerialPerspectiveLUTSize);
            sheet.properties.SetFloat(ShaderParamID.planetAngularSize, 2 * Mathf.Asin(a.atmosphereRadius * 2 / (2 * Vector3.Distance(camera.transform.position, a.planetPosition))));
            sheet.properties.SetMatrix("unity_CameraInvProjection", camera.projectionMatrix.inverse);
            sheet.properties.SetMatrix("unity_CameraToWorld", camera.worldToCameraMatrix.inverse);

            bool singlePassDoubleWide = (context.stereoActive && (context.stereoRenderingMode == PostProcessRenderContext.StereoRenderingMode.SinglePass) && (context.camera.stereoTargetEye == StereoTargetEyeMask.Both));
            int tw_stereo = singlePassDoubleWide ? context.screenWidth * 2 : context.screenWidth;
            context.GetScreenSpaceTemporaryRT(cmd, atmosphereBufferID, 0, context.sourceFormat, RenderTextureReadWrite.Default, FilterMode.Bilinear, tw_stereo, context.screenHeight);

            a.sharedAtmosphereValues.Apply(sheet);
            cmd.BlitFullscreenTriangle(context.source, atmosphereBufferID);
            cmd.BlitFullscreenTriangle(atmosphereBufferID, context.destination, sheet, 0);
        }
    }

    void Dispatch(CommandBuffer cmd, ComputeShader compute, int kernelIndex, int x, int y, int z)
    {
        Vector3Int threadGroupSizes = ComputeHelper.GetThreadGroupSizes(compute, kernelIndex);
        int numGroupsX = Mathf.CeilToInt(x / (float)threadGroupSizes.x);
        int numGroupsY = Mathf.CeilToInt(y / (float)threadGroupSizes.y);
        int numGroupsZ = Mathf.CeilToInt(z / (float)threadGroupSizes.y);
        cmd.DispatchCompute(compute, kernelIndex, numGroupsX, numGroupsY, numGroupsZ);
    }

    void SetRaymarchParams(CommandBuffer cmd, Camera cam, Bounds viewRect, Vector3 origin, ComputeShader raymarchCompute)
    {
        Vector3 topLeftDir = CalculateViewDirection(cam, new Vector2(viewRect.min.x, viewRect.max.y));
        Vector3 topRightDir = CalculateViewDirection(cam, new Vector2(viewRect.max.x, viewRect.max.y));
        Vector3 bottomLeftDir = CalculateViewDirection(cam, new Vector2(viewRect.min.x, viewRect.min.y));
        Vector3 bottomRightDir = CalculateViewDirection(cam, new Vector2(viewRect.max.x, viewRect.min.y));

        cmd.SetComputeVectorParam(raymarchCompute, ShaderParamID.topLeftDir, topLeftDir);
        cmd.SetComputeVectorParam(raymarchCompute, ShaderParamID.topRightDir, topRightDir);
        cmd.SetComputeVectorParam(raymarchCompute, ShaderParamID.bottomLeftDir, bottomLeftDir);
        cmd.SetComputeVectorParam(raymarchCompute, ShaderParamID.bottomRightDir, bottomRightDir);

        cmd.SetComputeVectorParam(raymarchCompute, ShaderParamID.camPos, cam.transform.position - origin);
    }

    Vector3 CalculateViewDirection(Camera camera, Vector2 texCoord)
    {
        Matrix4x4 camInverseMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true).inverse;
        Matrix4x4 localToWorldMatrix = camera.transform.localToWorldMatrix;

        Vector3 viewVector = camInverseMatrix * new Vector4(texCoord.x * 2 - 1, texCoord.y * 2 - 1, 0, -1);
        viewVector = localToWorldMatrix * new Vector4(viewVector.x, viewVector.y, viewVector.z, 0);
        return viewVector;
    }
}

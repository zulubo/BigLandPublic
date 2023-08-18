using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;
using System.Collections.Generic;

[System.Serializable]
[PostProcess(typeof(CloudscapeEffectRenderer), PostProcessEvent.BeforeTransparent, "Custom/Cloudscape")]
public class CloudscapeEffect : PostProcessEffectSettings
{
    public BoolParameter temporalSampling = new BoolParameter() { value = true };
    public FloatParameter resolutionScale = new FloatParameter() { value = 0.5f };
    public IntParameter maxResolution = new IntParameter() { value = 512 };

    public BoolParameter debugPauseTemporal = new BoolParameter() { value = false };
    public BoolParameter debugMotionVectors = new BoolParameter() { value = false };

    public FloatParameter postBlur = new FloatParameter() { value = 0 };

    public override bool IsEnabledAndSupported(PostProcessRenderContext context)
    {
        return enabled && Cloudscape.instance != null;
    }
}
class CloudscapeEffectRenderer : PostProcessEffectRenderer<CloudscapeEffect>
{
    const int temporalUpscaleFactor = 4;
    public override DepthTextureMode GetCameraFlags() => DepthTextureMode.Depth;

    const string ProfilerTag = "Cloudscape Pass";
    Material upsampleReproject
    {
        get
        {
            if (_upsampleReproject == null) _upsampleReproject = new Material(Shader.Find("Hidden/TemporalUpsampleReproject"));
            return _upsampleReproject;
        }
    }
    Material _upsampleReproject;

    Material cloudComposite
    {
        get
        {
            if (_cloudComposite == null) _cloudComposite = new Material(Shader.Find("Hidden/CloudscapeComposite"));
            return _cloudComposite;
        }
    }
    Material _cloudComposite;

    Material cloudMat
    {
        get
        {
            if (_cloudMat == null) _cloudMat = new Material(Shader.Find("Hidden/Cloudscape"));
            return _cloudMat;
        }
    }
    Material _cloudMat;

    Texture3D shapeNoise
    {
        get
        {
            if (_shapeNoise == null) _shapeNoise = (Texture3D)Resources.Load("CloudShapeNoise", typeof(Texture3D));
            return _shapeNoise;
        }
    }
    Texture3D _shapeNoise;
    Texture3D detailNoise
    {
        get
        {
            if (_detailNoise == null) _detailNoise = (Texture3D)Resources.Load("CloudDetailNoise", typeof(Texture3D));
            return _detailNoise;
        }
    }
    Texture3D _detailNoise;

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

    private class RenderData
    {
        public Camera camera;
        public RenderTexture textureLowRes;
        public RenderTexture texture;
        public RenderTexture textureProj;
        public RenderTexture motionVectors;
        public int currentSubpixel;
        public MaterialPropertyBlock propertyBlock;
        public Matrix4x4 oldVP;
    }
    private Dictionary<Camera, RenderData> cameraData = new Dictionary<Camera, RenderData>(); // Camera -> ReflectionData table
    public class ShaderParamID
    {
        public static readonly int topLeftDir = Shader.PropertyToID("topLeftDir");
    }

    public override void Init()
    {

    }

    // cross pattern for temporal reprojecting
    static readonly int[] crossPattern = new int[]
    {
        0,0,
        2,2,
        2,0,
        0,2,
        1,1,
        3,3,
        3,1,
        1,3,
        1,0,
        3,2,
        3,0,
        1,2,
        0,1,
        2,3,
        2,1,
        0,3
    };

    ComputeShaderUtility.GaussianBlur blur;
    Color[] ambientColors = new Color[2];
    readonly Vector3[] ambientDirections = new Vector3[2] { Vector3.up, Vector3.down };

    public override void Render(PostProcessRenderContext context)
    {
        var data = GetRenderData(context.camera);

        var clouds = Cloudscape.instance;

        cloudMat.SetVector("cloudHeights", new Vector3(clouds.atmosphere.planetRadius + clouds.cloudAltitude, clouds.atmosphere.planetRadius + clouds.cloudAltitude + clouds.cloudThickness, clouds.cloudThickness));
        cloudMat.SetVector("planetPosition", clouds.atmosphere.transform.position);
        cloudMat.SetFloat("maxStepLength", clouds.maxStepLength * clouds.cloudThickness);
        cloudMat.SetFloat("stepLengthExponent", clouds.stepLengthExponent);
        cloudMat.SetTexture("heightFalloff", clouds.GetDensityLUT());
        cloudMat.SetTexture("shapeNoise", shapeNoise);
        cloudMat.SetTexture("detailNoise", detailNoise);
        cloudMat.SetFloat("_Density", clouds.densityMultiplier);
        cloudMat.SetFloat("shapeScale", clouds.shapeScale);
        cloudMat.SetFloat("detailScale", clouds.detailScale);
        cloudMat.SetFloat("detailStrength", clouds.detailStrength);

        if (settings.temporalSampling)
        { // jitter projection matrix before raymarching
            Matrix4x4 projectionMatrix = context.camera.projectionMatrix;
            float ox = crossPattern[data.currentSubpixel * 2] / 4f;
            float oy = crossPattern[data.currentSubpixel * 2 + 1] / 4f;
            // I have no idea why this needs to be -0.75 instead of -1. Something in TemporalUpsamplingReproject.shader. Results are wrong otherwise. 
            projectionMatrix.m02 += ((ox * 2f) - 0.75f) / data.textureLowRes.width;
            projectionMatrix.m12 += ((oy * 2f) - 0.75f) / data.textureLowRes.height;
            cloudMat.SetMatrix("inverseProjectionMatrix", projectionMatrix.inverse);
        }
        else
        { // regular projection matrix
            cloudMat.SetMatrix("inverseProjectionMatrix", context.camera.projectionMatrix.inverse);
        }
        cloudMat.SetMatrix("cameraToWorldMatrix", context.camera.cameraToWorldMatrix);
        cloudMat.SetMatrix("worldToCameraMatrix", context.camera.worldToCameraMatrix);
        cloudMat.SetVector("cameraPos", context.camera.transform.position);
        cloudMat.SetTexture("_BlueNoise", blueNoise);
        if (settings.temporalSampling)
        {
            cloudMat.SetVector("_ScreenNoiseRes", new Vector4(data.textureLowRes.width, data.textureLowRes.height, blueNoise.width, blueNoise.height));
            cloudMat.SetVector("_BlueNoiseJitter", new Vector2(Mathf.Round(Random.value* data.textureLowRes.width)/data.textureLowRes.width,
                Mathf.Round(Random.value * data.textureLowRes.height) / data.textureLowRes.height));
        }
        else
        { // I find that jittering the blue noise makes it more noticeable when temporal sampling is off. You can uncomment these lines to add it back
            cloudMat.SetVector("_ScreenNoiseRes", new Vector4(data.texture.width, data.texture.height, blueNoise.width, blueNoise.height));
            //cloudMat.SetVector("_BlueNoiseJitter", new Vector2(Mathf.Round(Random.value * data.texture.width) / data.texture.width,
            //    Mathf.Round(Random.value * data.texture.height) / data.texture.height));
        }

        // approximation of ambient sky light
        RenderSettings.ambientProbe.Evaluate(ambientDirections, ambientColors);
        cloudMat.SetColor("_AmbientLight", ambientColors[0]);
        cloudMat.SetFloat("_AmbientOcclusion", clouds.ambientOcclusion);

        var atmosphere = Atmosphere.atmospheres.Count > 0 ? Atmosphere.atmospheres[0] : null;
        if (atmosphere != null)
        {
            if (atmosphere.sun != null) 
            {
                cloudMat.SetVector("_LightDir", -atmosphere.sun.direction);
                cloudMat.SetColor("_LightColor", atmosphere.sun.light.color * atmosphere.sun.light.intensity / Mathf.Lerp(1, atmosphere.overcastSunOcclusion, atmosphere.overcast));
            }

            if (AtmosphereEffectRenderer.perCameraAtmosphere.TryGetValue(context.camera, out var sky))
            { // this is important! Pass the precalculated atmosphere 3D textures to the clouds for aerial perspective
                cloudMat.SetTexture("AerialPerspectiveLuminance", sky.aerialPerspectiveLuminance);
                cloudMat.SetTexture("AerialPerspectiveTransmittance", sky.aerialPerspectiveTransmittance);
                cloudMat.SetFloat("AerialPerspectiveNearClip", sky.nearPlane);
                cloudMat.SetFloat("AerialPerspectiveFarClip", sky.farPlane);
            }
        }

        context.command.Blit(null, settings.temporalSampling ? data.textureLowRes : data.texture, cloudMat, 0);

        if (settings.temporalSampling)
        {
            // render motion vectors
            Matrix4x4 matrixVP = context.camera.nonJitteredProjectionMatrix * context.camera.worldToCameraMatrix;
            cloudMat.SetMatrix("_CurrentVP", matrixVP);
            cloudMat.SetMatrix("_PreviousVP", data.oldVP);
            context.command.Blit(null, data.motionVectors, cloudMat, 1);
            data.oldVP = matrixVP;

            // reproject high res texture using motion vectors
            upsampleReproject.SetVector("_MainTexDimensions", new Vector2(data.texture.width, data.texture.height));
            upsampleReproject.SetTexture("_NewSample", data.textureLowRes);
            upsampleReproject.SetVector("_NewSampleDimensions", new Vector2(data.textureLowRes.width, data.textureLowRes.height));
            upsampleReproject.SetTexture("_MotionVectors", data.motionVectors);
            if (!settings.debugPauseTemporal)
            {
                upsampleReproject.SetFloat("scale", temporalUpscaleFactor);
                upsampleReproject.SetVector("sampleOffset", new Vector2(crossPattern[data.currentSubpixel * 2], crossPattern[data.currentSubpixel * 2 + 1]));
                data.currentSubpixel++;
                if (data.currentSubpixel >= temporalUpscaleFactor * temporalUpscaleFactor) data.currentSubpixel = 0;
            }

            context.command.Blit(data.texture, data.textureProj, upsampleReproject);
            context.command.CopyTexture(data.textureProj, data.texture);
            
            if (settings.debugMotionVectors)
            {
                context.command.Blit(data.motionVectors, context.destination);
                return;
            }
        }

        if (settings.postBlur > 0)
        {
            if (blur == null) blur = new ComputeShaderUtility.GaussianBlur();
            blur.Blur(context.command, data.texture, Mathf.CeilToInt(settings.postBlur), settings.postBlur * 0.3f, true);
        }

        cloudComposite.SetTexture("_CloudTex", data.texture);
        context.command.Blit(context.source, context.destination, cloudComposite);
    }

    private RenderData GetRenderData(Camera currentCamera)
    {
        if (!cameraData.TryGetValue(currentCamera, out RenderData data))
        {
            data = new RenderData();
            data.propertyBlock = new MaterialPropertyBlock();
            cameraData[currentCamera] = data;
        }

        int widthLR = Mathf.RoundToInt(Mathf.Min(currentCamera.pixelWidth * settings.resolutionScale, settings.maxResolution) / temporalUpscaleFactor);
        int heightLR = Mathf.RoundToInt(Mathf.Min(currentCamera.pixelHeight * settings.resolutionScale, settings.maxResolution) / temporalUpscaleFactor);
        int width = widthLR * temporalUpscaleFactor;
        int height = heightLR * temporalUpscaleFactor;

        // For stereo cameras, we are going to render into a double-wide texture
        if (currentCamera.stereoEnabled)
        {
            width *= 2;
        }

        if (settings.temporalSampling)
        {
            ComputeHelper.CreateRenderTexture(ref data.textureLowRes, widthLR, heightLR, FilterMode.Bilinear, UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat, "CloudLR", useMipMaps: false);

            ComputeHelper.CreateRenderTexture(ref data.motionVectors, widthLR, heightLR, FilterMode.Bilinear, UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat, "CloudMotionVectors", useMipMaps: false);

            ComputeHelper.CreateRenderTexture(ref data.textureProj, width, height, FilterMode.Bilinear, UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat, "CloudProj", useMipMaps: false);
            data.textureProj.enableRandomWrite = true;
        }

        ComputeHelper.CreateRenderTexture(ref data.texture, width, height, FilterMode.Bilinear, UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat, "CloudTex", useMipMaps: false);

        return data;
    }

}

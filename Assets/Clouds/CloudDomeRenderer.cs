using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class CloudDomeRenderer : MonoBehaviour
{
    const int temporalUpscaleFactor = 4;
    public float resolutionScale = 0.5f;
    public int maxResolution = 512;
    public Material material;
    public Atmosphere atmosphere;
    public Sun sun;

    // debug
    public bool pauseTemporal;
    public bool debugMotionVectors;
    //

    private class RenderData
    {
        public RenderTexture textureLowRes;
        public RenderTexture texture;
        public RenderTexture textureProj;
        public RenderTexture motionVectors;
        public int currentSubpixel;
        public MaterialPropertyBlock propertyBlock;
        public Matrix4x4 oldVP;
    }
    private Dictionary<Camera, RenderData> cameraData = new Dictionary<Camera, RenderData>(); // Camera -> ReflectionData table
    private static readonly Rect LeftEyeRect = new Rect(0.0f, 0.0f, 0.5f, 1.0f);
    private static readonly Rect RightEyeRect = new Rect(0.5f, 0.0f, 0.5f, 1.0f);
    private static readonly Rect DefaultRect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
    static readonly int TexturePropertyID = Shader.PropertyToID("_MainTex");

    ComputeShader upsampleCompute
    {
        get
        {
            if (_upsampleCompute == null) _upsampleCompute = (ComputeShader)Resources.Load("TemporalUpsample", typeof(ComputeShader));
            return _upsampleCompute;
        }
    }
    ComputeShader _upsampleCompute;
    Material upsampleReproject
    {
        get
        {
            if (_upsampleReproject == null) _upsampleReproject = new Material(Shader.Find("Hidden/TemporalUpsampleReproject"));
            return _upsampleReproject;
        }
    }
    Material _upsampleReproject;

    Renderer rend;
    private Material displayMat;
    private Texture2D blueNoise;

    // This is called when it's known that this mirror will be rendered by some camera. We render reflections
    // and do other updates here. Because the script executes in edit mode, reflections for the scene view
    // camera will just work!
    public void OnWillRenderObject()
    {
        if (material == null) return;

        if (rend == null)
        {
            rend = GetComponent<Renderer>();
            displayMat = new Material(Shader.Find("Hidden/CloudDomeDisplay"));
            rend.sharedMaterial = displayMat;
        }
        
        if (!enabled || !rend || !rend.enabled)
        {
            return;
        }


        Camera cam = Camera.current;
        if (cam == null)
        {
            return;
        }

        RenderData data = GetRenderData(cam);


        /*if (cam.stereoEnabled)
        {
            if (cam.stereoTargetEye == StereoTargetEyeMask.Both || cam.stereoTargetEye == StereoTargetEyeMask.Left)
            {
                Vector3 eyePos = cam.transform.TransformPoint(SteamVR.instance.eyes[0].pos);
                Quaternion eyeRot = cam.transform.rotation * SteamVR.instance.eyes[0].rot;
                Matrix4x4 projectionMatrix = GetSteamVRProjectionMatrix(cam, Valve.VR.EVREye.Eye_Left);

                RenderMirror(data.texture, eyePos, eyeRot, projectionMatrix, LeftEyeRect);
            }

            if (cam.stereoTargetEye == StereoTargetEyeMask.Both || cam.stereoTargetEye == StereoTargetEyeMask.Right)
            {
                Vector3 eyePos = cam.transform.TransformPoint(SteamVR.instance.eyes[1].pos);
                Quaternion eyeRot = cam.transform.rotation * SteamVR.instance.eyes[1].rot;
                Matrix4x4 projectionMatrix = GetSteamVRProjectionMatrix(cam, Valve.VR.EVREye.Eye_Right);

                RenderMirror(data.texture, eyePos, eyeRot, projectionMatrix, RightEyeRect);
            }
        }
        else
        {*/
        RenderClouds(cam, data);
        //}

        rend.SetPropertyBlock(data.propertyBlock);
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
    void RenderClouds(Camera cam, RenderData data)
    {
        if (temporalSamplingEnabled)
        {
            Matrix4x4 projectionMatrix = cam.projectionMatrix;
            float ox = crossPattern[data.currentSubpixel * 2] / 4f;
            float oy = crossPattern[data.currentSubpixel * 2 + 1] / 4f;
            projectionMatrix.m02 += ((ox * 2f) - 1f) / data.textureLowRes.width;
            projectionMatrix.m12 += ((oy * 2f) - 1f) / data.textureLowRes.height;
            material.SetMatrix("inverseProjectionMatrix", projectionMatrix.inverse);
        }
        else
        {
            material.SetMatrix("inverseProjectionMatrix", cam.projectionMatrix.inverse);
        }
        material.SetMatrix("cameraToWorldMatrix", cam.cameraToWorldMatrix);
        material.SetMatrix("worldToCameraMatrix", cam.worldToCameraMatrix);
        Vector3 pos = transform.position; // make the local space matrix centered at world 0,0
        //transform.position = new Vector3(0, pos.y, 0);
        material.SetMatrix("worldToObjectMatrix", transform.worldToLocalMatrix);
        material.SetVector("cameraPos", transform.InverseTransformPoint(cam.transform.position));
        Vector3 offset = transform.InverseTransformPoint(Vector3.zero); offset.y = 0;
        material.SetVector("offset", offset); // ignore horizontal object motion
        //transform.position = pos;
        material.SetMatrix("objectToWorldMatrix", transform.localToWorldMatrix);
        material.SetVector("worldCameraPos", cam.transform.position);
        if (blueNoise == null) blueNoise = (Texture2D)Resources.Load("BlueNoise", typeof(Texture2D));
        material.SetTexture("_BlueNoise", blueNoise);
        if (temporalSamplingEnabled)
        {
            material.SetVector("_ScreenNoiseRes", new Vector4(data.textureLowRes.width, data.textureLowRes.height, blueNoise.width, blueNoise.height));
            material.SetVector("_BlueNoiseJitter", new Vector2(Mathf.Round(Random.value * data.textureLowRes.width) / data.textureLowRes.width,
                Mathf.Round(Random.value * data.textureLowRes.height) / data.textureLowRes.height));
        }
        else
        {
            material.SetVector("_ScreenNoiseRes", new Vector4(data.texture.width, data.texture.height, blueNoise.width, blueNoise.height));
            material.SetVector("_BlueNoiseJitter", new Vector2(Mathf.Round(Random.value * data.texture.width) / data.texture.width,
                Mathf.Round(Random.value * data.texture.height) / data.texture.height));
        }
        if (atmosphere != null)
        {
            material.SetVector("_LightDir", -sun.direction);
            material.SetColor("_LightColor", sun.light.color * sun.light.intensity / Mathf.Lerp(1, atmosphere.overcastSunOcclusion, atmosphere.overcast));

            if(AtmosphereEffectRenderer.perCameraAtmosphere.TryGetValue(cam, out var sky))
            {
                material.SetTexture("AerialPerspectiveLuminance", sky.aerialPerspectiveLuminance);
                material.SetTexture("AerialPerspectiveTransmittance", sky.aerialPerspectiveTransmittance);
                material.SetFloat("AerialPerspectiveNearClip", sky.nearPlane);
                material.SetFloat("AerialPerspectiveFarClip", sky.farPlane);
            }
        }
        var oldactive = RenderTexture.active;
        RenderTexture.active = temporalSamplingEnabled ? data.textureLowRes : data.texture;
        Graphics.Blit(null, material, 0);

        if (temporalSamplingEnabled)
        {
            // render motion vectors
            Matrix4x4 matrixVP = cam.nonJitteredProjectionMatrix * cam.worldToCameraMatrix;
            material.SetMatrix("_CurrentVP", matrixVP);
            material.SetMatrix("_PreviousVP", data.oldVP);
            RenderTexture.active = data.motionVectors;
            Graphics.Blit(null, material, 1);
            data.oldVP = matrixVP;

            // reproject high res texture using motion vectors
            upsampleReproject.SetTexture("_MotionVectors", data.motionVectors);
            upsampleReproject.SetTexture("_Fallback", data.textureLowRes);
            Graphics.Blit(data.texture, data.textureProj, upsampleReproject);
            Graphics.Blit(data.textureProj, data.texture);

            if (!pauseTemporal)
            {
                upsampleCompute.SetTexture(0, "Source", data.textureLowRes);
                upsampleCompute.SetTexture(0, "Dest", data.texture);
                upsampleCompute.SetInt("scaleFactor", temporalUpscaleFactor);
                upsampleCompute.SetInt("currentPixel", data.currentSubpixel);
                upsampleCompute.Dispatch(0, data.textureLowRes.width, data.textureLowRes.height, 1);
                data.currentSubpixel++;
                if (data.currentSubpixel >= temporalUpscaleFactor * temporalUpscaleFactor) data.currentSubpixel = 0;
            }

            if (debugMotionVectors) Graphics.Blit(data.motionVectors, data.texture);
        }
        RenderTexture.active = oldactive;
    }
    //
    // Cleanup all the objects we possibly have created
    void OnDisable()
    {
        foreach (RenderData reflectionData in cameraData.Values)
        {
            DestroyImmediate(reflectionData.texture);
        }
        cameraData.Clear();
    }


    bool temporalSamplingEnabled => !Reflector.ReflectorProbe.rendering;

    // On-demand create any objects we need
    private RenderData GetRenderData(Camera currentCamera)
    {
        RenderData data = null;
        if (!cameraData.TryGetValue(currentCamera, out data))
        {
            data = new RenderData();
            data.propertyBlock = new MaterialPropertyBlock();
            cameraData[currentCamera] = data;
        }

        int widthLR = Mathf.RoundToInt(Mathf.Min(currentCamera.pixelWidth * resolutionScale, maxResolution) / temporalUpscaleFactor);
        int heightLR = Mathf.RoundToInt(Mathf.Min(currentCamera.pixelHeight * resolutionScale, maxResolution) / temporalUpscaleFactor);
        int width = widthLR * temporalUpscaleFactor;
        int height = heightLR * temporalUpscaleFactor;

        // For stereo cameras, we are going to render into a double-wide texture
        if (currentCamera.stereoEnabled)
        {
            width *= 2;
        }

        if (temporalSamplingEnabled)
        {
            if (!data.textureLowRes || data.textureLowRes.width != widthLR || data.textureLowRes.height != heightLR)
            {
                if (data.textureLowRes)
                    DestroyImmediate(data.textureLowRes);
                data.textureLowRes = new RenderTexture(widthLR, heightLR, 0, UnityEngine.Experimental.Rendering.DefaultFormat.HDR);
                data.textureLowRes.antiAliasing = 1;
                data.textureLowRes.hideFlags = HideFlags.DontSave;
            }

            if (!data.motionVectors || data.motionVectors.width != widthLR || data.motionVectors.height != heightLR)
            {
                if (data.motionVectors)
                    DestroyImmediate(data.motionVectors);
                data.motionVectors = new RenderTexture(widthLR, heightLR, 0, UnityEngine.Experimental.Rendering.DefaultFormat.HDR);
                data.motionVectors.antiAliasing = 1;
                data.motionVectors.hideFlags = HideFlags.DontSave;
            }

            if (!data.textureProj || data.textureProj.width != width || data.textureProj.height != height)
            {
                if (data.textureProj)
                    DestroyImmediate(data.textureProj);
                data.textureProj = new RenderTexture(width, height, 0, UnityEngine.Experimental.Rendering.DefaultFormat.HDR);
                data.textureProj.enableRandomWrite = true;
                data.textureProj.antiAliasing = 1;
                data.textureProj.hideFlags = HideFlags.DontSave;
            }
        }
        
        
        if (!data.texture || data.texture.width != width || data.texture.height != height)
        {
            if (data.texture)
                DestroyImmediate(data.texture);
            data.texture = new RenderTexture(width, height, 0, UnityEngine.Experimental.Rendering.DefaultFormat.HDR);
            data.texture.enableRandomWrite = true;
            data.texture.antiAliasing = 1;
            data.texture.hideFlags = HideFlags.DontSave;
            data.propertyBlock.SetTexture(TexturePropertyID, data.texture);
        }

        return data;
    }

    private static Vector4 Plane(Vector3 pos, Vector3 normal)
    {
        return new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(pos, normal));
    }

    /*
    private static Matrix4x4 GetSteamVRProjectionMatrix(Camera cam, Valve.VR.EVREye eye)
    {
        Valve.VR.HmdMatrix44_t proj = SteamVR.instance.hmd.GetProjectionMatrix(eye, cam.nearClipPlane, cam.farClipPlane);
        Matrix4x4 m = new Matrix4x4();
        m.m00 = proj.m0;
        m.m01 = proj.m1;
        m.m02 = proj.m2;
        m.m03 = proj.m3;
        m.m10 = proj.m4;
        m.m11 = proj.m5;
        m.m12 = proj.m6;
        m.m13 = proj.m7;
        m.m20 = proj.m8;
        m.m21 = proj.m9;
        m.m22 = proj.m10;
        m.m23 = proj.m11;
        m.m30 = proj.m12;
        m.m31 = proj.m13;
        m.m32 = proj.m14;
        m.m33 = proj.m15;
        return m;
    }*/
}

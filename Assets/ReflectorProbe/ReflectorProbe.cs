using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Reflector
{
    [RequireComponent(typeof(ReflectionProbe)), ExecuteInEditMode]
    public class ReflectorProbe : MonoBehaviour//, ISectorizable
    {
        private static HashSet<ReflectorProbe> instances = new HashSet<ReflectorProbe>();

        public static ReflectorProbe[] Instances
        {
            get { return instances.ToArray(); }
        }

        private static Dictionary<int, RenderPair> targets = new Dictionary<int, RenderPair>();

        private static RenderPair[] Renders
        {
            get { return targets.Values.ToArray(); }
        }

        private static Camera renderCamera;

        private static Material mirror = null;

        private static Quaternion[] orientations = new Quaternion[]
        {
            Quaternion.LookRotation(Vector3.right, Vector3.down),
            Quaternion.LookRotation(Vector3.left, Vector3.down),
            Quaternion.LookRotation(Vector3.up, Vector3.forward),
            Quaternion.LookRotation(Vector3.down, Vector3.back),
            Quaternion.LookRotation(Vector3.forward, Vector3.down),
            Quaternion.LookRotation(Vector3.back, Vector3.down)
        };

        private RenderTexture cubemap;

        public RenderTexture Cubemap
        {
            get { return cubemap; }
        }

        private Coroutine refreshing;
#if UNITY_EDITOR
        private Unity.EditorCoroutines.Editor.EditorCoroutine refreshingEditor;
#endif

        public bool Refreshing
        {
            get 
            {
                if (Application.isPlaying)
                {
                    return refreshing != null;
                }
#if UNITY_EDITOR
                else
                {
                    return refreshingEditor != null;
                }
#endif
            }
        }

        private bool visible = false;

        public bool Visible
        {
            get { return visible; }
            set { visible = value; }
        }

        public GameObject GameObject
        {
            get { return gameObject; }
        }
        /*
        [SerializeField]
        private bool bakeable = false;

        [SerializeField]
        private RenderTexture baked;

        public Texture Baked
        {
            get { return baked; }
        }*/

        [SerializeField]
        private Camera customCamera;

        private Camera customCameraInstance;
        private Camera externalCamera;

        private ImageEffectPair[] effects;

        public static bool rendering { get; private set; }

        public ReflectionProbe probe
        {
            get
            {
                if (_probe == null) _probe = GetComponent<ReflectionProbe>();
                return _probe;
            }
        }
        private ReflectionProbe _probe;

        public Camera Camera
        {
            get
            {
                if (externalCamera != null)
                    return externalCamera;

                if (customCameraInstance != null)
                    return customCameraInstance;

                if (customCamera == null)
                {
                    if (renderCamera == null)
                        renderCamera = new GameObject("ReflectionCamera").AddComponent<Camera>();

                    return renderCamera;
                }
                else
                {
                    customCameraInstance = Instantiate(customCamera.gameObject).GetComponent<Camera>();
                    customCameraInstance.gameObject.hideFlags = HideFlags.HideAndDontSave;
                    return customCameraInstance;
                }
            }
            set
            {
                externalCamera = value;
            }
        }

        private void OnEnable()
        {
            instances.Add(this);
            RefreshReflection(RefreshMode.Instant);

            ReflectionProbe probe = GetComponent<ReflectionProbe>();
            probe.hideFlags = HideFlags.None;
            probe.mode = ReflectionProbeMode.Custom;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.customBakedTexture = cubemap;

            //CullingCamera.PostCull += PostCullRender;

#if UNITY_EDITOR
            UnityEditorInternal.InternalEditorUtility.SetIsInspectorExpanded(probe, false);
#endif
        }

        private void OnDisable()
        {
            instances.Remove(this);
            if (customCameraInstance != null)
                DestroyImmediate(customCameraInstance.gameObject);

            ResetReflection();

            //CullingCamera.PostCull -= PostCullRender;
        }

        private void OnDrawGizmos()
        {
            ReflectionProbe probe = GetComponent<ReflectionProbe>();
            Gizmos.color = new Color(1, 0.4f, 0, 0.3f);
            Gizmos.matrix = transform.localToWorldMatrix;

            Vector3 center = probe.center;

            Gizmos.DrawWireCube(center, probe.size);
            Gizmos.matrix = Matrix4x4.identity;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1, 0.4f, 0, 0.1f);
            Gizmos.matrix = transform.localToWorldMatrix;

            Vector3 center = probe.center;

            Gizmos.DrawCube(center, probe.size);
            Gizmos.matrix = Matrix4x4.identity;
        }

        private class TempCubemap
        {
            public RenderTexture cubemap;
            public bool inUse;

            public TempCubemap(RenderTexture cubemap)
            {
                this.cubemap = cubemap;
                inUse = true;
            }
        }
        private static List<TempCubemap> tempCubemaps = new List<TempCubemap>();
        static RenderTexture GetTempCubemap(int resolution)
        {
            var existing = tempCubemaps.Find(t => t != null && t.cubemap != null && !t.inUse && t.cubemap.width == resolution);
            if (existing != null)
            {
                existing.inUse = true;
                return existing.cubemap;
            }

            RenderTexture cubemap = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.DefaultHDR);
            cubemap.dimension = TextureDimension.Cube;
            cubemap.useMipMap = true;
            cubemap.autoGenerateMips = false;
            tempCubemaps.Add(new TempCubemap(cubemap));
            cubemap.Create();
            return cubemap;
        }
        static void ReleaseTempCubemap(RenderTexture cubemap)
        {
            var existing = tempCubemaps.Find(t => t.cubemap == cubemap);
            if (existing != null)
            {
                existing.inUse = false;
            }
        }
        /*
        public RenderTexture BakeReflection()
        {
            if (Application.isPlaying || !bakeable)
                return null;

            CreateData();

            RenderPair pair;
            if (!targets.TryGetValue(probe.resolution, out pair))
                return null;

            RenderTexture cubemap = GetTempCubemap(probe.resolution);

            Camera camera = Camera;
            camera.gameObject.SetActive(true);
            camera.transform.position = transform.position;
            camera.targetTexture = pair.Render;

            SetCameraSettings(camera);

            for (int face = 0; face < 6; face++)
            {
                camera.transform.rotation = orientations[face];

                Shader.EnableKeyword("NO_REFLECTION");
                camera.Render();
                Shader.DisableKeyword("NO_REFLECTION");

                Graphics.Blit(pair.Render, pair.Mirror, mirror);

                RenderTexture source = pair.Mirror;
                RenderTexture destination = pair.Render;
                for (int i = 0; i < effects.Length; i++)
                {
                    if (effects[i].Render(source, destination))
                    {
                        RenderTexture temp = source;
                        source = destination;
                        destination = temp;
                    }
                }

                Graphics.CopyTexture(source, 0, 0, cubemap, face, 0);

                Clear(pair);
            }

            CreateCubemap();

             SpecularConvolve(cubemap, baked);

            GetComponent<ReflectionProbe>().customBakedTexture = baked;

            DestroyImmediate(camera.gameObject);

            ResetReflection();

            return baked;
        }

        public void ClearBaking()
        {
            baked = null;
            GetComponent<ReflectionProbe>().customBakedTexture = null;
        }*/

        public void RefreshReflection(RefreshMode refresh = RefreshMode.Default)
        {
            RefreshReflectionAt(refresh, transform.position); 
        }

        public void RefreshReflectionAt(RefreshMode refresh, Vector3 position)
        {
            CreateData();
            CreateCubemap();

            int resolution = GetComponent<ReflectionProbe>().resolution;

            RenderPair pair;
            if (!targets.TryGetValue(resolution, out pair))
            {
                refreshing = null;
                return;
            }

            Camera camera = Camera;
            camera.transform.position = position;
            camera.targetTexture = pair.Render;

            SetCameraSettings(camera);

            switch (refresh)
            {
                case RefreshMode.Default:
                    break;
                case RefreshMode.Instant:
                    RefreshInstant(pair, camera);
                    break;
                case RefreshMode.Face:
                    if (Application.isPlaying)
                        refreshing = StartCoroutine(RefreshFace(pair, camera));
#if UNITY_EDITOR
                    else
                        refreshingEditor = Unity.EditorCoroutines.Editor.EditorCoroutineUtility.StartCoroutine(RefreshFace(pair, camera), this);
#endif
                    break;
                case RefreshMode.Overtime:
                    if (Application.isPlaying)
                        refreshing = StartCoroutine(RefreshOvertime(pair, camera));
#if UNITY_EDITOR
                    else
                        refreshingEditor = Unity.EditorCoroutines.Editor.EditorCoroutineUtility.StartCoroutine(RefreshOvertime(pair, camera), this);
#endif
                    break;
            }
        }

        public void ResetReflection()
        {
            probe.customBakedTexture = cubemap;
            int resolution = probe.resolution;

            RenderPair pair;
            if (targets.TryGetValue(resolution, out pair))
            {
                pair.Reflections.Remove(this);
                if (pair.Reflections.Count == 0)
                {
                    pair.Release();
                    targets.Remove(resolution);
                }
            }

            if (cubemap != null)
            {
                cubemap.Release();
                cubemap = null;
            }
        }

        private void RefreshInstant(RenderPair pair, Camera camera)
        {
            try
            {
                RenderTexture tempCube = GetTempCubemap(probe.resolution);

                for (int face = 0; face < 6; face++)
                {
                    camera.transform.rotation = orientations[face];

                    Shader.EnableKeyword("NO_REFLECTION");
                    camera.gameObject.SetActive(true);
                    rendering = true;
                    camera.Render();
                    rendering = false;
                    camera.gameObject.SetActive(false);
                    Shader.DisableKeyword("NO_REFLECTION");

                    Graphics.Blit(pair.Render, pair.Mirror, mirror);

                    RenderTexture source = pair.Mirror;
                    RenderTexture destination = pair.Render;
                    for (int i = 0; i < effects.Length; i++)
                    {
                        if (effects[i].Render(source, destination))
                        {
                            RenderTexture temp = source;
                            source = destination;
                            destination = temp;
                        }
                    }

                    Graphics.CopyTexture(source, 0, 0, tempCube, face, 0);

                    Clear(pair);
                }

                SpecularConvolve(tempCube, cubemap);
                ReleaseTempCubemap(tempCube);
            }
            finally
            {
                rendering = false;
                refreshing = null;
#if UNITY_EDITOR
                refreshingEditor = null;
#endif
            }
        }

        private IEnumerator RefreshFace(RenderPair pair, Camera camera)
        {
            try
            {
                for (int face = 0; face < 6; face++)
                {
                    camera.transform.rotation = orientations[face];

                    Shader.EnableKeyword("NO_REFLECTION");
                    camera.Render();
                    Shader.DisableKeyword("NO_REFLECTION");

                    Graphics.Blit(pair.Render, pair.Mirror, mirror);
                    Graphics.CopyTexture(pair.Mirror, 0, 0, cubemap, face, 0);

                    Clear(pair);

                    yield return null;
                }

                cubemap.GenerateMips();
            }
            finally
            {
                rendering = false;
                refreshing = null;
#if UNITY_EDITOR
                refreshingEditor = null;
#endif
            }
        }

        private IEnumerator RefreshOvertime(RenderPair pair, Camera camera)
        {
            try
            {
                RenderTexture tempCube = GetTempCubemap(probe.resolution);

                for (int face = 0; face < 6; face++)
                {
                    camera.transform.rotation = orientations[face];

                    Shader.EnableKeyword("NO_REFLECTION");
                    camera.gameObject.SetActive(true);
                    rendering = true;
                    camera.Render();
                    rendering = false;
                    camera.gameObject.SetActive(false);
                    Shader.DisableKeyword("NO_REFLECTION");

                    Graphics.Blit(pair.Render, pair.Mirror, mirror);

                    RenderTexture source = pair.Mirror;
                    RenderTexture destination = pair.Render;
                    for (int i = 0; i < effects.Length; i++)
                    {
                        if (effects[i].Render(source, destination))
                        {
                            RenderTexture temp = source;
                            source = destination;
                            destination = temp;
                        }
                    }

                    Graphics.CopyTexture(source, 0, 0, tempCube, face, 0);

                    Clear(pair);

                    yield return null;
                }

                SpecularConvolve(tempCube, cubemap);
                ReleaseTempCubemap(tempCube);
            }
            finally
            {
                rendering = false;
                refreshing = null;
#if UNITY_EDITOR
                refreshingEditor = null;
#endif
            }
        }

        /*public void PostCullRender(CullingCamera camera)
        {
            if (!visible || !rendering)
                return;

            Shader.EnableKeyword("NO_REFLECTION");
            camera.gameObject.SetActive(true);
            camera.Render();
            camera.gameObject.SetActive(false);
            Shader.DisableKeyword("NO_REFLECTION");

            cullRenderTimer = -1;
            rendering = false;
        }*/

        private IEnumerator ClearOvertime(RenderPair pair)
        {
            RenderTexture.active = pair.Render;
            GL.Clear(true, true, Color.clear);

            yield return null;

            RenderTexture.active = pair.Mirror;
            GL.Clear(true, true, Color.clear);

            yield return null;
        }

        private void Clear(RenderPair pair)
        {
            RenderTexture rt = RenderTexture.active;

            RenderTexture.active = pair.Render;
            GL.Clear(true, true, Color.red);

            RenderTexture.active = pair.Mirror;
            GL.Clear(true, true, Color.red);

            RenderTexture.active = rt;
        }

        private void CreateData()
        {
            if (mirror == null)
                mirror = new Material(Shader.Find("Hidden/ReflectorProbe/Mirror"));

            int resolution = GetComponent<ReflectionProbe>().resolution;

            RenderPair pair;
            if (targets.TryGetValue(resolution, out pair))
            {
                pair.Reflections.Add(this);
            }
            else
            {
                RenderTexture render = new RenderTexture(resolution, resolution, 16, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                render.useMipMap = false;
                render.Create();

                RenderTexture mirror = new RenderTexture(resolution, resolution, 16, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                mirror.useMipMap = false;
                mirror.Create();

                pair = new RenderPair(render, mirror);
                pair.Reflections.Add(this);
                targets.Add(resolution, pair);
            }
        }

        private void CreateCubemap()
        {
            if (cubemap != null)
                return;

            cubemap = new RenderTexture(probe.resolution, probe.resolution, 0, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);
            cubemap.dimension = TextureDimension.Cube;
            cubemap.useMipMap = true;
            cubemap.autoGenerateMips = false;
            cubemap.Create();

            GetComponent<ReflectionProbe>().customBakedTexture = cubemap;
        }

        private void SetCameraSettings(Camera camera)
        {
            ReflectionProbe probe = GetComponent<ReflectionProbe>();

            camera.hideFlags = HideFlags.HideAndDontSave;
            camera.enabled = false;
            camera.cameraType = CameraType.Reflection;
            camera.fieldOfView = 90;

            if (customCamera == null)
            {
                camera.farClipPlane = probe.farClipPlane;
                camera.nearClipPlane = probe.nearClipPlane;
                camera.cullingMask = probe.cullingMask;
                camera.clearFlags = (CameraClearFlags)probe.clearFlags;
                camera.backgroundColor = probe.backgroundColor;
                camera.allowHDR = probe.hdr;
            }

            List<ImageEffectPair> pairs = new List<ImageEffectPair>();

            MonoBehaviour[] behaviours = camera.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MethodInfo method = GetRenderImage(behaviours[i].GetType());
                if (method != null)
                    pairs.Add(new ImageEffectPair(behaviours[i], method));
            }

            effects = pairs.ToArray();
        }

        public static MethodInfo GetRenderImage(Type type)
        {
            while (type != null)
            {
                MethodInfo info = type.GetMethod("OnRenderImage", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (info != null)
                    return info;

                if (type.BaseType != null && type.BaseType != typeof(object) && type.BaseType != typeof(UnityEngine.Object))
                    type = type.BaseType;
                else
                    break;
            }

            return null;
        }

        private struct RenderPair
        {
            private RenderTexture render;

            public RenderTexture Render
            {
                get { return render; }
            }

            private RenderTexture mirror;

            public RenderTexture Mirror
            {
                get { return mirror; }
            }

            private HashSet<ReflectorProbe> reflections;

            public HashSet<ReflectorProbe> Reflections
            {
                get { return reflections; }
            }

            public RenderPair(RenderTexture render, RenderTexture mirror)
            {
                this.render = render;
                this.mirror = mirror;
                reflections = new HashSet<ReflectorProbe>();
            }

            public void Release()
            {
                render.Release();
                mirror.Release();
            }
        }

        private struct ImageEffectPair
        {
            private MonoBehaviour behaviour;
            private MethodInfo method;

            public bool Render(RenderTexture source, RenderTexture destination)
            {
                if (!behaviour.enabled)
                    return false;

                method.Invoke(behaviour, new object[] { source, destination });
                return true;
            }

            public ImageEffectPair(MonoBehaviour behaviour, MethodInfo method)
            {
                this.behaviour = behaviour;
                this.method = method;
            }
        }

        /// <summary>
        /// Helper function to perform a blit for each cubemap face.
        /// The provided material should use a shader where the TEXCOORD0 unit xyz coordinates is the world space view direction.
        /// See the built-in CubeBlurOdd shader for an example.
        /// </summary>
        void CubemapBlit(RenderTexture dstCubemap, Material material, int shaderPass = 0, int dstMip = 0)
        {
            GL.PushMatrix();
            GL.LoadOrtho();

            // The CubeBlur shader uses 3D texture coordinates when sampling the cubemap,
            // so we can't use Graphics.Blit here.
            // Instead we build a cube (sort of) and render using that.

            // Positive X
            Graphics.SetRenderTarget(dstCubemap, dstMip, CubemapFace.PositiveX);
            material.SetPass(shaderPass);
            GL.Begin(GL.QUADS);
            GL.MultiTexCoord3(0, 1, 1, 1);
            GL.Vertex3(0, 0, 1);
            GL.MultiTexCoord3(0, 1, -1, 1);
            GL.Vertex3(0, 1, 1);
            GL.MultiTexCoord3(0, 1, -1, -1);
            GL.Vertex3(1, 1, 1);
            GL.MultiTexCoord3(0, 1, 1, -1);
            GL.Vertex3(1, 0, 1);
            GL.End();

            // Negative X
            Graphics.SetRenderTarget(dstCubemap, dstMip, CubemapFace.NegativeX);
            material.SetPass(shaderPass);
            GL.Begin(GL.QUADS);
            GL.MultiTexCoord3(0, -1, 1, -1);
            GL.Vertex3(0, 0, 1);
            GL.MultiTexCoord3(0, -1, -1, -1);
            GL.Vertex3(0, 1, 1);
            GL.MultiTexCoord3(0, -1, -1, 1);
            GL.Vertex3(1, 1, 1);
            GL.MultiTexCoord3(0, -1, 1, 1);
            GL.Vertex3(1, 0, 1);
            GL.End();

            // Positive Y
            Graphics.SetRenderTarget(dstCubemap, dstMip, CubemapFace.PositiveY);
            material.SetPass(shaderPass);
            GL.Begin(GL.QUADS);
            GL.MultiTexCoord3(0, -1, 1, -1);
            GL.Vertex3(0, 0, 1);
            GL.MultiTexCoord3(0, -1, 1, 1);
            GL.Vertex3(0, 1, 1);
            GL.MultiTexCoord3(0, 1, 1, 1);
            GL.Vertex3(1, 1, 1);
            GL.MultiTexCoord3(0, 1, 1, -1);
            GL.Vertex3(1, 0, 1);
            GL.End();

            // Negative Y
            Graphics.SetRenderTarget(dstCubemap, dstMip, CubemapFace.NegativeY);
            material.SetPass(shaderPass);
            GL.Begin(GL.QUADS);
            GL.MultiTexCoord3(0, -1, -1, 1);
            GL.Vertex3(0, 0, 1);
            GL.MultiTexCoord3(0, -1, -1, -1);
            GL.Vertex3(0, 1, 1);
            GL.MultiTexCoord3(0, 1, -1, -1);
            GL.Vertex3(1, 1, 1);
            GL.MultiTexCoord3(0, 1, -1, 1);
            GL.Vertex3(1, 0, 1);
            GL.End();

            // Positive Z
            Graphics.SetRenderTarget(dstCubemap, dstMip, CubemapFace.PositiveZ);
            material.SetPass(shaderPass);
            GL.Begin(GL.QUADS);
            GL.MultiTexCoord3(0, -1, 1, 1);
            GL.Vertex3(0, 0, 1);
            GL.MultiTexCoord3(0, -1, -1, 1);
            GL.Vertex3(0, 1, 1);
            GL.MultiTexCoord3(0, 1, -1, 1);
            GL.Vertex3(1, 1, 1);
            GL.MultiTexCoord3(0, 1, 1, 1);
            GL.Vertex3(1, 0, 1);
            GL.End();

            // Negative Z
            Graphics.SetRenderTarget(dstCubemap, dstMip, CubemapFace.NegativeZ);
            material.SetPass(shaderPass);
            GL.Begin(GL.QUADS);
            GL.MultiTexCoord3(0, 1, 1, -1);
            GL.Vertex3(0, 0, 1);
            GL.MultiTexCoord3(0, 1, -1, -1);
            GL.Vertex3(0, 1, 1);
            GL.MultiTexCoord3(0, -1, -1, -1);
            GL.Vertex3(1, 1, 1);
            GL.MultiTexCoord3(0, -1, 1, -1);
            GL.Vertex3(1, 0, 1);
            GL.End();

            GL.PopMatrix();
        }


        const int mipCount = 7; // Unity seems to always use 7 mip steps in total for reflection probe cubemaps
        public void SpecularConvolve(RenderTexture inputCubemap, RenderTexture outputCubemap)
        {
            // I had to copy this shader from BuiltinShaders to my project
            Material convolutionMaterial = new Material(Shader.Find("Hidden/CubeBlurOdd"));

            outputCubemap.filterMode = FilterMode.Trilinear;

            // Generate mip maps using the standard box filter (these will appear blocky if the cubemap is used for glossy reflections)
            // We will use these as the input to the convolution blur shader
            // Your cubemap RT must have been created with useMipMap set to true and it should use the maximum number of mipmaps for its resolution
            // If you already generating mips you can skip this
            inputCubemap.GenerateMips();

            // If you want to amortize, turn the function into a coroutine and yield for a frame here

            // Transfer mip 0 (this is done separately from the loop below as we do not want to blur it)
            for (int face = 0; face < 6; face++)
                Graphics.CopyTexture(inputCubemap, face, 0, outputCubemap, face, 0);

            // This will track the texel size for the current mip level
            float texelSize = 2f / (probe.resolution / 2); // Divided by 2 because we are going to start with mip 1, which is half-res
                                                     // Process mips 1-7
            for (int mip = 1; mip <= mipCount; mip++)
            {
                // If you want to amortize, turn the function into a coroutine and yield for a frame here

                convolutionMaterial.SetTexture("_MainTex", inputCubemap);
                convolutionMaterial.SetFloat("_Texel", texelSize);
                // Output mip range -> normalized range -> input mip range
                float level = (float)(mip + 1) / outputCubemap.mipmapCount * inputCubemap.mipmapCount;
                convolutionMaterial.SetFloat("_Level", level);

                CubemapBlit(outputCubemap, convolutionMaterial, 0, mip);

                texelSize *= 2;
            }
        }
    }
}

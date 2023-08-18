using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
[ExecuteAlways]
public class Atmosphere : MonoBehaviour
{
    public static List<Atmosphere> atmospheres = new List<Atmosphere>();


    [SerializeField] private float m_planetRadius = 2f;
    [SerializeField] private float m_atmosphereRadius = 2.5f;

    public float planetRadius => m_planetRadius * transform.lossyScale.x;
    public float atmosphereRadius => m_atmosphereRadius * transform.lossyScale.x;
    public Vector3 planetPosition => transform.position;

    public ComputeShader transmittanceLUTCompute;
	[HideInInspector] public RenderTexture transmittanceLUT;
    static readonly Vector2Int transmittanceLUTSize = new Vector2Int(64, 64);
    [System.Serializable]
    public class AtmosphereParameters
    {
        // Allow control over how strongly atmosphere affects appearance of objects in the sky (moon, stars)
        [Range(0, 1)] public float skyTransmittanceWeight = 1;
        [Header("Rayleigh Scattering")]
        // Wavelengths of red, green, and blue light (nanometres)
        public Vector3 wavelengthsRGB = new Vector3(700, 530, 460);
        // Scale value to adjust all wavelengths at once
        public float wavelengthScale = 300;
        // Altitude [0, 1] at which the average density of particles causing rayleigh scattering is found
        [Range(0, 1)] public float rayleighDensityAvg = 0.1f;

        [Header("Mie Scattering")]
        // Altitude [0, 1] at which the average density of particles causing mie scattering is found
        [Range(0, 1)] public float mieDensityAvg = 0.1f;
        // Strength of mie scattering
        public float mieCoefficient;
        // Strength of mie absorption
        public float mieAbsorption;

        [Header("Ozone")]
        //Altitude [0, 1] at which ozone density is at the greatest
        [Range(0, 1)] public float ozonePeakDensityAltitude = 0.25f;
        [Range(0, 10)] public float ozoneDensityFalloff = 4;
        [Range(0, 5)] public float ozoneStrength = 1;
        public Vector3 ozoneAbsorption;

        [Header("Aerial Perspective")]
        [Range(0, 1)] public float aerialPerspectiveStrength = 1;
        public float maxAerialPerspectiveDist = 1000;
    }

    public AtmosphereParameters atmosphereParameters = new AtmosphereParameters();

    [Range(0,1)]
    public float overcast = 0;
    [Range(0,1)]
    public float overcastSunOcclusion = 0.3f;

    public Sun sun;
    Quaternion sunRot;

    private void OnEnable()
    {
        atmospheres.Add(this);
        CalculateLUTs();
    }

    private void OnDisable()
    {
        atmospheres.Remove(this);
    }


    private void OnDrawGizmosSelected()
    {
        Gizmos.DrawWireSphere(transform.position, planetRadius);
        Gizmos.DrawWireSphere(transform.position, atmosphereRadius);
    }

    public Bounds bounds
    {
        get { return new Bounds(transform.position, Vector3.one * atmosphereRadius * 2); }
    }

    public Bounds ScreenSpaceBounds(Camera camera)
    {
        var wb = bounds;
        Vector3[] corners = new Vector3[]
        {
            new Vector3(wb.min.x, wb.min.y, wb.min.z),
            new Vector3(wb.min.x, wb.max.y, wb.min.z),
            new Vector3(wb.min.x, wb.min.y, wb.max.z),
            new Vector3(wb.min.x, wb.max.y, wb.max.z),
            new Vector3(wb.max.x, wb.min.y, wb.min.z),
            new Vector3(wb.max.x, wb.max.y, wb.min.z),
            new Vector3(wb.max.x, wb.min.y, wb.max.z),
            new Vector3(wb.max.x, wb.max.y, wb.max.z),
        };
        Bounds b = new Bounds(camera.WorldToViewportPoint(corners[0]), Vector3.zero);
        for (int i = 1; i < corners.Length; i++)
        {
            b.Encapsulate(camera.WorldToViewportPoint(corners[i]));
        }
        var min = b.min; if (min.x < 0) min.x = 0; if (min.y < 0) min.y = 0;
        var max = b.max; if (max.x > 1) max.x = 1; if (max.y > 1) max.y = 1;
        b.min = min; b.max = max;
        return b;
    }


    public ShaderValues sharedAtmosphereValues;
    [ContextMenu("Recalculate LUTs")]
    void CalculateLUTs()
    {
        sharedAtmosphereValues = GetShaderValues();
        sharedAtmosphereValues.Apply(transmittanceLUTCompute);

        InitAndRenderTransmittanceLUT();
    }

    Vector3 rayleighCoefficients;
    ShaderValues GetShaderValues()
    {
        ShaderValues values = new ShaderValues();
        // Size values
        values.floats.Add((Shader.PropertyToID("atmosphereThickness"), atmosphereRadius - planetRadius));
        values.floats.Add((Shader.PropertyToID("atmosphereRadius"), atmosphereRadius));
        values.floats.Add((Shader.PropertyToID("planetRadius"), planetRadius));
        values.floats.Add((Shader.PropertyToID("terrestrialClipDst"), planetRadius));

        values.floats.Add((Shader.PropertyToID("aerialPerspectiveStrength"), atmosphereParameters.aerialPerspectiveStrength));
        values.floats.Add((Shader.PropertyToID("skyTransmittanceWeight"), atmosphereParameters.skyTransmittanceWeight));


        // Rayleigh values
        values.floats.Add((Shader.PropertyToID("rayleighDensityAvg"), atmosphereParameters.rayleighDensityAvg));
        // Arbitrary scale to give nicer range of reasonable values for the scattering constant
        // Strength of (rayleigh) scattering is dependent on wavelength (~ 1/wavelength^4)
        Vector3 inverseWavelengths = new Vector3(1 / atmosphereParameters.wavelengthsRGB.x, 1 / atmosphereParameters.wavelengthsRGB.y, 1 / atmosphereParameters.wavelengthsRGB.z);
        rayleighCoefficients = inverseWavelengths * atmosphereParameters.wavelengthScale;
        rayleighCoefficients.x = Mathf.Pow(rayleighCoefficients.x, 4);
        rayleighCoefficients.y = Mathf.Pow(rayleighCoefficients.y, 4);
        rayleighCoefficients.z = Mathf.Pow(rayleighCoefficients.z, 4);
        //float grey = (rayleighCoefficients.x + rayleighCoefficients.y + rayleighCoefficients.z) / 3f;
        //grey *= 0.1f;
        //rayleighCoefficients = Vector3.Lerp(rayleighCoefficients, new Vector3(grey, grey, grey), overcast * 0.98f);
        values.vectors.Add((Shader.PropertyToID("rayleighCoefficients"), rayleighCoefficients));

        // Mie values
        values.floats.Add((Shader.PropertyToID("mieDensityAvg"), atmosphereParameters.mieDensityAvg));
        values.floats.Add((Shader.PropertyToID("mieCoefficient"), atmosphereParameters.mieCoefficient));
        values.floats.Add((Shader.PropertyToID("mieAbsorption"), atmosphereParameters.mieAbsorption));//

        // Ozone values
        values.floats.Add((Shader.PropertyToID("ozonePeakDensityAltitude"), atmosphereParameters.ozonePeakDensityAltitude));
        values.floats.Add((Shader.PropertyToID("ozoneDensityFalloff"), atmosphereParameters.ozoneDensityFalloff));
        values.vectors.Add((Shader.PropertyToID("ozoneAbsorption"), atmosphereParameters.ozoneAbsorption * atmosphereParameters.ozoneStrength * 0.1f));

        return values;
    }


    // Create lookup texture for the transmittance (proportion of light reaching given point through the atmosphere)
    // This only needs to be created once at the start (or whenever atmosphere parameters are changed)
    void InitAndRenderTransmittanceLUT()
    {
        GraphicsFormat transmittanceLUTFormat = GraphicsFormat.R16G16B16A16_UNorm;//
        ComputeHelper.CreateRenderTexture(ref transmittanceLUT, transmittanceLUTSize.x, transmittanceLUTSize.y, FilterMode.Bilinear, transmittanceLUTFormat, "Transmittance LUT");
        transmittanceLUTCompute.SetTexture(0, "TransmittanceLUT", transmittanceLUT);
        transmittanceLUTCompute.SetInt("width", transmittanceLUTSize.x);
        transmittanceLUTCompute.SetInt("height", transmittanceLUTSize.y);
        ComputeHelper.Dispatch(transmittanceLUTCompute, transmittanceLUT, 0);
        cpuLUTNeedsUpdate = true;
    }

    public class ShaderValues
    {
        public List<(int id, float value)> floats;
        public List<(int id, int value)> ints;
        public List<(int id, Vector4 value)> vectors;

        public ShaderValues()
        {
            floats = new List<(int id, float value)>();
            ints = new List<(int id, int value)>();
            vectors = new List<(int id, Vector4 value)>();
        }

        public void Apply(Material material)
        {
            foreach (var data in floats)
            {
                material.SetFloat(data.id, data.value);
            }

            foreach (var data in ints)
            {
                material.SetInt(data.id, data.value);
            }

            foreach (var data in vectors)
            {
                material.SetVector(data.id, data.value);
            }
        }
        public void Apply(UnityEngine.Rendering.PostProcessing.PropertySheet sheet)
        {
            foreach (var data in floats)
            {
                sheet.properties.SetFloat(data.id, data.value);
            }

            foreach (var data in ints)
            {
                sheet.properties.SetInt(data.id, data.value);
            }

            foreach (var data in vectors)
            {
                sheet.properties.SetVector(data.id, data.value);
            }
        }

        public void Apply(ComputeShader compute)
        {
            foreach (var data in floats)
            {
                compute.SetFloat(data.id, data.value);
            }

            foreach (var data in ints)
            {
                compute.SetInt(data.id, data.value);
            }

            foreach (var data in vectors)
            {
                compute.SetVector(data.id, data.value);
            }
        }

        public void ApplyCommand(CommandBuffer cmd, ComputeShader compute)
        {
            foreach (var data in floats)
            {
                cmd.SetComputeFloatParam(compute, data.id, data.value);
            }

            foreach (var data in ints)
            {
                cmd.SetComputeIntParam(compute, data.id, data.value);
            }

            foreach (var data in vectors)
            {
                cmd.SetComputeVectorParam(compute, data.id, data.value);
            }
        }
    }

    const float sunRotationThreshold = 1f;
    private void Update()
    {
        if(sun != null && MainCamera.exists)
        {
            Color dimmedSunColor = sun.baseColor * EvaluateTransmittanceCPU(MainCamera.position, -sun.direction);
            dimmedSunColor =  Color.Lerp(dimmedSunColor, dimmedSunColor * overcastSunOcclusion, overcast);
            sun.SetOccludedColor(dimmedSunColor);
            sun.light.enabled = dimmedSunColor.maxColorComponent * sun.baseIntensity > 0.001f;
            sun.light.intensity = sun.baseIntensity;

            if(Quaternion.Angle(sun.transform.rotation, sunRot) > sunRotationThreshold)
            {
                DynamicAmbientLight.SetLightingDirty();
                sunRot = sun.transform.rotation;
            }
        }
    }

    Color[] transmittanceLUTCPU;
    Vector2Int transmittanceLUTDimensions;
    bool cpuLUTNeedsUpdate = false;
    Color EvaluateTransmittanceCPU(Vector3 pos, Vector3 dir)
    {
        if(transmittanceLUTCPU == null || cpuLUTNeedsUpdate)
        {
            var transmittanceLUTTex = new Texture2D(transmittanceLUT.width, transmittanceLUT.height);
            var rt = RenderTexture.active;
            RenderTexture.active = transmittanceLUT;
            transmittanceLUTTex.ReadPixels(new Rect(0, 0, transmittanceLUT.width, transmittanceLUT.height), 0, 0, false);
            transmittanceLUTTex.Apply();
            RenderTexture.active = rt;
            transmittanceLUTCPU = transmittanceLUTTex.GetPixels();
            transmittanceLUTDimensions = new Vector2Int(transmittanceLUTTex.width, transmittanceLUTTex.height);
            cpuLUTNeedsUpdate = false;
        }
        pos = transform.InverseTransformPoint(pos);
        dir = transform.InverseTransformDirection(dir);
        float TRANSMITTANCE_LUT_SPACEPOS = 0.8f;
        float TRANSMITTANCE_LUT_SPACESIZE = 0.2f;
        float atmosphereThickness = atmosphereRadius - planetRadius;
        float dstFromCentre = pos.magnitude;
        float height = dstFromCentre - planetRadius;
        float height01 = height / atmosphereThickness;

        float uvX = Vector3.Dot(pos / dstFromCentre, dir) * 0.5f + 0.5f;

        if (height01 < 1)
        {
            // in atmosphere
            height01 = Mathf.Clamp01(height01) * TRANSMITTANCE_LUT_SPACEPOS;
        }
        else
        {
            // in space
            height01 = (dstFromCentre / atmosphereRadius - 1) * TRANSMITTANCE_LUT_SPACESIZE + TRANSMITTANCE_LUT_SPACEPOS;
            if (height01 > 1)
            {
                uvX *= (dstFromCentre / atmosphereRadius / 2) * (dstFromCentre / atmosphereRadius / 2);
            }
        }
        uvX = 1 - uvX;

        // bilinear sampling
        Vector2 pixelCoords = new Vector2(Mathf.Clamp01(uvX) * transmittanceLUTDimensions.x, Mathf.Clamp01(height01) * transmittanceLUTDimensions.y);
        Vector2Int pixelFloor = new Vector2Int(Mathf.FloorToInt(pixelCoords.x), Mathf.FloorToInt(pixelCoords.y));
        Vector2Int pixelCeil = new Vector2Int(Mathf.CeilToInt(pixelCoords.x), Mathf.CeilToInt(pixelCoords.y));
        Vector2 pixelFrac = pixelCoords - pixelFloor;
        Color a = Color.Lerp(transmittanceLUTCPU[pixelFloor.x + pixelFloor.y * transmittanceLUTDimensions.x],
           transmittanceLUTCPU[pixelCeil.x + pixelFloor.y * transmittanceLUTDimensions.x], pixelFrac.x);
        Color b = Color.Lerp(transmittanceLUTCPU[pixelFloor.x + pixelCeil.y * transmittanceLUTDimensions.x],
           transmittanceLUTCPU[pixelCeil.x + pixelCeil.y * transmittanceLUTDimensions.x], pixelFrac.x);
        return Color.Lerp(a, b, pixelFrac.y);
    }
}

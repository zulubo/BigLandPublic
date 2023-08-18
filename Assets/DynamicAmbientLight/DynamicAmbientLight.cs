using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class DynamicAmbientLight : MonoBehaviour
{
    public static DynamicAmbientLight instance;
    static bool lightingDirty = false;
    public float ambientSmoothingSpeed = 5;
    public static void SetLightingDirty()
    {
        lightingDirty = true;
    }

    public Reflector.ReflectorProbe reflectorProbe;

    private void OnEnable()
    {
        instance = this;
        updating = false;

        if (Application.isPlaying)
        {
            StartCoroutine(SmoothAmbientLighting());
        }
#if UNITY_EDITOR
        else
        {
            Unity.EditorCoroutines.Editor.EditorCoroutineUtility.StartCoroutine(SmoothAmbientLighting(), this);
        }
#endif
    }

    private void Update()
    {
        if (lightingDirty && !updating)
        {
            if (Application.isPlaying)
            {
                StartCoroutine(UpdateLighting());
            }
#if UNITY_EDITOR
            else
            {
                Unity.EditorCoroutines.Editor.EditorCoroutineUtility.StartCoroutine(UpdateLighting(), this);
            }
#endif
            lightingDirty = false;
        }
    }

    bool updating;
    IEnumerator UpdateLighting()
    {
        updating = true;
        reflectorProbe.RefreshReflectionAt(Reflector.RefreshMode.Overtime, MainCamera.position);
        while (reflectorProbe.Refreshing) yield return null;
        RenderSettings.customReflectionTexture = reflectorProbe.Cubemap;
        Vector4[] probeData = new Vector4[9];
        if(SphericalHarmonics.GPU_Project_Uniform_9Coeff(reflectorProbe.Cubemap, probeData))
        {
            for (int rgb = 0; rgb < 3; rgb++)
            {
                for (int c = 0; c < 9; c++)
                { // transfer baked harmonics into float array
                    ambientCoefficients[rgb * 9 + c] = rgb == 0 ? probeData[c].x : (rgb == 1 ? probeData[c].y : probeData[c].z);
                }
            }
        }
        updating = false;
    }
    float[] ambientCoefficients = new float[27];
    float[] ambientCoefficientsSmoothed = new float[27];
    UnityEngine.Rendering.SphericalHarmonicsL2 ambientProbe;
    IEnumerator SmoothAmbientLighting()
    {
        while (true)
        {
            yield return null;
            float dt = Time.deltaTime * ambientSmoothingSpeed;
            for (int i = 0; i < 27; i++)
            { // smoothly interpolate towards current ambient conditions
                ambientCoefficientsSmoothed[i] = Mathf.Lerp(ambientCoefficientsSmoothed[i], ambientCoefficients[i], Time.deltaTime * ambientSmoothingSpeed);
            }
            for (int rgb = 0; rgb < 3; rgb++)
            {
                for (int c = 0; c < 9; c++)
                { // transfer smoothed coefficients into SphericalHarmonics object
                    if(c > 3)
                        ambientProbe[rgb, c] = ambientCoefficientsSmoothed[rgb * 9 + c];
                    else
                        ambientProbe[rgb, c] = ambientCoefficientsSmoothed[rgb * 9 + c];
                }
            }
            RenderSettings.ambientProbe = ambientProbe;
        }
    }
}

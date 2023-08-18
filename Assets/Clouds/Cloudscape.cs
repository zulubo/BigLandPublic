using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class Cloudscape : MonoBehaviour
{
    public static Cloudscape instance;

    public Atmosphere atmosphere;
    public float cloudAltitude = 1000;
    public float cloudThickness = 500;
    public float densityMultiplier = 1;
    public float maxStepLength = 0.5f;
    public float stepLengthExponent = 0.5f;
    public AnimationCurve densityCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(0.3f, 0.5f), new Keyframe(0.7f, 0.25f), new Keyframe(1, 0));
    Texture2D densityLUT;
    [ContextMenu("Generate Density LUT")]
    void GenerateDensityLUT()
    {
        if (densityLUT == null)
        {
            densityLUT = new Texture2D(32, 1, TextureFormat.R16, false);
            densityLUT.wrapMode = TextureWrapMode.Clamp;
        }
        for (int i = 0; i < 32; i++)
        {
            densityLUT.SetPixel(i, 0, new Color(densityCurve.Evaluate(i / 32f), 0, 0, 0));
        }
        densityLUT.Apply();
    }
    public Texture2D GetDensityLUT()
    {
        if (densityLUT == null) GenerateDensityLUT();
        return densityLUT;
    }
    public float shapeScale = 1000;
    public float detailScale = 200;
    public float detailStrength = 0.1f;

    public float ambientOcclusion = 5;


    private void OnEnable()
    {
        instance = this;
    }
    private void OnDisable()
    {
        if(instance == this) instance = null;
    }
}

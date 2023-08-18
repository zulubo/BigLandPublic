using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Sun : MonoBehaviour
{
    public static Sun instance;
    public Color baseColor = Color.white;
    public float baseIntensity = 1;
    public new Light light
    {
        get
        {
            if (_light == null) _light = GetComponent<Light>();
            return _light;
        }
    }
    Light _light;

    public Vector3 direction => transform.forward;

    private void OnEnable()
    {
        instance = this;
    }
    private void OnDisable()
    {
        if (instance == this) instance = null;
    }

    public void SetOccludedColor(Color color)
    {
        light.color = color;
    }
}

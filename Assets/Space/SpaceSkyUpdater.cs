using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class SpaceSkyUpdater : MonoBehaviour
{
    public Sun sun;
    public Material skyboxMat;

    public Transform skyTransform;
    public Transform ringTransform;

    private void Update()
    {
        skyboxMat.SetVector("_SunDir", skyTransform != null ? skyTransform.InverseTransformDirection(-sun.direction) : -sun.direction);
        skyboxMat.SetColor("_SunColor", sun.baseColor * sun.baseIntensity);
        if(skyTransform != null) skyboxMat.SetMatrix("worldToSkySpace", skyTransform.worldToLocalMatrix);
        if (ringTransform != null) skyboxMat.SetMatrix("worldToRingSpace", ringTransform.worldToLocalMatrix);
        skyboxMat.SetVector("_RingSunDir", ringTransform != null ? ringTransform.InverseTransformDirection(-sun.direction) : -sun.direction);
    }
}

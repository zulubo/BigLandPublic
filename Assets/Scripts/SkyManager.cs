using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class SkyManager : MonoBehaviour
{
    public bool overcast;
    [SerializeField] float cloudHeight = 900;
    [SerializeField] GameObject cloudDome;
    [SerializeField] GameObject cloudscape;
    [SerializeField] Renderer cloudTransition;
    [SerializeField] float cloudTransitionFadeLength = 50f;
    [SerializeField] float cloudTransitionSolidLength = 20f;
    [SerializeField] Color cloudTransitionColor = Color.grey;
    [SerializeField] Atmosphere atmosphere;
    [SerializeField] Sun sun;

    private void LateUpdate()
    {
        if (overcast)
        {
            float h = MainCamera.position.y;
            atmosphere.overcast = Mathf.InverseLerp(cloudHeight + cloudTransitionSolidLength * 0.5f, cloudHeight - cloudTransitionSolidLength * 0.5f, h);
            cloudDome.SetActive(h < cloudHeight);
            cloudscape.SetActive(h > cloudHeight);
            float transitionOpacity = Mathf.Clamp01(1 - (Mathf.Abs(h - cloudHeight) - cloudTransitionSolidLength * 0.5f) / cloudTransitionFadeLength);
            cloudTransition.gameObject.SetActive(transitionOpacity > 0);
            if (transitionOpacity > 0)
            {
                Color c = cloudTransitionColor * sun.light.color * sun.light.intensity;
                c.a = transitionOpacity;
                cloudTransition.sharedMaterial.SetColor("_Color", c);
                cloudTransition.transform.position = MainCamera.position;
            }
        }
        else
        {
            atmosphere.overcast = 0;
            cloudDome.SetActive(false);
            cloudscape.SetActive(false);
            cloudTransition.gameObject.SetActive(false);
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class MainCamera : MonoBehaviour
{
    public static Camera instance
    {
        get
        {
            if (Application.isPlaying)
            {
                return _instance;
            }
            else
            {
#if UNITY_EDITOR
                if (UnityEditor.SceneView.lastActiveSceneView != null)
                    return UnityEditor.SceneView.lastActiveSceneView.camera;
                else return null;
#endif
            }
        }
    }
    private static Camera _instance;
    public static bool exists => instance != null;
    Camera _camera;
    public static Transform transformComp => instance != null ? instance.transform : null;
    public static Vector3 position => instance != null ? instance.transform.position : Vector3.zero;
    public static Quaternion rotation => instance != null ? instance.transform.rotation : Quaternion.identity;

    private void OnEnable()
    {
        if (_camera == null) _camera = GetComponent<Camera>();
        _instance = _camera;
    }
    private void OnDisable()
    {
        if (instance == this) _instance = null;
    }
}

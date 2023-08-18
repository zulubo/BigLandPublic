using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
[ExecuteAlways]
public class HUDLinearMeter : MonoBehaviour
{
    [SerializeField] RectTransform line;
    public float increment = 100;
    public float incrementSpacing = 10f;
    [SerializeField] GameObject markerPrefab;
    [SerializeField] GameObject subMarkerPrefab;
    public int subMarkerCount = 1;
    private class MarkerInstance
    {
        public GameObject gameObject;
        public TMP_Text text;

        public MarkerInstance(GameObject gameObject)
        {
            this.gameObject = gameObject;
            this.text = gameObject.GetComponentInChildren<TMP_Text>();
        }
    }
    private List<MarkerInstance> instancedMarkers = new List<MarkerInstance>();
    private List<MarkerInstance> instancedSubMarkers = new List<MarkerInstance>();

    public float value;

    private void OnEnable()
    {
        CreateMarkers();
    }

    void CreateMarkers()
    {
        for (int i = 0; i < instancedMarkers.Count; i++)
        {
            if (instancedMarkers[i] != null && instancedMarkers[i].gameObject != null) DestroyImmediate(instancedMarkers[i].gameObject);
        }
        instancedMarkers.Clear();
        for (int i = 0; i < instancedSubMarkers.Count; i++)
        {
            if (instancedSubMarkers[i] != null && instancedSubMarkers[i].gameObject != null) DestroyImmediate(instancedSubMarkers[i].gameObject);
        }
        instancedSubMarkers.Clear();

        int markerCount = Mathf.CeilToInt(line.rect.width / incrementSpacing) + 1;
        for (int i = 0; i < markerCount; i++)
        {
            var inst = Instantiate(markerPrefab, line.transform);
            inst.SetActive(true);
            inst.hideFlags = HideFlags.HideAndDontSave;
            instancedMarkers.Add(new MarkerInstance(inst));
        }
        int subMarkerCountTotal = (markerCount) * subMarkerCount;
        for (int i = 0; i < subMarkerCountTotal; i++)
        {
            var inst = Instantiate(subMarkerPrefab, line.transform);
            inst.SetActive(true);
            inst.hideFlags = HideFlags.HideAndDontSave;
            instancedSubMarkers.Add(new MarkerInstance(inst));
        }
        UpdateMarkers();
    }

    private void Update()
    {
        UpdateMarkers();
    }

    void UpdateMarkers()
    {
        float valueScaled = value / increment - (line.rect.width / incrementSpacing) * 0.5f;
        int step = Mathf.FloorToInt(valueScaled);
        float frac = valueScaled - step;

        for (int i = 0; i < instancedMarkers.Count; i++)
        {
            float pos = line.rect.width * -0.5f + (i - frac) * incrementSpacing;
            instancedMarkers[i].gameObject.transform.localPosition = new Vector3(pos, 0, 0);
            instancedMarkers[i].text.text = ((step + i) * increment).ToString();
            instancedMarkers[i].gameObject.SetActive(pos < line.rect.max.x && pos > line.rect.min.x);
        }

        if (subMarkerCount > 0)
        {
            for (int i = 0; i < instancedSubMarkers.Count; i++)
            {
                int m = i / subMarkerCount;
                float pos = line.rect.width * -0.5f + ((m + (i % subMarkerCount + 1) / (subMarkerCount + 1.0f)) - frac) * incrementSpacing;
                instancedSubMarkers[i].gameObject.transform.localPosition = new Vector3(pos, 0, 0);
                instancedSubMarkers[i].gameObject.SetActive(pos < line.rect.max.x && pos > line.rect.min.x);
            }
        }
    }

    private void OnDisable()
    {
        for (int i = 0; i < instancedMarkers.Count; i++)
        {
            if (instancedMarkers[i] != null && instancedMarkers[i].gameObject != null) DestroyImmediate(instancedMarkers[i].gameObject);
        }
        instancedMarkers.Clear();
        for (int i = 0; i < instancedSubMarkers.Count; i++)
        {
            if (instancedSubMarkers[i] != null && instancedSubMarkers[i].gameObject != null) DestroyImmediate(instancedSubMarkers[i].gameObject);
        }
        instancedSubMarkers.Clear();
    }
}

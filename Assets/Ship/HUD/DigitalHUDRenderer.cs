using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class DigitalHUDRenderer : MonoBehaviour
{
    new Renderer renderer 
    { 
        get 
        { 
            if (_renderer == null) _renderer = GetComponent<Renderer>(); 
            return _renderer; 
        } 
    }
    Renderer _renderer;
    MaterialPropertyBlock props;

    private bool propsDirty = false;

    [SerializeField] [ColorUsage(true,true)] private Color _color = Color.white;
    public Color color
    {
        get { return _color; }
        set
        {
            if (_color != value)
            {
                _color = value;
                propsDirty = true;
            }
        }
    }

    [SerializeField] private Texture _texture;
    public Texture texture
    {
        get { return _texture; }
        set
        {
            if (_texture != value)
            {
                _texture = value;
                propsDirty = true;
            }
        }
    }

    [SerializeField] private Vector4 _scaleOffset = new Vector4(1,1,0,0);
    public Vector4 scaleOffset
    {
        get { return _scaleOffset; }
        set
        {
            if (_scaleOffset != value)
            {
                _scaleOffset = value;
                propsDirty = true;
            }
        }
    }

    public enum FillMode
    {
        None = 0,
        LeftToRight = 1,
        RightToLeft = 2,
        BottomToTop = 3,
        TopToBottom = 4,
        Clockwise = 5,
        CounterClockwise = 6,
    }

    [SerializeField] private FillMode _fillMode = FillMode.None;
    public FillMode fillMode
    {
        get { return _fillMode; }
        set
        {
            if (_fillMode != value)
            {
                _fillMode = value;
                propsDirty = true;
            }
        }
    }
    [SerializeField] [Range(0,1)] private float _fillAmount = 1;
    public float fillAmount
    {
        get { return _fillAmount; }
        set
        {
            if (_fillAmount != value)
            {
                _fillAmount = value;
                propsDirty = true;
            }
        }
    }

    private float seed;

    private void Start()
    {
        seed = Random.Range(-10f, 10f);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        renderer.allowOcclusionWhenDynamic = false;
    }
    private void OnEnable()
    {
        propsDirty = true;
    }
    private void OnValidate()
    {
        propsDirty = true;
    }

    int prop_color = Shader.PropertyToID("_Color");
    int prop_maintex = Shader.PropertyToID("_MainTex");
    int prop_maintexST = Shader.PropertyToID("_MainTex_ST");
    int prop_seed = Shader.PropertyToID("_RandomSeed");
    int prop_fillMode = Shader.PropertyToID("_FillMode");
    int prop_fill = Shader.PropertyToID("_Fill");
    private void Update()
    {
        if (propsDirty)
        {
            propsDirty = false;

            if (props == null) props = new MaterialPropertyBlock();
            props.Clear();
            props.SetColor(prop_color, _color);
            if(_texture != null) props.SetTexture(prop_maintex, _texture);
            props.SetVector(prop_maintexST, _scaleOffset);
            props.SetFloat(prop_seed, seed);
            props.SetInt(prop_fillMode, (int)_fillMode);
            props.SetFloat(prop_fill, _fillAmount);

            renderer.SetPropertyBlock(props);
        }
    }
}

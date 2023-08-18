using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class GrassRenderer : MonoBehaviour
{
    public Mesh mesh;
    public Material material;
    public float bladeSize = 1;
    [Space()]
    public float idealChunkSize = 10f;
    public float grassDensity = 1f;
    public float cullDistance = 100;
    
    private Vector2Int chunkCount;
    private Vector2 chunkSize;

    Terrain terrain;
    ComputeShader chunkGeneratorShader
    {
        get
        {
            if (_chunkGeneratorShader == null)
            {
                _chunkGeneratorShader = (ComputeShader)Resources.Load("GrassChunkGenerator", typeof(ComputeShader));
            }
            return _chunkGeneratorShader;
        }
    }
    ComputeShader _chunkGeneratorShader;
    static readonly int grassBufferProp = Shader.PropertyToID("GrassBuffer");

    private class Chunk
    {
        public Terrain terrain;
        public Vector2Int index;
        public Vector2 size;
        public int grassCount;

        public bool active = true;
        public void SetActive(bool value) => active = value;

        public ComputeBuffer grassBuffer { get; private set; }

        public Bounds bounds { get; private set; }

        public RenderParams renderParams;

        public Chunk(Terrain terrain, Vector2Int index, Vector2 size, float density)
        {
            this.terrain = terrain;
            this.index = index;
            this.size = size;
            this.grassCount = Mathf.RoundToInt(size.x * size.y * density);
            Vector3 min = terrain.transform.TransformPoint(new Vector3(index.x * size.x, 0, index.y * size.y));
            Vector3 max = terrain.transform.TransformPoint(new Vector3((index.x + 1) * size.x, terrain.terrainData.size.y, (index.y + 1) * size.y));
            bounds = new Bounds((min + max) * 0.5f, max - min + Vector3.one);
        }
        public void Generate(ComputeShader compute)
        {
            grassBuffer = new ComputeBuffer(grassCount, 4 + 4);

            compute.SetTexture(0, "TerrainHeight", terrain.terrainData.heightmapTexture);
            compute.SetInt("heightmapDimensions", terrain.terrainData.heightmapResolution);
            compute.SetMatrix("terrainTransform", terrain.transform.localToWorldMatrix);
            compute.SetVector("terrainSize", terrain.terrainData.size);
            compute.SetVector("chunkCoords", (Vector2)index);
            compute.SetVector("chunkSize", size);
            compute.SetBuffer(0, grassBufferProp, grassBuffer);
            compute.Dispatch(0, grassCount, 1, 1);
        }

        public void Dispose()
        {
            if (grassBuffer != null) grassBuffer.Dispose();
        }

    }

    Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
    Dictionary<Vector2Int, Chunk> oldChunks = new Dictionary<Vector2Int, Chunk>();
    List<Vector2Int> newChunks = new List<Vector2Int>();
    private List<Chunk> allChunks = new List<Chunk>();

    Vector2Int camPos;
    Vector2Int oldCamPos = new Vector2Int(-1000, -1000);

    private void OnEnable()
    {
        if (terrain == null) terrain = GetComponent<Terrain>();
        chunkCount = new Vector2Int(Mathf.CeilToInt(terrain.terrainData.size.x / idealChunkSize),
            Mathf.CeilToInt(terrain.terrainData.size.z / idealChunkSize));
        chunkSize = new Vector2(terrain.terrainData.size.x / chunkCount.x, terrain.terrainData.size.z / chunkCount.y);

        oldCamPos = new Vector2Int(-1000, -1000);
    }


    private void OnDisable()
    {
        for (int i = 0; i < allChunks.Count; i++)
        {
            allChunks[i].Dispose();
        }
        chunks.Clear();
        oldChunks.Clear();
        newChunks.Clear();
        allChunks.Clear();
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        // Ensure continuous Update calls.
        if (!Application.isPlaying)
        {
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            UnityEditor.SceneView.RepaintAll();
        }

        //if (allChunks.Count > 0) Gizmos.DrawWireCube(allChunks[0].bounds.center, allChunks[0].bounds.size);
    }
#endif

    void Update()
    {
        if (MainCamera.exists) camPos = WorldToChunk(MainCamera.position);


        if (camPos != oldCamPos)
        {
            UpdateGrass();
            oldCamPos = camPos;
        }

        for (int i = 0; i < allChunks.Count; i++)
        {
            if (allChunks[i] == null || !allChunks[i].active || allChunks[i].grassBuffer == null || allChunks[i].grassCount == 0) continue;

            RenderChunk(allChunks[i]);
        }
    }

    void UpdateGrass()
    {
        Vector2Int closestChunk = camPos;

        int radius = Mathf.CeilToInt(cullDistance / Mathf.Max(chunkSize.x,chunkSize.y)) + 1;

        oldChunks.Clear();
        oldChunks = new Dictionary<Vector2Int, Chunk>(chunks);
        newChunks.Clear();
        for (int x = closestChunk.x - radius; x <= closestChunk.x + radius; x++)
        {
            if (x < 0 || x >= chunkCount.x) continue;
            for (int z = closestChunk.y - radius; z <= closestChunk.y + radius; z++)
            {
                if (z < 0 || z >= chunkCount.y) continue;
                Vector2Int v = new Vector2Int(x, z);
                newChunks.Add(v);

                if (!chunks.ContainsKey(v)) // is a chunk that was not present previously
                {
                    // make new chunk here
                    var nc = new Chunk(terrain, v, chunkSize, grassDensity);
                    nc.Generate(chunkGeneratorShader);
                    allChunks.Add(nc);
                    chunks.Add(v, nc);
                }
                else if (!chunks[v].active)
                {
                    // enable existing chunk that has returned to active radius
                    chunks[v].SetActive(true);
                }
            }
        }

        // dispose old chunks
        foreach (KeyValuePair<Vector2Int, Chunk> chunkKey in oldChunks)
        {
            if (!newChunks.Contains(chunkKey.Key))
            {
                //chunk.Value.SetActive(false);
                if(chunks.ContainsKey(chunkKey.Key)) chunks.Remove(chunkKey.Key);
                if (allChunks.Contains(chunkKey.Value)) allChunks.Remove(chunkKey.Value);
                chunkKey.Value.Dispose();
            }
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.SceneView.RepaintAll();
        }
#endif
    }

    Vector2Int WorldToChunk(Vector3 position)
    {
        position = terrain.transform.InverseTransformPoint(position);
        position.x /= chunkSize.x;
        position.z /= chunkSize.y;
        return new Vector2Int(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.z));
    }

    private void RenderChunk(Chunk chunk)
    {
        if(chunk.renderParams.material == null)
        {
            chunk.renderParams = new RenderParams(material);
            material.EnableKeyword("UNITY_INSTANCING_ENABLED");
            chunk.renderParams.matProps = new MaterialPropertyBlock();
            chunk.renderParams.worldBounds = chunk.bounds;
            chunk.renderParams.matProps = new MaterialPropertyBlock();
            chunk.renderParams.matProps.SetBuffer(grassBufferProp, chunk.grassBuffer);
            chunk.renderParams.matProps.SetFloat("_BladeSize", bladeSize);
            chunk.renderParams.matProps.SetVector("_ChunkSize", chunk.size);
            chunk.renderParams.matProps.SetFloat("_CullDist", cullDistance);
            chunk.renderParams.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            chunk.renderParams.receiveShadows = true;
        }

        Vector3 position = new Vector3(chunk.index.x * chunkSize.x, 0, chunk.index.y * chunkSize.y);
        position = terrain.transform.TransformPoint(position);
        Quaternion rotation = terrain.transform.rotation;
        Vector3 scale = terrain.transform.lossyScale;
        Matrix4x4 localMatrix = Matrix4x4.TRS(position, rotation, scale);
        chunk.renderParams.matProps.SetMatrix("_ObjectToWorld", localMatrix);
        if(MainCamera.exists) chunk.renderParams.matProps.SetVector("_ViewPos", localMatrix.inverse.MultiplyPoint(MainCamera.position));

        Graphics.RenderMeshPrimitives(chunk.renderParams, mesh, 0, chunk.grassCount);
    }

}

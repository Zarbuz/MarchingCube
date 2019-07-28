using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class MeshGenerator : MonoBehaviour
{
    private const int ThreadGroupSize = 8;

    [Header("General Settings")]
    public DensityGenerator DensityGenerator;

    public bool FixedMapSize;
    [ConditionalHide(nameof(FixedMapSize), true)]
    public Vector3Int NumChunks = Vector3Int.one;
    [ConditionalHide(nameof(FixedMapSize), false)]
    public Transform Viewer;
    [ConditionalHide(nameof(FixedMapSize), false)]
    public float ViewDistance = 30;

    [Space()]
    public bool AutoUpdateInEditor = true;
    public bool AutoUpdateInGame = true;
    public ComputeShader Shader;
    public Material Mat;
    public bool GenerateColliders;

    [Header("Voxel Settings")]
    public float IsoLevel;
    public float BoundsSize = 1;
    public Vector3 Offset = Vector3.zero;

    [Range(2, 100)]
    public int NumPointsPerAxis = 30;

    [Header("Gizmos")]
    public bool ShowBoundsGizmo = true;
    public Color BoundsGizmoCol = Color.white;

    private GameObject _chunkHolder;
    private const string ChunkHolderName = "Chunks Holder";
    private List<Chunk> _chunks;
    private Dictionary<Vector3Int, Chunk> _existingChunks;
    private Queue<Chunk> _recycleableChunks;

    // Buffers
    private ComputeBuffer _triangleBuffer;
    private ComputeBuffer _pointsBuffer;
    private ComputeBuffer _triCountBuffer;

    private bool _settingsUpdated;

    private void Awake()
    {

        if (Application.isPlaying && !FixedMapSize)
        {
            InitVariableChunkStructures();

            var oldChunks = FindObjectsOfType<Chunk>();
            for (int i = oldChunks.Length - 1; i >= 0; i--)
            {
                Destroy(oldChunks[i].gameObject);
            }
        }
    }

    private void Update()
    {
        // Update endless terrain
        if ((Application.isPlaying && !FixedMapSize))
        {
            Run();
        }

        if (_settingsUpdated)
        {
            RequestMeshUpdate();
            _settingsUpdated = false;
        }
    }

    public void Run()
    {
        CreateBuffers();

        if (FixedMapSize)
        {
            InitChunks();
            UpdateAllChunks();

        }
        else
        {
            if (Application.isPlaying)
            {
                InitVisibleChunks();
            }
        }

        // Release buffers immediately in editor
        if (!Application.isPlaying)
        {
            ReleaseBuffers();
        }

    }

    public void RequestMeshUpdate()
    {
        if ((Application.isPlaying && AutoUpdateInGame) || (!Application.isPlaying && AutoUpdateInEditor))
        {
            Run();
        }
    }

    private void InitVariableChunkStructures()
    {
        _recycleableChunks = new Queue<Chunk>();
        _chunks = new List<Chunk>();
        _existingChunks = new Dictionary<Vector3Int, Chunk>();
    }

    private void InitVisibleChunks()
    {
        if (_chunks == null)
        {
            return;
        }
        CreateChunkHolder();

        Vector3 p = Viewer.position;
        Vector3 ps = p / BoundsSize;
        Vector3Int viewerCoord = new Vector3Int(Mathf.RoundToInt(ps.x), Mathf.RoundToInt(ps.y), Mathf.RoundToInt(ps.z));

        int maxChunksInView = Mathf.CeilToInt(ViewDistance / BoundsSize);
        float sqrViewDistance = ViewDistance * ViewDistance;

        // Go through all existing chunks and flag for recyling if outside of max view dst
        for (int i = _chunks.Count - 1; i >= 0; i--)
        {
            Chunk chunk = _chunks[i];
            Vector3 centre = CentreFromCoord(chunk.coord);
            Vector3 viewerOffset = p - centre;
            Vector3 o = new Vector3(Mathf.Abs(viewerOffset.x), Mathf.Abs(viewerOffset.y), Mathf.Abs(viewerOffset.z)) - Vector3.one * BoundsSize / 2;
            float sqrDst = new Vector3(Mathf.Max(o.x, 0), Mathf.Max(o.y, 0), Mathf.Max(o.z, 0)).sqrMagnitude;
            if (sqrDst > sqrViewDistance)
            {
                _existingChunks.Remove(chunk.coord);
                _recycleableChunks.Enqueue(chunk);
                _chunks.RemoveAt(i);
            }
        }

        for (int x = -maxChunksInView; x <= maxChunksInView; x++)
        {
            for (int y = -maxChunksInView; y <= maxChunksInView; y++)
            {
                for (int z = -maxChunksInView; z <= maxChunksInView; z++)
                {
                    Vector3Int coord = new Vector3Int(x, y, z) + viewerCoord;

                    if (_existingChunks.ContainsKey(coord))
                    {
                        continue;
                    }

                    Vector3 centre = CentreFromCoord(coord);
                    Vector3 viewerOffset = p - centre;
                    Vector3 o = new Vector3(Mathf.Abs(viewerOffset.x), Mathf.Abs(viewerOffset.y), Mathf.Abs(viewerOffset.z)) - Vector3.one * BoundsSize / 2;
                    float sqrDst = new Vector3(Mathf.Max(o.x, 0), Mathf.Max(o.y, 0), Mathf.Max(o.z, 0)).sqrMagnitude;

                    // Chunk is within view distance and should be created (if it doesn't already exist)
                    if (sqrDst <= sqrViewDistance)
                    {

                        Bounds bounds = new Bounds(CentreFromCoord(coord), Vector3.one * BoundsSize);
                        if (IsVisibleFrom(bounds, Camera.main))
                        {
                            if (_recycleableChunks.Count > 0)
                            {
                                Chunk chunk = _recycleableChunks.Dequeue();
                                chunk.coord = coord;
                                _existingChunks.Add(coord, chunk);
                                _chunks.Add(chunk);
                                UpdateChunkMesh(chunk);
                            }
                            else
                            {
                                Chunk chunk = CreateChunk(coord);
                                chunk.coord = coord;
                                chunk.SetUp(Mat, GenerateColliders);
                                _existingChunks.Add(coord, chunk);
                                _chunks.Add(chunk);
                                UpdateChunkMesh(chunk);
                            }
                        }
                    }

                }
            }
        }
    }

    public bool IsVisibleFrom(Bounds bounds, Camera cam)
    {
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
        return GeometryUtility.TestPlanesAABB(planes, bounds);
    }

    public void UpdateChunkMesh(Chunk chunk)
    {
        int numVoxelsPerAxis = NumPointsPerAxis - 1;
        int numThreadsPerAxis = Mathf.CeilToInt(numVoxelsPerAxis / (float)ThreadGroupSize);
        float pointSpacing = BoundsSize / (NumPointsPerAxis - 1);

        Vector3Int coord = chunk.coord;
        Vector3 centre = CentreFromCoord(coord);

        Vector3 worldBounds = new Vector3(NumChunks.x, NumChunks.y, NumChunks.z) * BoundsSize;

        DensityGenerator.Generate(_pointsBuffer, NumPointsPerAxis, BoundsSize, worldBounds, centre, Offset, pointSpacing);

        _triangleBuffer.SetCounterValue(0);
        Shader.SetBuffer(0, "points", _pointsBuffer);
        Shader.SetBuffer(0, "triangles", _triangleBuffer);
        Shader.SetInt("numPointsPerAxis", NumPointsPerAxis);
        Shader.SetFloat("isoLevel", IsoLevel);

        Shader.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);

        // Get number of triangles in the triangle buffer
        ComputeBuffer.CopyCount(_triangleBuffer, _triCountBuffer, 0);
        int[] triCountArray = { 0 };
        _triCountBuffer.GetData(triCountArray);
        int numTris = triCountArray[0];

        // Get triangle data from shader
        Triangle[] tris = new Triangle[numTris];
        _triangleBuffer.GetData(tris, 0, 0, numTris);

        Mesh mesh = chunk.mesh;
        mesh.Clear();

        var vertices = new Vector3[numTris * 3];
        var meshTriangles = new int[numTris * 3];

        for (int i = 0; i < numTris; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                meshTriangles[i * 3 + j] = i * 3 + j;
                vertices[i * 3 + j] = tris[i][j];
            }
        }
        mesh.vertices = vertices;
        mesh.triangles = meshTriangles;

        mesh.RecalculateNormals();
    }

    public void UpdateAllChunks()
    {

        // Create mesh for each chunk
        foreach (Chunk chunk in _chunks)
        {
            UpdateChunkMesh(chunk);
        }

    }

    private void OnDestroy()
    {
        if (Application.isPlaying)
        {
            ReleaseBuffers();
        }
    }

    private void CreateBuffers()
    {
        int numPoints = NumPointsPerAxis * NumPointsPerAxis * NumPointsPerAxis;
        int numVoxelsPerAxis = NumPointsPerAxis - 1;
        int numVoxels = numVoxelsPerAxis * numVoxelsPerAxis * numVoxelsPerAxis;
        int maxTriangleCount = numVoxels * 5;

        // Always create buffers in editor (since buffers are released immediately to prevent memory leak)
        // Otherwise, only create if null or if size has changed
        if (!Application.isPlaying || (_pointsBuffer == null || numPoints != _pointsBuffer.count))
        {
            if (Application.isPlaying)
            {
                ReleaseBuffers();
            }
            _triangleBuffer = new ComputeBuffer(maxTriangleCount, sizeof(float) * 3 * 3, ComputeBufferType.Append);
            _pointsBuffer = new ComputeBuffer(numPoints, sizeof(float) * 4);
            _triCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);

        }
    }

    private void ReleaseBuffers()
    {
        if (_triangleBuffer != null)
        {
            _triangleBuffer.Release();
            _pointsBuffer.Release();
            _triCountBuffer.Release();
        }
    }

    private Vector3 CentreFromCoord(Vector3Int coord)
    {
        // Centre entire map at origin
        if (FixedMapSize)
        {
            Vector3 totalBounds = (Vector3)NumChunks * BoundsSize;
            return -totalBounds / 2 + (Vector3)coord * BoundsSize + Vector3.one * BoundsSize / 2;
        }

        return new Vector3(coord.x, coord.y, coord.z) * BoundsSize;
    }

    private void CreateChunkHolder()
    {
        // Create/find mesh holder object for organizing chunks under in the hierarchy
        if (_chunkHolder == null)
        {
            if (GameObject.Find(ChunkHolderName))
            {
                _chunkHolder = GameObject.Find(ChunkHolderName);
            }
            else
            {
                _chunkHolder = new GameObject(ChunkHolderName);
            }
        }
    }

    // Create/get references to all chunks
    private void InitChunks()
    {
        CreateChunkHolder();
        _chunks = new List<Chunk>();
        List<Chunk> oldChunks = new List<Chunk>(FindObjectsOfType<Chunk>());

        // Go through all coords and create a chunk there if one doesn't already exist
        for (int x = 0; x < NumChunks.x; x++)
        {
            for (int y = 0; y < NumChunks.y; y++)
            {
                for (int z = 0; z < NumChunks.z; z++)
                {
                    Vector3Int coord = new Vector3Int(x, y, z);
                    bool chunkAlreadyExists = false;

                    // If chunk already exists, add it to the chunks list, and remove from the old list.
                    for (int i = 0; i < oldChunks.Count; i++)
                    {
                        if (oldChunks[i].coord == coord)
                        {
                            _chunks.Add(oldChunks[i]);
                            oldChunks.RemoveAt(i);
                            chunkAlreadyExists = true;
                            break;
                        }
                    }

                    // Create new chunk
                    if (!chunkAlreadyExists)
                    {
                        var newChunk = CreateChunk(coord);
                        _chunks.Add(newChunk);
                    }

                    _chunks[_chunks.Count - 1].SetUp(Mat, GenerateColliders);
                }
            }
        }

        // Delete all unused chunks
        for (int i = 0; i < oldChunks.Count; i++)
        {
            oldChunks[i].DestroyOrDisable();
        }
    }

    Chunk CreateChunk(Vector3Int coord)
    {
        GameObject chunk = new GameObject($"Chunk ({coord.x}, {coord.y}, {coord.z})");
        chunk.transform.parent = _chunkHolder.transform;
        Chunk newChunk = chunk.AddComponent<Chunk>();
        newChunk.coord = coord;
        return newChunk;
    }

    private void OnValidate()
    {
        _settingsUpdated = true;
    }

    private struct Triangle
    {
#pragma warning disable 649 // disable unassigned variable warning
        private Vector3 _a;
        private Vector3 _b;
        private Vector3 _c;

        public Vector3 this[int i]
        {
            get
            {
                switch (i)
                {
                    case 0:
                        return _a;
                    case 1:
                        return _b;
                    default:
                        return _c;
                }
            }
        }
    }

    private void OnDrawGizmos()
    {
        if (ShowBoundsGizmo)
        {
            Gizmos.color = BoundsGizmoCol;

            List<Chunk> chunks = _chunks ?? new List<Chunk>(FindObjectsOfType<Chunk>());
            foreach (Chunk chunk in chunks)
            {
                //Bounds bounds = new Bounds (CentreFromCoord (chunk.coord), Vector3.one * BoundsSize);
                Gizmos.color = BoundsGizmoCol;
                Gizmos.DrawWireCube(CentreFromCoord(chunk.coord), Vector3.one * BoundsSize);
            }
        }
    }

}
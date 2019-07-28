using UnityEngine;

public class Chunk : MonoBehaviour
{
    public Vector3Int coord;

    [HideInInspector]
    public Mesh mesh;

    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private MeshCollider _meshCollider;
    private bool _generateCollider;

    public void DestroyOrDisable()
    {
        if (Application.isPlaying)
        {
            mesh.Clear();
            gameObject.SetActive(false);
        }
        else
        {
            DestroyImmediate(gameObject, false);
        }
    }

    // Add components/get references in case lost (references can be lost when working in the editor)
    public void SetUp(Material mat, bool generateCollider)
    {
        _generateCollider = generateCollider;

        _meshFilter = GetComponent<MeshFilter>();
        _meshRenderer = GetComponent<MeshRenderer>();
        _meshCollider = GetComponent<MeshCollider>();

        if (_meshFilter == null)
        {
            _meshFilter = gameObject.AddComponent<MeshFilter>();
        }

        if (_meshRenderer == null)
        {
            _meshRenderer = gameObject.AddComponent<MeshRenderer>();
        }

        if (_meshCollider == null && generateCollider)
        {
            _meshCollider = gameObject.AddComponent<MeshCollider>();
        }
        if (_meshCollider != null && !generateCollider)
        {
            DestroyImmediate(_meshCollider);
        }

        mesh = _meshFilter.sharedMesh;
        if (mesh == null)
        {
            mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _meshFilter.sharedMesh = mesh;
        }

        if (generateCollider)
        {
            if (_meshCollider.sharedMesh == null)
            {
                _meshCollider.sharedMesh = mesh;
            }
            // force update
            _meshCollider.enabled = false;
            _meshCollider.enabled = true;
        }

        _meshRenderer.material = mat;
    }
}
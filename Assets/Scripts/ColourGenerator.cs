using UnityEngine;

[ExecuteInEditMode]
public class ColourGenerator : MonoBehaviour
{
    public Material mat;
    public Gradient gradient;
    public float normalOffsetWeight;

    private Texture2D _texture;
    private const int TextureResolution = 50;

    private void Init()
    {
        if (_texture == null || _texture.width != TextureResolution)
        {
            _texture = new Texture2D(TextureResolution, 1, TextureFormat.RGBA32, false);
        }
    }

    private void Update()
    {
        Init();
        UpdateTexture();

        MeshGenerator m = FindObjectOfType<MeshGenerator>();

        float boundsY = m.BoundsSize * m.NumChunks.y;

        mat.SetFloat("_boundsY", boundsY);
        mat.SetFloat("_normalOffsetWeight", normalOffsetWeight);

        mat.SetTexture("_ramp", _texture);
    }

    private void UpdateTexture()
    {
        if (gradient != null)
        {
            Color[] colours = new Color[_texture.width];
            for (int i = 0; i < TextureResolution; i++)
            {
                Color gradientCol = gradient.Evaluate(i / (TextureResolution - 1f));
                colours[i] = gradientCol;
            }

            _texture.SetPixels(colours);
            _texture.Apply();
        }
    }
}
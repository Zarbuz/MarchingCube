using UnityEngine;

[ExecuteInEditMode]
public class SeaWorldColours : MonoBehaviour {

    public Material Mat;
    [Range (0, 1)]
    public float FogDstMultiplier = 1;

    public Vector4 ShaderParams;

    private MeshGenerator _meshGenerator;
    private Camera _cam;

    public Gradient Gradient;

    private Texture2D _texture;
    private const int TextureResolution = 50;

    private void Init () {
        if (_texture == null || _texture.width != TextureResolution) {
            _texture = new Texture2D (TextureResolution, 1, TextureFormat.RGBA32, false);
        }
    }

    private void Update () {
        Init ();
        UpdateTexture ();

        if (_meshGenerator == null) {
            _meshGenerator = FindObjectOfType<MeshGenerator> ();
        }
        if (_cam == null) {
            _cam = FindObjectOfType<Camera> ();
        }

        Mat.SetTexture ("ramp", _texture);
        Mat.SetVector("params",ShaderParams);

        RenderSettings.fogColor = _cam.backgroundColor;
        RenderSettings.fogEndDistance = _meshGenerator.ViewDistance * FogDstMultiplier;
    }

    private void UpdateTexture () {
        if (Gradient != null) {
            Color[] colours = new Color[_texture.width];
            for (int i = 0; i < TextureResolution; i++) {
                Color gradientCol = Gradient.Evaluate (i / (TextureResolution - 1f));
                colours[i] = gradientCol;
            }

            _texture.SetPixels (colours);
            _texture.Apply ();
        }
    }
}
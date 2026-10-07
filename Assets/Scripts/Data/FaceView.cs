using TMPro;
using UnityEngine;

public class FaceView : MonoBehaviour
{
    [SerializeField] private TMP_Text label;   // only the numeral prefab has one

    [Header("Highlight")]
    [SerializeField] private Color baseColor = Color.white;          // numeral: normal text color
    [SerializeField, ColorUsage(true, true)] private Color glowColor = new Color(1f, 0.6f, 0.2f) * 1.5f;
    [SerializeField] private int glowMaterialIndex = -1;            // pips: -1 = whole face, otherwise one material

    private MaterialPropertyBlock block;

    public void SetLabel(string text)
    {
        if (label != null) label.text = text;
    }

    public void SetHighlight(float intensity)
    {
        // Numeral face: tint the text
        if (label != null)
        {
            label.color = Color.Lerp(baseColor, glowColor, intensity);
            return;
        }

        // Pip face: emission
        MeshRenderer renderer = GetComponentInChildren<MeshRenderer>();
        if (renderer == null) return;
        if (block == null) block = new MaterialPropertyBlock();

        Color color = glowColor * intensity;   // 0 = off
        bool oneMaterial = glowMaterialIndex >= 0 && glowMaterialIndex < renderer.sharedMaterials.Length;
        if (oneMaterial)
        {
            renderer.GetPropertyBlock(block, glowMaterialIndex);
            block.SetColor("_EmissionColor", color);
            renderer.SetPropertyBlock(block, glowMaterialIndex);
        }
        else
        {
            renderer.GetPropertyBlock(block);
            block.SetColor("_EmissionColor", color);
            renderer.SetPropertyBlock(block);
        }
    }
}
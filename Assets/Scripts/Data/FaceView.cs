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
    private void Awake()
    {
        if (label != null) baseColor = label.color;   // the prefab's colour is the truth, no second copy to keep in sync
    }

    public void SetLabel(string text)
    {
        if (label != null) label.text = text;
    }

    public void SetHighlight(float intensity)
    {
        // Numeral face: the digit stays its own colour, a glowing rim grows around it
        if (label != null)
        {
            label.color = baseColor;
            label.outlineColor = glowColor;              // HDR gets clamped to 0..1 here, which is fine for a rim
            label.outlineWidth = 0.25f * intensity;      // 0 = no rim
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
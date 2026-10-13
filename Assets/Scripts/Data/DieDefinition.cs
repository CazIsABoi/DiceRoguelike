using UnityEngine;

public enum DieRole { Auto, Number, Operator }

[CreateAssetMenu(fileName = "DieDefinition", menuName = "Dice/DieDefinition")]
public class DieDefinition : ScriptableObject
{
    public string displayName;
    public FaceDefinition[] faceDefinitions = new FaceDefinition[6];
    [Tooltip("Auto: whichever kind most of its faces are. Set it on mixed dice: a 3/3 die can't be counted.")]
    public DieRole role = DieRole.Auto;
    [Tooltip("Boss dice: a short line for the pick screen, 30 characters or less. Empty = it lists the faces.")]
    public string line;
    public Die prefab;   // leave empty to use the default D6
    public Sprite icon;
}
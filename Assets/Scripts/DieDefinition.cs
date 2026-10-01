using UnityEngine;

[CreateAssetMenu(fileName = "DieDefinition", menuName = "Dice/DieDefinition")]
public class DieDefinition : ScriptableObject
{
    public string displayName;
    public FaceDefinition[] faceDefinitions = new FaceDefinition[6];
    public GameObject prefab;
}
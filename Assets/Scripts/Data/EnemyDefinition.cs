using UnityEditor.Overlays;
using UnityEngine;

[CreateAssetMenu(fileName = "EnemyDefinition", menuName = "Enemy/EnemyDefinition")]
public class EnemyDefinition : ScriptableObject
{
    public string displayName;
    public int maxHP;
    public DieDefinition[] dice;
    public SlotLayout layout;
}

using UnityEngine;

public enum EnemyBrain
{
    Impulsive,
    Greedy,
    Calculating
}

[CreateAssetMenu(fileName = "EnemyDefinition", menuName = "Enemy/EnemyDefinition")]
public class EnemyDefinition : ScriptableObject
{
    public string displayName;
    public int maxHP;
    public DieDefinition[] dice;
    public SlotLayout layout;
    public EnemyBrain brain;
    public int throwAttempts = 1;
}

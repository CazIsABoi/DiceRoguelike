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

    [Header("Personality")]
    public EnemyBrain brain;
    public int throwAttempts = 1;
    [Range(0f, 1f)] public float blunderChance = 0f;   // chance to skip thinking and just slap dice in
    public int rethrowBelow = 2;                       // Greedy: rethrows numbers under this
    public float minGain = 2f;                         // Calculating: how sure it must be before risking a rethrow
    public AudioClip voice;

    [Header("Table")]
    public TableType table = TableType.High;
    public Vector2Int targetRange = new Vector2Int(10, 40);   // Target tables only, both ends included

    [Header("Portrait")]
    public Sprite portrait;        // idle
    public Sprite portraitTalk;    // mouth open 

    [Header("Flavour")]
    [TextArea] public string intro;    // when it sits down
    public string[] winLines;          // it won a turn
    public string[] loseLines;         // it lost a turn
    public string[] bustLines;         // it busted
    public string defeatLine;          // knocked out

    [Header("Rewards")]
    public FaceDefinition[] unlockFaces;   // added to the reward pool when this enemy is beaten
    public DieDefinition dieDrop;          // offered as the special reward, act bosses only
}

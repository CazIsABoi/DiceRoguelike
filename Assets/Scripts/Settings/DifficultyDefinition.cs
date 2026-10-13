using UnityEngine;

// What a difficulty changes: the dice you start with, and how tough the enemies are.
// Make one per difficulty: right-click in the Project window > Create > Carry the One > Difficulty.
// The menu hands the chosen one to the game scene through RunSettings.
[CreateAssetMenu(menuName = "Carry the One/Difficulty", fileName = "Difficulty_")]
public class DifficultyDefinition : ScriptableObject
{
    public string displayName = "NORMAL";
    [TextArea(2, 4)] public string description = "THE GAME AS IT'S MEANT TO BE PLAYED";
    public string telemetryName = "Normal";        // what the telemetry sheet records

    [Tooltip("Your dice at the start of a run, left to right. Mixed dice need their Role set (Number or Operator).")]
    public DieDefinition[] startingDice;

    [Tooltip("Every enemy's Max HP is multiplied by this. 1 = as tuned.")]
    [Range(0.3f, 2f)] public float enemyHpMultiplier = 1f;

    [Tooltip("Custom runs only: work the sum left to right instead of × ÷ % first.")]
    public bool leftToRight;

    public Color color = Color.white;

    [Header("Random Mixed Faces")]
    [Tooltip("Every run, each face of the other kind (an operator on a number die, a number on the operator die) " +
             "is drawn again from the lists below. The starting dice above say which faces those are.")]
    public bool randomMixedFaces;

    [Tooltip("What an operator face on a number die can become. Leave × out.")]
    public FaceDefinition[] randomOperators;

    [Tooltip("What a number face on the operator die can become. Empty = those faces stay as they are.")]
    public FaceDefinition[] randomNumbers;

    [Tooltip("At least this many of the drawn operators are a + (Random Operators must hold the +).")]
    [Min(0)] public int minimumPlus = 1;

    [Tooltip("On: Minimum Plus counts on each die. Off: across all your dice together.")]
    public bool plusOnEachDie;

    [Header("Boss Dice")]
    [Tooltip("Optional: this difficulty's own boss dice. Empty = the pool on GameController.")]
    public BossDicePool bossDice;
}

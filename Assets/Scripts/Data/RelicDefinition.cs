using UnityEngine;

public enum RelicType { TimesTable, RabbitsFoot, PocketDie, Insurance, CarryTheOne }

// One asset per relic. The type decides what it does (PlayerController and GameController read it),
// the rest is what the relic screen shows.
[CreateAssetMenu(menuName = "Carry the One/Relic", fileName = "Relic_")]
public class RelicDefinition : ScriptableObject
{
    public RelicType type;
    public string displayName = "Times Table";
    public string line = "Your dice hold one more ×.";   // 30 characters or less, so it fits the dot screen
    public Sprite icon;
}
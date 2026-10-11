using System.Collections;
using UnityEngine;

// The base for enemy gimmicks (guide section 8). Each gimmick is a small class that inherits this, each tuned version
// of it is an asset, and an enemy gets a list of them (EnemyDefinition.traits). GameController calls these hooks at
// fixed moments in a round.
//
// HOW TO THINK
// - Open/closed: GameController gets the hook calls ONCE. After that a new gimmick is a new file, and nothing else in
//   the game changes. That's the whole point of the pattern.
// - A trait overrides only the hooks it cares about. The rest do nothing, which is why they're virtual, not abstract.
// - ASSETS ARE BLUEPRINTS, NOT INSTANCES. Every enemy with "Hot Hand" shares the SAME asset, and in the editor a
//   change to an asset's field survives leaving Play mode. So never keep per-fight state in a trait's fields:
//   keep it on the EnemyController (e.g. enemy.BonusRolls), which is reset every fight.
public abstract class EnemyTrait : ScriptableObject
{
    public string displayName = "Hot Hand";
    [TextArea] public string description = "Wins a round? It gets an extra roll next round.";

    // Once, when the fight starts (after the enemy is loaded)
    public virtual void OnFightStart(EnemyController enemy) { }

    // After the enemy has placed its dice, before your turn
    public virtual IEnumerator AfterEnemyTurn(EnemyController enemy) { yield break; }

    // After both equations are done, before any damage
    public virtual IEnumerator BeforeDamage(EnemyController enemy) { yield break; }

    // After the damage. enemyWon: the enemy won this round
    public virtual void OnRoundEnd(EnemyController enemy, bool enemyWon) { }
}
using UnityEngine;

// "Hot Hand": every round it wins, it gets one more roll per die next round (up to maxBonus). Losing resets it.
// (guide section 8)
[CreateAssetMenu(menuName = "Carry the One/Traits/Hot Hand", fileName = "Trait_HotHand")]
public class HotHandTrait : EnemyTrait
{
    [SerializeField] private int maxBonus = 2;
    [SerializeField] private string[] heatingUpLines = { "I'm on fire.", "Feeling lucky.", "Again. Faster." };
    [SerializeField] private string[] cooledLines = { "...Cold dice.", "Hm." };

    public override void OnFightStart(EnemyController enemy)
    {
        enemy.BonusRolls = 0;   // the bonus lives on the enemy, never on this asset (assets are shared blueprints)
    }

    public override void OnRoundEnd(EnemyController enemy, bool enemyWon)
    {
        int before = enemy.BonusRolls;
        enemy.BonusRolls = enemyWon ? Mathf.Min(before + 1, maxBonus) : 0;

        // A gimmick nobody notices doesn't exist: say it out loud whenever the bonus changes
        if (enemy.BonusRolls > before) enemy.Announce(Pick(heatingUpLines));
        else if (enemy.BonusRolls < before) enemy.Announce(Pick(cooledLines));
    }

    private static string Pick(string[] lines) =>
        lines == null || lines.Length == 0 ? "" : lines[Random.Range(0, lines.Length)];
}
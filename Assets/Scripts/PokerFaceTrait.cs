using System.Collections;
using UnityEngine;

// "Poker Face": after placing its dice, it hides its biggest number under a dice cup. You throw without knowing
// exactly what you have to beat, and the cup lifts before the damage (guide section 8).
//
// Hiding is VISUAL ONLY: the die and its face never change, so the result is computed exactly the same way.
[CreateAssetMenu(menuName = "Carry the One/Traits/Poker Face", fileName = "Trait_PokerFace")]
public class PokerFaceTrait : EnemyTrait
{
    [SerializeField] private GameObject cupPrefab;   // an upside-down cup, pivot at its open rim, big enough to cover a die
    [SerializeField] private float dropHeight = 3f;  // where the cup starts above the die (your dice are 2 units)
    [SerializeField] private float dropTime = 0.12f;
    [SerializeField] private float liftTime = 0.3f;
    [SerializeField] private float revealPause = 0.6f;
    [SerializeField] private string[] hideLines = { "Guess.", "You'll see.", "Read my face." };

    public override IEnumerator AfterEnemyTurn(EnemyController enemy)
    {
        // 1. The meanest slot to hide: the one holding the biggest number
        int hide = -1;
        int biggest = int.MinValue;
        for (int i = 0; i < enemy.SlotCount; i++)
        {
            DiceSlot slot = enemy.GetSlot(i);
            if (slot.IsEmpty) continue;
            FaceDefinition face = slot.CurrentDie.GetTopFace();
            if (face == null || face.type != FaceType.Number) continue;
            if (face.number > biggest) { biggest = face.number; hide = i; }
        }
        if (hide < 0) yield break;   // nothing to hide (it busted everything)

        // 2. The equation prints "?" for that slot from now on
        enemy.HideSlot(hide);
        enemy.Announce(hideLines.Length > 0 ? hideLines[Random.Range(0, hideLines.Length)] : "");

        // 3. The cup slams down over the die
        if (cupPrefab == null) yield break;
        Transform die = enemy.GetSlot(hide).CurrentDie.transform;
        Bounds b = die.GetComponent<Collider>().bounds;
        Vector3 rest = new Vector3(b.center.x, b.min.y, b.center.z);   // the rim sits where the die meets the slot
        Vector3 high = rest + Vector3.up * dropHeight;

        GameObject cup = Instantiate(cupPrefab, high, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        enemy.Cup = cup;   // per-round state lives on the enemy, not here

        for (float t = 0f; t < dropTime; t += Time.deltaTime)
        {
            float k = t / dropTime;
            cup.transform.position = Vector3.Lerp(high, rest, k * k);   // ease IN: the slam
            yield return null;
        }
        cup.transform.position = rest;

        ParticleSystem dust = enemy.LandDust;
        if (dust != null) { dust.transform.position = rest; dust.Emit(15); }
    }

    public override IEnumerator BeforeDamage(EnemyController enemy)
    {
        if (enemy.HiddenSlot < 0) yield break;   // it didn't hide anything this round

        GameObject cup = enemy.Cup;
        if (cup != null)
        {
            Vector3 from = cup.transform.position;
            Vector3 to = from + Vector3.up * dropHeight;
            for (float t = 0f; t < liftTime; t += Time.deltaTime)
            {
                float k = t / liftTime;
                k = 1f - (1f - k) * (1f - k);   // ease OUT: whipped off, then floats away
                cup.transform.position = Vector3.Lerp(from, to, k);
                yield return null;
            }
            Destroy(cup);
            enemy.Cup = null;
        }

        enemy.RevealSlots();
        yield return new WaitForSeconds(revealPause);   // let the player see the number before the count
    }
}
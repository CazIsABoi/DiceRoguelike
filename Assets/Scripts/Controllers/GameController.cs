using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static UnityEngine.Rendering.RayTracingAccelerationStructure;

public class GameController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerController player;
    [SerializeField] private EnemyController enemy;
    [SerializeField] private RewardController reward;
    [SerializeField] private AudioSource audio;

    private int? playerResult;
    private int? enemyResult;
    private int playerBust;
    private int enemyBust;

    [Header("Maths")]
    [SerializeField] private bool schoolOrder = true;   // × ÷ % before + −. Untick for the old left-to-right rule
    public static bool SchoolOrder = true;

    [Header("Score UI")]
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private Gradient scoreGradient;
    [SerializeField] private Color loseColor = new Color(1f, 0.25f, 0.2f);  

    [SerializeField] private StripDisplay strip;
    [SerializeField] private float maxLog = 3f;
    [SerializeField] private float maxShake = 15f;
    [SerializeField] private CanvasGroup equationsGroup;   // the parent holding both equation texts
    [SerializeField] private float swapTime = 0.25f;

    [Header("Bust")]
    [SerializeField] private int bustBase = 3;
    public int Fight { get; private set; } = 1;
    public int OperatorBustPenalty => (bustBase + RunSettings.RaiseBustExtra) * Fight * player.Stake;   // the stake grows with your number dice, like your HP

    [Header("Acts")]
    [SerializeField] private int[] actStarts = { 1, 9, 17 };   // first fight of each act: 8 + 8 + 5 fights
    public int Act { get; private set; } = 1;

    [Header("Audio")]
    [SerializeField] private AudioClip winClip;
    [SerializeField] private AudioClip turnWinClip;
    [SerializeField] private AudioClip loseClip;

    [Header("Run")]
    [SerializeField] private EnemyDefinition[] ladder;      // the fights in order. With Slots filled in, it's rolled from them every run
    [SerializeField] private LadderSlot[] slots;            // enemy pools: one per fight, each run picks one enemy from each
    public EnemyDefinition[] Ladder => ladder;              // this run's enemies (already rolled by the time anything asks)
    public int[] ActStarts => actStarts;
    public event System.Action<int> OnFightStarted;   
    private EnemyTrait[] currentTraits = new EnemyTrait[0];
    private EnemyTrait[] Traits => currentTraits;           // this enemy's traits, plus any a Raise level adds
    [SerializeField] private float introTime = 2f;
    [SerializeField] private float actBannerTime = 2.5f;

    [Header("Round Flow")]
    [SerializeField] private float timeBetweenRounds = 2f;
    [SerializeField] FaceDefinition[] rewardPool;
    private List<FaceDefinition> facePool;
    [SerializeField] private BossDicePool bossDice;
    [SerializeField, Range(0, 100)] private int healPercent = 40;   // the heal reward gives this much of your max HP

    [Header("Turn Order")]
    [SerializeField] private Transform tableView;
    [SerializeField] private Transform enemyView;          // where the camera stands to watch the enemy
    [SerializeField] private CameraController cameraController;
    [SerializeField] private float readTime = 0.6f;       // to read its equation before the camera comes back

    [Header("Tables")]
    [SerializeField] private int lowCap = 20;                 // at a Low table a distance counts this much at most (x the stake)
    [SerializeField] private TMP_Text tableText;              // "TARGET 437" / "CLOSEST TO 0" for the whole round. Empty: the strip shows it
    [SerializeField] private Color tableColor = new Color32(0xFF, 0x71, 0x34, 0xFF);
    [SerializeField] private float tableBannerTime = 2.5f;
    [SerializeField] private ScreenBanner screenBanner;       // full-screen flashing banner for the table type and new acts. Empty: the monitor shows it
    public TableType Table { get; private set; } = TableType.High;
    public int Target { get; private set; }
    private Vector2Int targetRange;

    [Header("Relics")]
    [SerializeField] private RelicDefinition[] relicPool;     // the five relics. Empty = no relic screens! (right-click this component > Find Relic Assets)
    [SerializeField] private int[] relicFights = { 4, 8, 12, 16 }; // a relic screen follows the reward screen after these fights (the sim: with 11 relics, 4 screens keep Normal where 3 screens and 5 relics had it)
    [SerializeField] private RectTransform relicBar;          // an empty UI object on your HUD canvas: the icons are built inside it
    [SerializeField] private float relicIconSize = 48f;
    [SerializeField] private Color relicColor = new Color32(0xFF, 0x71, 0x34, 0xFF);
    [SerializeField] private TMP_Text relicTooltip;           // optional: hover an icon and this says what the relic does
    [SerializeField] private AudioClip relicClip;             // optional: plays when a relic is picked or does something
    [SerializeField] private Image[] relicSlots;              // or build the icons yourself and drag them in here (then Relic Bar is ignored)
    [SerializeField, Range(0f, 1f)] private float spentRelicAlpha = 0.3f;   // Insurance used, Rabbit's Foot spent this fight
    private readonly List<RelicDefinition> heldRelics = new List<RelicDefinition>();
    private RelicDefinition hoveredRelic;
    private bool warnedTableText;
    private int carry;                                        // Carry the One: damage past the last enemy's HP
    private int oppositeDayAct;

    [Header("More Relics")]
    [SerializeField] private int primeMultiplier = 2;         // Prime Time: a prime result at a High table deals this many times the damage
    [SerializeField] private int bullseyeMultiplier = 3;      // Bullseye: an exact hit (the target, 0 at a Low table, the limit itself) deals this many times the damage
    [SerializeField] private int luckySevenBonus = 7;         // Lucky Seven: at High tables each 7 you play is worth 7 more, before the maths: 7 × 3 is 14 × 3
    public int LuckySevenValue => 7 + luckySevenBonus;        // what a 7 counts as (a 7 in the tens is 140 instead of 70: the same +7 in its place)
    [SerializeField, Range(0, 100)] private int evenStevenHeal = 3;   // Even Steven: an even result heals this % of your max HP
    [SerializeField] private bool evenStevenOnlyWins = true;          // Even Steven: only rounds you win. The sim: 5% on every round took Normal from 21% to 42%, this is about 28%
    [SerializeField] private float relicFlashTime = 1.2f;
    private bool doubleDownArmed;
    private int doubleDownFight;                              // the fight Double Down was used in (0 = not yet)

    [Header("End Screen")]
    [SerializeField] private GameObject endPanel;
    [SerializeField] private TMP_Text endTitle;   // "THANKS FOR PLAYING" / "GAME OVER"
    [SerializeField] private CameraRig rig;
    [SerializeField] private Transform monitorView;
    [SerializeField] private GameObject equationPanel;   // hide it, like the reward screen does
    [SerializeField] private TMP_Text endStats;

    [Header("Juice")]
    [SerializeField] private NixieCounter scoreCounter;   // on the score text
    [SerializeField] private ParticleSystem scoreSparks;   // the Sphere-shaped copy
    [SerializeField] private float sparkForward = 0.3f;   // how far in front of the score text they burst

    [Header("Reveal")]
    [SerializeField] private float monitorHold = 1.2f;   // how long the camera lingers on the result before zooming out
    [SerializeField, Range(0f, 1f)] private float rollThreshold = 0.5f;   // wins at least this big get the nixie roll
    [SerializeField] private NixieHealth playerHealthTubes;
    [SerializeField] private NixieHealth enemyHealthTubes;

    [Header("Counting")]
    [SerializeField] private CountShow counter;                                   // empty: found by itself
    [SerializeField] private AudioClip knockoutClip;                              
    [SerializeField, Range(0f, 0.1f)] private float photoFinishShare = 0.02f;     // a gap this small a share of the bigger number...
    [SerializeField] private int photoFinishMin = 50;                             // ...when that number is at least this big...
    [SerializeField, Range(0.1f, 1f)] private float photoFinishSpeed = 0.4f;      // ...counts the margin this slowly
    public CountShow Counter => counter;
    public int LowCap => lowCap + RunSettings.RaiseLowCapExtra;   // Raise (Low Blow) adds to it


    private const string formUrl = "https://docs.google.com/forms/d/e/1FAIpQLScix26Ts907fGNdApSgtKu10-K0fdspGZps9k1Yjl9vXGlMGg/viewform";
    private const string resultEntry = "entry.2115005332";
    private const string fightEntry = "entry.1084623422";
    private const string beatenEntry = "entry.1538550322";
    private const string minutesEntry = "entry.522183546";
    private const string diceEntry = "entry.1966468218";
    private const string versionEntry = "entry.173446048";
    private const string runEntry = "entry.1005792611";   // your new Run ID question

    private int score;
    private bool roundEnding;
    private bool runWon;
    private Coroutine scorePunchRoutine;
    private Coroutine scoreFadeRoutine;

    public int Score => score;

    // 0..1: how big this result is. Every effect reads this, so one knob tunes them all.
    private float Intensity(int diff)
    {
        if (diff == 0) return 0f;
        return Mathf.InverseLerp(0f, maxLog, Mathf.Log10((Mathf.Abs(diff) + 1f) / player.Stake));
    }

    private void Awake()
    {
        ladder = RollLadder();   // in Awake, so the ladder board sees this run's enemies when it builds itself
        if (counter == null) counter = FindAnyObjectByType<CountShow>();
        SchoolOrder = schoolOrder && !RunSettings.LeftToRight;
    }
    private void Start()
    {
#if !UNITY_WEBGL
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 144;
#endif
        scoreText.text = "0";
        facePool = new List<FaceDefinition>(rewardPool);
        CheckRelicSetup();
        BuildRelicBar();
        player.OnRelicsChanged += UpdateRelicBar;
        player.OnRelicTriggered += PulseRelic;
        UpdateRelicBar();
        Telemetry.StartRun();
        StartCoroutine(BeginFight());
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.dKey.wasPressedThisFrame) TryDoubleDown();
    }

    // Double Down: once a fight, before your first throw of a round. That round's damage counts twice, either way.
    public void TryDoubleDown()
    {
        if (!player.HasRelic(RelicType.DoubleDown) || SettingsMenu.IsOpen || doubleDownArmed) return;
        if (doubleDownFight == Fight) { Tutorial.Notice("DOUBLE DOWN IS USED UP THIS FIGHT"); return; }
        if (player.InputBlocked) return;
        if (!player.NoDiceThrownYet) { Tutorial.Notice("DOUBLE DOWN BEFORE YOUR FIRST THROW"); return; }

        doubleDownArmed = true;
        doubleDownFight = Fight;
        Tutorial.Done("doubledown");
        player.TriggerRelic(RelicType.DoubleDown);
        UpdateRelicBar();
        strip.Flash("DOUBLE DOWN · THIS ROUND COUNTS TWICE", Color.yellow, 2f);
    }

    // ---------- Round flow ----------

    // Called by DiceSide when every slot is filled (or busted) and the last die has landed.
    // result is the plain equation, bust what the busted dice cost, kept apart because the table decides what a bust does.
    public void OnEquationComplete(DiceSide side, int result, int bust)
    {
        if (side == player) { playerResult = result; playerBust = bust; }
        else { enemyResult = result; enemyBust = bust; }

        if (playerResult.HasValue && enemyResult.HasValue && !roundEnding)
        {
            StartCoroutine(EndRoundRoutine());
        }
    }

    private IEnumerator BeginFight()
    {
        player.InputBlocked = true;
        cameraController.SetLook(false);
        SetTableText("");

        if (Act < actStarts.Length && Fight == actStarts[Act])   // first fight of a new act
        {
            Act++;
            if (screenBanner != null) yield return screenBanner.Play($"ACT {Act}", "", Color.yellow, actBannerTime);
            else
            {
                strip.Flash($"ACT {Act}", Color.yellow, actBannerTime);
                yield return new WaitForSeconds(actBannerTime);
            }
        }

        EnemyDefinition def = ladder[Fight - 1];   // Fight starts at 1
        Table = def.table;
        targetRange = def.targetRange;
        currentTraits = RunSettings.TraitsFor(def.traits, Act);
        bool boss = IsBossFight(def);
        enemy.ExtraThrows = boss ? RunSettings.RaiseBossThrows : 0;   // Raise (Boss Throws, not in the default table)
        enemy.HpMultiplier = boss ? RunSettings.RaiseBossHp : 1f;     // Raise (Pit Watch)
        enemy.Load(def);
        OnFightStarted?.Invoke(Fight);
        if (Traits != null) foreach (EnemyTrait t in Traits) if (t != null) t.OnFightStart(enemy);
        player.ClearTotal();
        player.OnFightStart();
        enemy.SayIntro();
        strip.Flash($"VS {enemy.DisplayName}", Color.red, 3f);
        yield return new WaitForSeconds(introTime);
        scoreText.text = "";
        if (Traits != null && Traits.Length > 0)
        {
            List<string> names = new List<string>();
            foreach (EnemyTrait t in Traits) if (t != null) names.Add(t.displayName.ToUpper());
            strip.Flash(string.Join(" · ", names), tableColor, 2.5f);
            yield return new WaitForSeconds(2f);
        }

        if (Table != TableType.High)
        {
            yield return TableBannerOnScreen();
            yield return reward.SwapRoutine(player, Table);   // the spare at Low tables, the Pocket Die at both
            Tutorial.Done("lowtable");
        }

        int carried = 0;
        if (carry > 0)   // Carry the One: last fight's overkill hits this enemy, but never finishes it
        {
            int dealt = Mathf.Min(carry, enemy.Health - 1);
            carry = 0;
            if (dealt > 0)
            {
                enemy.TakeDamage(dealt);
                carried = dealt;
                player.TriggerRelic(RelicType.CarryTheOne);
                strip.Flash($"CARRY THE ONE · {dealt}", tableColor, 2f);
                yield return new WaitForSeconds(1.5f);
            }
        }

        Telemetry.StartFight(def, player, enemy, Act, carried);

        if (Fight == 1 && screenBanner != null)   // the goal, once, before the very first throw
            yield return screenBanner.Play("BEAT THEIR NUMBER", "THE BIGGER NUMBER WINS THE ROUND", Color.yellow, actBannerTime);

        yield return RoundRoutine();
    }

    private IEnumerator RoundRoutine()
    {
        scoreText.text = "";
        if (Table == TableType.Target || Table == TableType.Limit)
        {
            Target = Random.Range(targetRange.x, targetRange.y + 1);
            Telemetry.SetTarget(Target);
            SetTableText(Table == TableType.Limit ? $"LIMIT {Target}" : $"TARGET {Target}");
            if (tableText == null) ShowTargetInScore();
        }
        else if (Table == TableType.Low) SetTableText("CLOSEST TO 0");
        else SetTableText("");


        player.InputBlocked = true;
        cameraController.SetLook(false);
        yield return rig.MoveTo(enemyView);
        yield return enemy.TakeTurn();   // the enemy's AI throws its dice, then calls OnEquationComplete
        WaitUntil wait = new WaitUntil(() => enemyResult.HasValue);
        yield return wait;
        if (Traits != null) foreach (EnemyTrait t in Traits) if (t != null) yield return t.AfterEnemyTurn(enemy);
        yield return new WaitForSeconds(readTime);   
        yield return rig.MoveTo(tableView);         
        cameraController.SetLook(true);
        player.InputBlocked = false;
        if (Fight == 1) Tutorial.Hint("throw", ThrowStyle.ThrowHint);
        else if (player.HasRelic(RelicType.DoubleDown) && doubleDownFight != Fight)
            Tutorial.Hint("doubledown", "DOUBLE DOWN: PRESS D BEFORE YOU THROW");
        yield break;
    }

    private IEnumerator EndRoundRoutine()
    {
        roundEnding = true;
        player.InputBlocked = true;   // until the next round starts: no grabbing dice during the reward or relic screens

        // The table turns both results into scores (bigger is better), the difference is the damage
        int playerScore = TableScore(playerResult.Value, playerBust);
        int enemyScore = TableScore(enemyResult.Value, enemyBust);

        if (Traits != null) foreach (EnemyTrait t in Traits) if (t != null) yield return t.BeforeDamage(enemy);

        yield return cameraController.FocusOn(monitorView, 0.45f);
        if (counter != null) counter.NewRound();   // the pitch starts at the bottom again
        yield return enemy.PlayCount(() => cameraController.Kick());

        // Lucky Seven: your 7s are already worth more in the result (PlayerController.SevenValue). Say so before your
        // count, so the 14s in it make sense.
        if (player.SevenValue != 7 && player.CountPlaced(7) > 0)
        {
            player.TriggerRelic(RelicType.LuckySeven);
            strip.Flash($"LUCKY SEVEN · EVERY 7 IS A {player.SevenValue}", Color.yellow, relicFlashTime);
            yield return new WaitForSeconds(relicFlashTime * 0.6f);
        }
        yield return player.PlayCount(() => cameraController.Kick(4f));

        int diff = playerScore - enemyScore;
        Telemetry.RoundScored(playerScore, enemyScore);
        List<CountStep> margin = MarginSteps(playerScore, enemyScore);

        // Relics that multiply the damage: shown as one more "× 2" step at the end of the count
        int multiplier = 1;
        List<string> why = new List<string>();
        if (doubleDownArmed)
        {
            doubleDownArmed = false;
            multiplier *= 2;
            why.Add("DOUBLE DOWN ×2");
        }
        if (diff > 0 && Table == TableType.High && player.HasRelic(RelicType.PrimeTime) && IsPrime(playerScore))
        {
            multiplier *= primeMultiplier;
            why.Add($"{playerScore} IS PRIME ×{primeMultiplier}");
            player.TriggerRelic(RelicType.PrimeTime);
        }
        if (diff > 0 && player.HasRelic(RelicType.Bullseye) && IsBullseye(playerScore))
        {
            multiplier *= bullseyeMultiplier;
            why.Add($"BULLSEYE ×{bullseyeMultiplier}");
            player.TriggerRelic(RelicType.Bullseye);
        }
        if (multiplier > 1 && diff != 0)
        {
            diff *= multiplier;
            MultiplyMargin(margin, multiplier);
            strip.Flash(string.Join(" · ", why), Color.yellow, relicFlashTime);
            yield return new WaitForSeconds(relicFlashTime);
        }
        score = diff;

        yield return SwapMonitor(true);
        yield return RevealResult(diff, margin);          // the show, zoomed in

        enemy.ClearTotal();
        player.ClearTotal();
        yield return SwapMonitor(false);                  // equations back, while still close enough to read them
        yield return cameraController.FocusBack(0.5f);    // zoom out: now the HP tubes are in view
                                                          // Opposite Day: once per act, the first round you lose flips into a win by the same margin
        if (diff < 0 && player.HasRelic(RelicType.OppositeDay) && oppositeDayAct != Act)
        {
            oppositeDayAct = Act;
            diff = -diff;
            score = diff;
            player.TriggerRelic(RelicType.OppositeDay);
            UpdateRelicBar();
            strip.Flash("OPPOSITE DAY! THAT LOSS IS A WIN", Color.yellow, 1.5f);
            yield return new WaitForSeconds(1.5f);
        }

        bool insured = false;
        yield return Drain(diff, () =>
        {
            if (diff < 0)
            {
                player.TakeDamage(-diff);
                insured = player.TryInsurance();   // Insurance: the first knockout leaves you on 25%
            }
            else if (diff > 0)
            {
                int hpBefore = enemy.Health;
                enemy.TakeDamage(diff);
                RunDamageCounter.Add(diff);
                if (enemy.IsDead && player.HasRelic(RelicType.CarryTheOne)) carry = diff - hpBefore;
            }
        });

        // Even Steven: an even result heals you a little (only on a win, unless Even Steven Only Wins is off)
        if (player.HasRelic(RelicType.EvenSteven) && !player.IsDead && playerResult.Value % 2 == 0 && (!evenStevenOnlyWins || diff > 0))
        {
            int heal = Mathf.Max(1, player.MaxHealth * evenStevenHeal / 100);
            if (player.Health < player.MaxHealth)
            {
                player.Heal(heal);
                player.TriggerRelic(RelicType.EvenSteven);
            }
        }

        if (Traits != null) foreach (EnemyTrait t in Traits) if (t != null) t.OnRoundEnd(enemy, diff < 0);
        if (diff > 0) strip.Flash("YOU WIN", Color.green, timeBetweenRounds);
        else if (insured) strip.Flash("INSURANCE PAID OUT!", Color.yellow, timeBetweenRounds);
        else if (diff < 0) strip.Flash("YOU LOST", Color.red, timeBetweenRounds);
        else strip.Flash("DRAW", Color.white, timeBetweenRounds);

        if (player.IsDead)
        {
            player.RevertSwaps();                // so the survey reports your own dice, not the spare
            yield return EndRun(false);
            yield break;
        }

        if (enemy.IsDead)
        {
            Telemetry.EndFight(true);
            player.RevertSwaps();
            SetTableText("");

            if (Fight == ladder.Length)          // beat the last enemy: no reward screen
            {
                yield return EndRun(true);
                yield break;
            }

            EnemyDefinition beaten = ladder[Fight - 1];
            facePool.AddRange(beaten.unlockFaces);

            if (beaten.dieDrop != null)          // act bosses: a new die. One more digit, one more zero on your HP
            {
                BossDicePool pool = RunSettings.BossDice(bossDice);
                List<DieDefinition> choices = pool != null ? pool.Roll() : new List<DieDefinition>();
                if (choices.Count > 1) yield return reward.BossDieRoutine(player, choices);
                else player.AddDie(choices.Count == 1 ? choices[0] : beaten.dieDrop);

                player.Heal((player.MaxHealth - player.Health) * RunSettings.RaiseBossHealPercent / 100);   // a full heal for the next act (Raise: Last Call halves it)
                strip.Flash($"NEW DIE · MAX HP {player.MaxHealth}", Color.yellow, 2.5f);
                yield return new WaitForSeconds(2.5f);
            }

            int heal = Mathf.RoundToInt(player.MaxHealth * healPercent * RunSettings.RaiseHealMultiplier / 100f);   // Raise: No Comps
            List<RelicDefinition> relicChoices = System.Array.IndexOf(relicFights, Fight) >= 0 ? RollRelics(RunSettings.RelicChoices(3)) : null;
            yield return reward.RewardRoutine(player, facePool.ToArray(), beaten.unlockFaces, heal, relicChoices);
            Fight++;

            player.ResetRound();
            playerResult = null;
            enemyResult = null;
            roundEnding = false;
            yield return BeginFight();   // next enemy (or a new act), intro, then it throws
            yield break;
        }

        player.ResetRound();
        enemy.ResetRound();
        playerResult = null;
        enemyResult = null;
        roundEnding = false;
        yield return RoundRoutine();
    }

    // The score is the biggest, clearest text on the monitor. At a Target table the last round's damage matters less
    // than what you're aiming for, so it shows the target while you throw (the damage still flashes at round end).
    private void ShowTargetInScore()
    {
        if (scoreFadeRoutine != null) StopCoroutine(scoreFadeRoutine);
        scoreText.color = tableColor;
        scoreText.text = Table == TableType.Limit ? $"LIMIT {Target}" : $"TARGET {Target}";
        if (scorePunchRoutine != null) StopCoroutine(scorePunchRoutine);
        scorePunchRoutine = StartCoroutine(Punch(scoreText.transform, 1.3f, 0.25f, 0f));
    }

    // The table type flashes on the full-screen banner (or, without one, big on the monitor's score text)
    private IEnumerator TableBannerOnScreen()
    {
        string title, rule;
        switch (Table)
        {
            case TableType.Low: title = "LOW TABLE"; rule = "CLOSEST TO 0 WINS"; break;
            case TableType.Limit: title = "LIMIT TABLE"; rule = $"DON'T GO OVER · {targetRange.x} TO {targetRange.y}"; break;
            default: title = "TARGET TABLE"; rule = $"TARGET {targetRange.x} TO {targetRange.y}"; break;
        }
        if (screenBanner != null)                       // the full-screen banner when there is one...
        {
            if (audio != null && turnWinClip != null) audio.PlayOneShot(turnWinClip, 0.6f);
            yield return screenBanner.Play(title, rule, tableColor, tableBannerTime);
            yield break;                              
        }

        yield return SwapMonitor(true);
        if (scoreFadeRoutine != null) StopCoroutine(scoreFadeRoutine);
        scoreText.text = $"{title}\n<size=50%>{rule}</size>";
        if (scorePunchRoutine != null) StopCoroutine(scorePunchRoutine);
        scorePunchRoutine = StartCoroutine(Punch(scoreText.transform, 1.4f, 0.3f, 0f));
        if (audio != null && turnWinClip != null) audio.PlayOneShot(turnWinClip, 0.6f);

        // Blinks a few times like a sign switching on, then holds
        const int blinks = 3;
        const float blinkTime = 0.12f;
        for (int i = 0; i < blinks; i++)
        {
            scoreText.color = tableColor;
            yield return new WaitForSeconds(blinkTime);
            scoreText.color = Color.clear;
            yield return new WaitForSeconds(blinkTime);
        }
        scoreText.color = tableColor;
        yield return new WaitForSeconds(Mathf.Max(0.5f, tableBannerTime - blinks * blinkTime * 2f));
        scoreText.text = "";
        scoreText.color = Color.white;
        yield return SwapMonitor(false);                // equations back
    }

    // Use a Table Text: the strip is shared with hints and flashes, so a target shown there gets wiped mid-round
    private void SetTableText(string text)
    {
        if (tableText != null) { tableText.text = text; return; }

        if (!warnedTableText && !string.IsNullOrEmpty(text))
        {
            warnedTableText = true;
            Debug.LogWarning("[Carry the One] GameController > Table Text is empty, so the target goes on the strip, " +
                             "where hints and flashes wipe it. Add a big text on the monitor and drag it in.");
        }
        if (string.IsNullOrEmpty(text)) strip.Clear();
        else strip.Show(text, tableColor);
    }



    private IEnumerator EndRun(bool won)
    {
        Telemetry.EndRun(won);
        int opened = won ? RunSettings.RecordClear() : 0;   // the next Raise level, if this clear opened one
        equationPanel.SetActive(false);
        endPanel.SetActive(true);
        string cleared = RunSettings.Raise > 0 ? $"RAISE {RunSettings.Raise} CLEARED" : $"ALL {ladder.Length} FIGHTS CLEARED";
        endStats.text = won ? cleared + (opened > 0 ? $" · RAISE {opened} OPEN" : "")
                            : $"ACT {Act} · FIGHT {Fight} · BEATEN BY {ladder[Fight - 1].displayName.ToUpper()}";
        yield return rig.MoveTo(monitorView);

        runWon = won;
        endTitle.text = won ? "THANKS FOR PLAYING" : "GAME OVER";
        scoreText.text = won ? "YOU WIN" : "GAME OVER";
        strip.Show(won ? "THANKS FOR PLAYING" : "BETTER LUCK NEXT TIME", won ? Color.green : Color.red);

        Camera.main.GetComponent<CameraController>().enabled = false;
        foreach (Die die in player.GetDice()) die.GetComponent<DiceController>().ToggleInputs(false);
        yield break;   // the buttons take it from here
    }

    public void OpenSurvey() => Application.OpenURL(BuildSurveyUrl());
    public void PlayAgain() => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    public void ToMenu() { Telemetry.AbandonRun(); SceneManager.LoadScene("MainMenu"); }

    private string BuildSurveyUrl()
    {
        string beatenBy = runWon ? "Nobody" : ladder[Fight - 1].displayName;
        int minutes = Mathf.RoundToInt(Time.timeSinceLevelLoad / 60f);

        string baseUrl = formUrl.Split('?')[0];   // drops "?usp=dialog" or anything else after viewform

        string url = baseUrl + "?usp=pp_url"
            + Field(resultEntry, runWon ? "Won" : "Lost")
            + Field(fightEntry, Fight.ToString())
            + Field(beatenEntry, beatenBy)
            + Field(minutesEntry, minutes.ToString())
            + Field(diceEntry, DescribeDice())
            + Field(versionEntry, Application.version)
            + Field(runEntry, Telemetry.LastRunId);

        Debug.Log("Survey URL: " + url);
        return url;
    }

    private string Field(string entry, string value) => "&" + entry + "=" + UnityWebRequest.EscapeURL(value);

    // e.g. "[1 2 3 4 5 9] [+ + + × × ÷] [1 2 3 4 7 6]"
    private string DescribeDice()
    {
        string text = "";
        foreach (Die die in player.GetDice())
        {
            text += "[";
            foreach (FaceDefinition f in die.GetCurrentFaces())
                text += (f.type == FaceType.Number ? f.number.ToString() : OpSymbol(f.op)) + " ";
            text = text.TrimEnd() + "] ";
        }
        foreach (RelicType r in player.Relics) text += r + " ";
        return text.TrimEnd();
    }

    // ---------- Maths (static: anyone can use these, player or enemy) ----------
    public class CountStep
    {
        public List<int> numbers;
        public List<Operator> ops;
        public int next;
    }

    // sevenValue: what a 7 counts as. 7, except on your side with Lucky Seven (14): 7 × 3 = 42, and 7 5 = 145
    public static int Evaluate(List<FaceDefinition> faces, int sevenValue = 7) => Count(faces, null, sevenValue);
    public static List<CountStep> CountSteps(List<FaceDefinition> faces, int sevenValue = 7)
    {
        List<CountStep> steps = new List<CountStep>();
        Count(faces, steps, sevenValue);
        return steps;
    }

    // The ONE place maths happens. Evaluate and the count animation both run this, so they can never disagree.
    private static int Count(List<FaceDefinition> faces, List<CountStep> record, int sevenValue = 7)
    {
        // 1. Faces → two lists. "5 + 9 0 ÷ 5" becomes numbers [5, 90, 5] and ops [+, ÷]
        List<int> numbers = new List<int>();
        List<Operator> ops = new List<Operator>();
        int current = 0;
        foreach (FaceDefinition face in faces)
        {
            if (face.type == FaceType.Number)
            {
                int digit = face.number == 7 ? sevenValue : face.number;   // Lucky Seven: a 14 in a 7's place adds 7 there
                current = current * 10 + digit;   // digits next to each other join: 9 then 0 → 90
            }
            else
            {
                numbers.Add(current);   // an operator ends the number before it
                ops.Add(face.op);
                current = 0;
            }
        }
        numbers.Add(current);   // the last number has no operator after it to end it

        // 2. Do one operator at a time until none are left
        while (ops.Count > 0)
        {
            int k = NextOperator(ops);
            if (record != null)   // copies: the lists keep changing, a snapshot must not
                record.Add(new CountStep { numbers = new List<int>(numbers), ops = new List<Operator>(ops), next = k });

            numbers[k] = Apply(ops[k], numbers[k], numbers[k + 1]);   // 90 ÷ 5 → 18, written where the 90 was
            numbers.RemoveAt(k + 1);                                 // the 5 is used up
            ops.RemoveAt(k);                                         // so is the ÷
        }

        if (record != null)
            record.Add(new CountStep { numbers = new List<int>(numbers), ops = new List<Operator>(ops), next = -1 });
        return numbers[0];
    }

    // THE RULE. This is the only line that differs between left to right and school maths.
    private static int NextOperator(List<Operator> ops)
    {
        if (!SchoolOrder) return 0;   // always the leftmost
        for (int i = 0; i < ops.Count; i++)
        {
            if (ops[i] == Operator.Multiply || ops[i] == Operator.Divide || ops[i] == Operator.Modulus) return i;
        }
        return 0;   // no × ÷ % left: the leftmost + or −
    }

    // "5+90÷5", with the part about to be counted coloured, and one token (focus) drawn bigger.
    // Tokens count in reading order: number 0, op 0, number 1, op 1... so number i is token 2i, operator i is 2i+1.
    public static string StepText(CountStep step, int focus = -1, float focusSize = 150f, string highlight = "#FFD54A")
    {
        StringBuilder text = new StringBuilder();
        int token = 0;
        for (int i = 0; i < step.numbers.Count; i++)
        {
            if (i == step.next) text.Append($"<color={highlight}>");
            AppendToken(text, step.numbers[i].ToString(), token++ == focus, focusSize);
            if (step.next >= 0 && i == step.next + 1) text.Append("</color>");
            if (i < step.ops.Count) AppendToken(text, OpSymbol(step.ops[i]), token++ == focus, focusSize);
        }
        return text.ToString();
    }

    private static void AppendToken(StringBuilder text, string token, bool big, float size)
    {
        if (big) text.Append($"<size={size:0}%>{token}</size>");
        else text.Append(token);
    }

    public static int Apply(Operator op, int a, int b)
    {
        switch (op)
        {
            case Operator.Add:
                return a + b;
            case Operator.Subtract:
                return a - b;
            case Operator.Multiply:
                return a * b;
            case Operator.Divide:
                if (b == 0)
                {
                    Debug.Log("Divide by Zero");
                    return 0;
                }
                return Mathf.CeilToInt((float)a / b);
            case Operator.Modulus:
                if (b == 0)
                {
                    Debug.Log("Modulus by Zero");
                    return 0;
                }
                return a % b;
        }
        return 0;
    }

    public static string OpSymbol(Operator op)
    {
        switch (op)
        {
            case Operator.Add: return "+";
            case Operator.Subtract: return "-";
            case Operator.Multiply: return "×";
            case Operator.Divide: return "÷";
            case Operator.Modulus: return "%";
        }
        return "?";
    }

    // ---------- Tables ----------

    // A round's result as a score for the current table: bigger is always better.
    //   High: the result minus busts.  Low: minus the distance from 0 (busts add to it, Low Cap at most) x the stake.
    //   Target: minus the distance from this round's target (busts add to it).
    public int TableScore(int result, int bust)
    {
        switch (Table)
        {
            case TableType.Low:
                return -Mathf.Min(Mathf.Abs(result) + bust, LowCap) * player.Stake;
            case TableType.Target:
                return -(Mathf.Abs(result - Target) + bust);
            case TableType.Limit:
                return (result > Target ? 0 : result) - bust;  
            default:
                return result - bust;
        }
    }
    // The number the margin compares for one side: the score at High and Limit, the distance at Low and Target
    public int MarginNumber(int result, int bust)
    {
        int s = TableScore(result, bust);
        if (Table == TableType.Low) return -s / player.Stake;
        if (Table == TableType.Target) return -s;
        return s;
    }

    // What "nothing fits" counts as when a Calculating enemy weighs up a rethrow
    public int WorstScore
    {
        get
        {
            switch (Table)
            {
                case TableType.Low: return -LowCap * player.Stake;
                case TableType.Target: return -(Mathf.Abs(Target) + OperatorBustPenalty);   // a result of 0 plus a bust
                default: return 0;
            }
        }
    }

    // How busts show in the equation: taken off at a High table, added to the distance at the others
    public string BustText(int penalty)
    {
        return Table == TableType.High || Table == TableType.Limit ? $" - {penalty}" : $" · BUST +{penalty}";   
    }

    // ---------- Enemy pools ----------

    // One enemy per fight. Slots set: a random one from each slot's pool. No slots: the Ladder as it is.
    private EnemyDefinition[] RollLadder()
    {
        if (slots == null || slots.Length == 0) return ladder;
        EnemyDefinition[] run = new EnemyDefinition[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            List<EnemyDefinition> pool = new List<EnemyDefinition>();
            if (slots[i] != null && slots[i].enemies != null)
                foreach (EnemyDefinition e in slots[i].enemies) if (e != null) pool.Add(e);
            if (pool.Count > 0) run[i] = pool[Random.Range(0, pool.Count)];
            else if (ladder != null && i < ladder.Length) run[i] = ladder[i];   // an empty slot falls back to the old ladder
            if (run[i] == null) Debug.LogError($"[Carry the One] GameController: slot {i + 1} has no enemy.");
        }
        return run;
    }

    // Act bosses hand over a die; the last fight is the final boss
    private bool IsBossFight(EnemyDefinition def) => def.dieDrop != null || Fight == ladder.Length;

#if UNITY_EDITOR
    // Turns the Ladder into Slots, one enemy each, so you only have to add the variants
    [ContextMenu("Copy Ladder Into Slots")]
    private void CopyLadderIntoSlots()
    {
        if (ladder == null || ladder.Length == 0) { Debug.LogWarning("[Carry the One] The Ladder is empty, nothing to copy."); return; }
        UnityEditor.Undo.RecordObject(this, "Copy Ladder Into Slots");
        slots = new LadderSlot[ladder.Length];
        for (int i = 0; i < ladder.Length; i++) slots[i] = new LadderSlot { enemies = new[] { ladder[i] } };
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"[Carry the One] {slots.Length} slots made from the Ladder. Add variants to each slot's Enemies list.");
    }
#endif

    // ---------- Relics ----------

    // Up to 'count' relics you don't have yet, in random order
    private List<RelicDefinition> RollRelics(int count)
    {
        List<RelicDefinition> bag = new List<RelicDefinition>();
        foreach (RelicDefinition r in relicPool)
        {
            if (r != null && !player.HasRelic(r.type)) bag.Add(r);
        }
        List<RelicDefinition> picks = new List<RelicDefinition>();
        while (picks.Count < count && bag.Count > 0)
        {
            int i = Random.Range(0, bag.Count);
            picks.Add(bag[i]);
            bag.RemoveAt(i);
        }
        if (picks.Count == 0 && player.Relics.Count == 0)
            Debug.LogError("[Carry the One] Fight " + Fight + " should give a relic, but GameController > Relic Pool is empty.");
        return picks;
    }

    // An empty pool used to skip every relic screen without a word
    private void CheckRelicSetup()
    {
        int found = 0;
        if (relicPool != null) foreach (RelicDefinition r in relicPool) if (r != null) found++;
        if (found == 0)
            Debug.LogError("[Carry the One] GameController > Relic Pool is empty, so there are no relic screens after the Relic Fights. " +
                           "Drag the Relic_ assets in, or right-click the GameController component > Find Relic Assets.");
        else if (relicBar == null && (relicSlots == null || relicSlots.Length == 0))
            Debug.LogWarning("[Carry the One] GameController has no Relic Bar, so the relics you hold never show. " +
                             "Make an empty UI object on your HUD canvas and drag it into Relic Bar.");
    }

    // Builds one hidden icon per relic inside Relic Bar, in a row. Icons show up as you pick relics.
    private void BuildRelicBar()
    {
        if (relicSlots == null || relicSlots.Length == 0)
        {
            if (relicBar == null) return;
            if (relicBar.GetComponent<HorizontalLayoutGroup>() == null)
            {
                HorizontalLayoutGroup row = relicBar.gameObject.AddComponent<HorizontalLayoutGroup>();
                row.spacing = relicIconSize * 0.3f;
                row.childControlWidth = false;
                row.childControlHeight = false;
                row.childForceExpandWidth = false;
                row.childForceExpandHeight = false;
            }

            int count = relicPool != null && relicPool.Length > 0 ? relicPool.Length : 5;
            relicSlots = new Image[count];
            for (int i = 0; i < count; i++)
            {
                GameObject icon = new GameObject($"Relic {i + 1}", typeof(RectTransform), typeof(Image));
                icon.transform.SetParent(relicBar, false);
                ((RectTransform)icon.transform).sizeDelta = Vector2.one * relicIconSize;
                Image image = icon.GetComponent<Image>();
                image.preserveAspect = true;
                image.color = relicColor;
                relicSlots[i] = image;
                icon.SetActive(false);
            }
        }

        foreach (Image slot in relicSlots)
        {
            if (slot == null) continue;
            RelicIcon hover = slot.GetComponent<RelicIcon>();
            if (hover == null) hover = slot.gameObject.AddComponent<RelicIcon>();
            hover.OnHover = ShowRelicTooltip;
        }
        if (relicTooltip != null) relicTooltip.text = "";
    }

    private void ShowRelicTooltip(RelicDefinition relic)
    {
        hoveredRelic = relic;
        if (relicTooltip == null) return;
        if (relic == null) { relicTooltip.text = ""; return; }

        string text = $"{relic.displayName.ToUpper()} · {relic.line.ToUpper()}";
        if (relic.type == RelicType.Insurance && player.InsuranceUsed) text += " · USED";
        if (relic.type == RelicType.RabbitsFoot && !player.RabbitsFootReady) text += " · USED THIS FIGHT";
        if (relic.type == RelicType.DoubleDown && doubleDownFight == Fight) text += doubleDownArmed ? " · ON THIS ROUND" : " · USED THIS FIGHT";
        if (relic.type == RelicType.OppositeDay && oppositeDayAct == Act) text += " · USED THIS ACT";
        relicTooltip.text = text;
    }

    // A relic was just picked or just did something: its icon pops (and the relic sound plays)
    private void PulseRelic(RelicType type)
    {
        if (relicClip != null) audio.PlayOneShot(relicClip);
        if (relicSlots == null) return;
        int i = heldRelics.FindIndex(r => r.type == type);
        if (i < 0 || i >= relicSlots.Length || relicSlots[i] == null || !relicSlots[i].gameObject.activeInHierarchy) return;
        StartCoroutine(PopScale(relicSlots[i].transform, 1.8f, 0.4f));
    }

    // Scale only: Punch also writes the position, which fights the relic bar's layout group
    private static IEnumerator PopScale(Transform target, float scale, float duration)
    {
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            target.localScale = Vector3.one * Mathf.Lerp(scale, 1f, Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }
        target.localScale = Vector3.one;
    }

#if UNITY_EDITOR
    // Right-click the GameController component header > Find Relic Assets: fills Relic Pool with every Relic asset in the project
    [ContextMenu("Find Relic Assets")]
    private void FindRelicAssets()
    {
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:RelicDefinition");
        List<RelicDefinition> found = new List<RelicDefinition>();
        foreach (string guid in guids)
        {
            RelicDefinition r = UnityEditor.AssetDatabase.LoadAssetAtPath<RelicDefinition>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (r != null) found.Add(r);
        }
        UnityEditor.Undo.RecordObject(this, "Find Relic Assets");
        relicPool = found.ToArray();
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"[Carry the One] Relic Pool: found {found.Count} relic(s).");
    }
#endif

    private void UpdateRelicBar()
    {
        if (relicSlots == null) return;
        heldRelics.Clear();
        foreach (RelicType type in player.Relics)
        {
            foreach (RelicDefinition r in relicPool)
            {
                if (r != null && r.type == type) { heldRelics.Add(r); break; }
            }
        }

        for (int i = 0; i < relicSlots.Length; i++)
        {
            if (relicSlots[i] == null) continue;
            bool show = i < heldRelics.Count;
            relicSlots[i].gameObject.SetActive(show);
            if (!show) continue;

            RelicDefinition r = heldRelics[i];
            relicSlots[i].sprite = r.icon;
            RelicIcon hover = relicSlots[i].GetComponent<RelicIcon>();
            if (hover != null) hover.Relic = r;
            bool spent = (r.type == RelicType.Insurance && player.InsuranceUsed)
                      || (r.type == RelicType.RabbitsFoot && !player.RabbitsFootReady)
                      || (r.type == RelicType.DoubleDown && doubleDownFight == Fight && !doubleDownArmed)
                      || (r.type == RelicType.OppositeDay && oppositeDayAct == Act);
            Color c = relicSlots[i].color;
            c.a = spent ? spentRelicAlpha : 1f;
            relicSlots[i].color = c;
        }
        if (hoveredRelic != null) ShowRelicTooltip(hoveredRelic);   // keeps "USED" up to date while you hover
    }

    // ---------- Score feedback ----------

    private void PlayResultFeedback(int result)
    {
        Color flashColor;
        float punchSize;
        float shake;

        if (result < 0)            // a loss: red, a small inward punch
        {
            flashColor = loseColor;
            punchSize = 0.8f;
            shake = 0f;
        }
        else if (result == 0)      // a draw: grey, barely a twitch
        {
            flashColor = Color.gray;
            punchSize = 0.9f;
            shake = 0f;
        }
        else                       // a win: the gradient, bigger with intensity
        {
            float intensity = Intensity(result);
            flashColor = scoreGradient.Evaluate(intensity);
            shake = intensity > 0.5f ? intensity * maxShake : 0f;
            punchSize = Mathf.Lerp(1.1f, 1.6f, intensity);
        }

        scoreText.color = flashColor;

        if (scorePunchRoutine != null) StopCoroutine(scorePunchRoutine);
        scorePunchRoutine = StartCoroutine(Punch(scoreText.transform, punchSize, 0.25f, shake));

        if (scoreFadeRoutine != null) StopCoroutine(scoreFadeRoutine);
        scoreFadeRoutine = StartCoroutine(FadeColor(scoreText, flashColor, Color.white, 1f, 0.25f));
    }

    // "37 - 24" → "13": the gap between the two results, as one last sum. Always winner minus loser, so it's never negative.
    // High: the two scores. Low and Target: the two DISTANCES (the loser's is bigger), and a Low table multiplies by the stake.
    private List<CountStep> MarginSteps(int playerScore, int enemyScore)
    {
        int a, b, multiplier = 1;
        if (Table == TableType.High || Table == TableType.Limit)
        {
            a = Mathf.Max(playerScore, enemyScore);
            b = Mathf.Min(playerScore, enemyScore);
        }
        else
        {
            int stake = Table == TableType.Low ? player.Stake : 1;
            int playerDistance = -playerScore / stake;   // TableScore made these negative (and x stake at Low): undo that
            int enemyDistance = -enemyScore / stake;
            a = Mathf.Max(playerDistance, enemyDistance);
            b = Mathf.Min(playerDistance, enemyDistance);
            multiplier = stake;
        }

        List<CountStep> steps = new List<CountStep>();
        steps.Add(new CountStep { numbers = new List<int> { a, b }, ops = new List<Operator> { Operator.Subtract }, next = 0 });
        int margin = a - b;
        if (multiplier > 1)
            steps.Add(new CountStep { numbers = new List<int> { margin, multiplier }, ops = new List<Operator> { Operator.Multiply }, next = 0 });
        steps.Add(new CountStep { numbers = new List<int> { margin * multiplier }, ops = new List<Operator>(), next = -1 });
        return steps;
    }

    // Adds "× 2" to the end of the margin count: [41 - 18], [23] becomes [41 - 18], [23 × 2], [46]
    private static void MultiplyMargin(List<CountStep> steps, int multiplier)
    {
        CountStep last = steps[steps.Count - 1];
        int amount = last.numbers[0];
        steps[steps.Count - 1] = new CountStep { numbers = new List<int> { amount, multiplier }, ops = new List<Operator> { Operator.Multiply }, next = 0 };
        steps.Add(new CountStep { numbers = new List<int> { amount * multiplier }, ops = new List<Operator>(), next = -1 });
    }

    // An exact hit: the target at a Target table, 0 at a Low table, the limit itself at a Limit table (a bust never counts)
    private bool IsBullseye(int score)
    {
        switch (Table)
        {
            case TableType.Target:
            case TableType.Low: return score == 0;
            case TableType.Limit: return Target > 0 && score == Target;
            default: return false;
        }
    }

    private static bool IsPrime(int n)
    {
        if (n < 2) return false;
        for (int k = 2; k * k <= n; k++) if (n % k == 0) return false;
        return true;
    }

    private IEnumerator PlayMargin(List<CountStep> steps, int amount, float intensity, bool roll, bool isPlayerDiff)
    {
        if (scoreFadeRoutine != null) StopCoroutine(scoreFadeRoutine);
        scoreText.color = Color.white;
        if (counter == null) { scoreText.text = amount.ToString(); yield break; }

        int bigger = steps[0].numbers[0];
        int gap = bigger - steps[0].numbers[1];
        if (gap > 0 && bigger >= photoFinishMin && gap <= bigger * photoFinishShare)
        {
            counter.Speed = photoFinishSpeed;
            strip.Flash("PHOTO FINISH", Color.yellow, 3f);
        }

        int knockoutAt = score > 0 ? enemy.Health : 0;
        float oldSpeed = counter.Speed;
        if (isPlayerDiff) counter.Speed = 0.6f;   // ~1.8× longer. Tune to taste (0.4–0.7)

        yield return counter.PlayMargin(scoreText, steps, roll ? scoreCounter : null, intensity, knockoutAt, Knockout, isPlayerDiff);

        counter.Speed = oldSpeed;  
    }

    // The damage count just passed the enemy's HP: this hit kills (the tubes still have to get there)
    private void Knockout()
    {
        strip.Flash("KNOCKOUT", Color.red, 2.5f);
        StartCoroutine(HitStop(0.08f));
        cameraController.Kick(6f);
        CameraShake.Instance?.AddTrauma(0.4f);
        if (knockoutClip != null) audio.PlayOneShot(knockoutClip);
    }

    // ---------- Reusable juice (static: any MonoBehaviour can StartCoroutine these) ----------

    public static IEnumerator Punch(Transform target, float punchScale, float duration, float shake)
    {
        Vector3 big = Vector3.one * punchScale;
        Vector3 startingPosition = target.localPosition;
        float elapsed = 0f;

        target.localScale = big;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            target.localPosition = startingPosition + (Vector3)Random.insideUnitCircle * shake * (1 - t);
            target.localScale = Vector3.Lerp(big, Vector3.one, t);
            yield return null;
        }

        target.localScale = Vector3.one;
        target.localPosition = startingPosition;
    }
    public static IEnumerator Pop(Transform target, Vector3 baseScale, float size, float duration, float wobbles = 1.5f)
    {
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float k = t / duration;   // 0 → 1 over the pop
                                      // cos swings 1 → -1 → 1 (big, squashed, big...), and (1-k)² fades the swing out
            float spring = Mathf.Cos(k * wobbles * Mathf.PI * 2f) * (1f - k) * (1f - k);
            target.localScale = baseScale * (1f + (size - 1f) * spring);
            yield return null;
        }
        target.localScale = baseScale;
    }

    // Freezes the game for a moment. Realtime, because WaitForSeconds itself would freeze too.
    public static IEnumerator HitStop(float seconds)
    {
        if (seconds <= 0f) yield break;
        Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(seconds);
        Time.timeScale = 1f;
    }

    public static IEnumerator FadeColor(TMP_Text text, Color from, Color to, float delay, float duration)
    {
        yield return new WaitForSeconds(delay);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            text.color = Color.Lerp(from, to, t);
            yield return null;
        }

        text.color = to;
    }

    private IEnumerator RevealResult(int diff, List<CountStep> margin)
    {
        float intensity = Intensity(diff);
        bool roll = diff > 0 && intensity >= rollThreshold;

        // ---- 1. ANTICIPATION ----
        yield return PlayMargin(margin, Mathf.Abs(diff), intensity, roll, diff > 0);

        // ---- 2. IMPACT: the number lands. No damage yet: the HP tubes aren't on screen ----
        PlayResultFeedback(diff);
        if (diff > 0)
        {
            if (intensity > 0.6f) StartCoroutine(HitStop(0.1f));
            cameraController.Kick(Mathf.Lerp(2f, 8f, intensity));
            ScoreSparks(Mathf.RoundToInt(Mathf.Lerp(10, 80, intensity)));
            audio.pitch = Mathf.Lerp(0.9f, 1.2f, intensity);
            audio.PlayOneShot(turnWinClip);
            CameraShake.Instance?.AddTrauma(intensity * 0.8f);
        }
        else if (diff < 0)
        {
            audio.pitch = Mathf.Lerp(0.9f, 0.6f, intensity);
            audio.PlayOneShot(loseClip);
            cameraController.Kick(Mathf.Lerp(1f, 4f, intensity));
        }

        // ---- 3. LINGER on the monitor ----
        yield return new WaitForSeconds(monitorHold);
        audio.pitch = 1f;
    }

    // The consequence: the damage lands while the HP tubes are on screen, they count down, and the kill waits for 0
    private IEnumerator Drain(int diff, System.Action applyDamage)
    {
        applyDamage?.Invoke();   // the tubes start ticking right now

        NixieHealth tubes = diff > 0 ? enemyHealthTubes : diff < 0 ? playerHealthTubes : null;
        if (tubes != null) yield return new WaitWhile(() => tubes.Ticking);

        if (enemy.IsDead)
        {
            StartCoroutine(HitStop(0.15f));
            cameraController.Kick(10f);
            CameraShake.Instance?.AddTrauma(1f);
            audio.PlayOneShot(winClip);
            enemy.SayDefeat();
        }
        else if (diff != 0) enemy.SayTurnResult(diff < 0);

        yield return new WaitForSeconds(timeBetweenRounds);
    }

    // A burst of sparks right in front of the score
    private void ScoreSparks(int count)
    {
        if (scoreSparks == null || count <= 0) return;
        if (!scoreSparks.gameObject.scene.IsValid()) scoreSparks = Instantiate(scoreSparks);

        // camera.forward points INTO the scene, so minus it points back toward the camera.
        // Bursting a little in front of the text keeps the sparks from hiding behind the monitor's glass or bezel.
        Vector3 towardCamera = -Camera.main.transform.forward;
        scoreSparks.transform.position = scoreText.transform.position + towardCamera * sparkForward;
        scoreSparks.Emit(count);
    }

    // Fades the equations and the score at the same time, in opposite directions.
    // showScore = true: equations out, score in. false: the other way round.
    private IEnumerator SwapMonitor(bool showScore)
    {
        float eqFrom = equationsGroup.alpha, eqTo = showScore ? 0f : 1f;
        float scFrom = scoreText.alpha, scTo = showScore ? 1f : 0f;

        for (float t = 0f; t < swapTime; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / swapTime);
            equationsGroup.alpha = Mathf.Lerp(eqFrom, eqTo, k);
            scoreText.alpha = Mathf.Lerp(scFrom, scTo, k);
            yield return null;
        }
        equationsGroup.alpha = eqTo;
        scoreText.alpha = scTo;
    }

    // ------ Debug ------
    // Dev panel: abandon whatever is happening and start fight n fresh (camera, intro, banner and all)
    public void DebugJumpToFight(int n)
    {
        n = Mathf.Clamp(n, 1, ladder.Length);
        StopAllCoroutines();                 // the round, the enemy turn and the camera moves all live in here
        Time.timeScale = 1f;
        roundEnding = false;
        playerResult = null;
        enemyResult = null;
        player.RevertSwaps();

        Fight = n;
        int started = 0;
        foreach (int s in actStarts) if (s < n) started++;
        Act = Mathf.Max(1, started);         // BeginFight bumps it (and plays ACT 2) when n is an act's first fight

        player.ResetRound();
        StartCoroutine(BeginFight());
    }
}

// One fight on the ladder: the enemies it can be. Each run picks one of them at random.
[System.Serializable]
public class LadderSlot
{
    public EnemyDefinition[] enemies;
}

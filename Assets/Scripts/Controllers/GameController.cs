using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
    public int OperatorBustPenalty => bustBase * Fight * player.Stake;   // the stake grows with your number dice, like your HP

    [Header("Acts")]
    [SerializeField] private int[] actStarts = { 1, 9, 17 };   // first fight of each act: 8 + 8 + 5 fights
    public int Act { get; private set; } = 1;

    [Header("Audio")]
    [SerializeField] private AudioClip winClip;
    [SerializeField] private AudioClip turnWinClip;
    [SerializeField] private AudioClip loseClip;

    [Header("Run")]
    [SerializeField] private EnemyDefinition[] ladder;
    public EnemyDefinition[] Ladder => ladder;
    public int[] ActStarts => actStarts;
    public event System.Action<int> OnFightStarted;   
    private EnemyTrait[] Traits => ladder[Fight - 1].traits;
    [SerializeField] private float introTime = 2f;
    [SerializeField] private float actBannerTime = 2.5f;

    [Header("Round Flow")]
    [SerializeField] private float timeBetweenRounds = 2f;
    [SerializeField] FaceDefinition[] rewardPool;
    private List<FaceDefinition> facePool;
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
    [SerializeField] private int[] relicFights = { 4, 8, 16 }; // a relic screen follows the reward screen after these fights
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
    [SerializeField] private float marginReadTime = 0.2f;   // seconds per token while reading "37 - 24"
    [SerializeField] private float readPopSize = 170f;      // percent
    [SerializeField] private float readHoldSize = 125f;     // percent
    [SerializeField] private AudioClip marginTick;          // optional: a soft tick per token read
    [SerializeField] private float fastForward = 4f;        // holding Shift or the mouse plays the reveal this much faster
    [SerializeField] private NixieHealth playerHealthTubes;
    [SerializeField] private NixieHealth enemyHealthTubes;


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
        SchoolOrder = schoolOrder;
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
        if (Fight == 1) Tutorial.Hint("throw", "DRAG A DIE AND LET GO TO THROW IT");
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
        yield return enemy.PlayCount(() => cameraController.Kick());       
        yield return player.PlayCount(() => cameraController.Kick(4f));       

        int diff = playerScore - enemyScore;
        Telemetry.RoundScored(playerScore, enemyScore);
        score = diff;
        List<CountStep> margin = MarginSteps(playerScore, enemyScore);  

        yield return SwapMonitor(true);
        yield return RevealResult(diff, margin);          // the show, zoomed in

        enemy.ClearTotal();
        player.ClearTotal();
        yield return SwapMonitor(false);                  // equations back, while still close enough to read them
        yield return cameraController.FocusBack(0.5f);    // zoom out: now the HP tubes are in view

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
                if (enemy.IsDead && player.HasRelic(RelicType.CarryTheOne)) carry = diff - hpBefore;
            }
        });

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

            if (beaten.dieDrop != null)          // act bosses hand their die over: one more digit, one more zero on your HP
            {
                player.AddDie(beaten.dieDrop);
                player.Heal(player.MaxHealth - player.Health);   // and a full heal for the next act
                strip.Flash($"NEW DIE · MAX HP {player.MaxHealth}", Color.yellow, 2.5f);
                yield return new WaitForSeconds(2.5f);
            }

            int heal = player.MaxHealth * healPercent / 100;
            List<RelicDefinition> relicChoices = System.Array.IndexOf(relicFights, Fight) >= 0 ? RollRelics(3) : null;
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
        equationPanel.SetActive(false);
        endPanel.SetActive(true);
        endStats.text = won ? $"ALL {ladder.Length} FIGHTS CLEARED"
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

    public static int Evaluate(List<FaceDefinition> faces) => Count(faces, null);
    public static List<CountStep> CountSteps(List<FaceDefinition> faces)
    {
        List<CountStep> steps = new List<CountStep>();
        Count(faces, steps);
        return steps;
    }

    // The ONE place maths happens. Evaluate and the count animation both run this, so they can never disagree.
    private static int Count(List<FaceDefinition> faces, List<CountStep> record)
    {
        // 1. Faces → two lists. "5 + 9 0 ÷ 5" becomes numbers [5, 90, 5] and ops [+, ÷]
        List<int> numbers = new List<int>();
        List<Operator> ops = new List<Operator>();
        int current = 0;
        foreach (FaceDefinition face in faces)
        {
            if (face.type == FaceType.Number)
            {
                current = current * 10 + face.number;   // digits next to each other join: 9 then 0 → 90
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
                return -Mathf.Min(Mathf.Abs(result) + bust, lowCap) * player.Stake;
            case TableType.Target:
                return -(Mathf.Abs(result - Target) + bust);
            case TableType.Limit:
                return (result > Target ? 0 : result) - bust;  
            default:
                return result - bust;
        }
    }

    // What "nothing fits" counts as when a Calculating enemy weighs up a rethrow
    public int WorstScore
    {
        get
        {
            switch (Table)
            {
                case TableType.Low: return -lowCap * player.Stake;
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
            Debug.LogError("[Carry the One] GameController > Relic Pool is empty, so there are no relic screens after fights 4, 8 and 16. " +
                           "Drag the five Relic_ assets in, or right-click the GameController component > Find Relic Assets.");
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
                      || (r.type == RelicType.RabbitsFoot && !player.RabbitsFootReady);
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

    // Reads the margin token by token on the score text, then shows the answer (big wins: the nixie roll)
    private IEnumerator PlayMargin(List<CountStep> steps, int amount, float intensity, bool roll)
    {
        if (scoreFadeRoutine != null) StopCoroutine(scoreFadeRoutine);
        scoreText.color = Color.white;

        for (int i = 0; i < steps.Count; i++)
        {
            CountStep step = steps[i];
            if (step.next < 0)   // the answer
            {
                if (roll && scoreCounter != null) yield return scoreCounter.Roll(amount, intensity);
                else scoreText.text = amount.ToString();
                yield break;
            }

            if (i > 0) StartCoroutine(Pop(scoreText.transform, Vector3.one, 1.25f, 0.2f));   // "× stake": a small collapse

            int first = step.next * 2;
            for (int t = first; t <= first + 2; t++)
            {
                if (marginTick != null) { audio.pitch = 0.9f + (t - first) * 0.1f; audio.PlayOneShot(marginTick, 0.7f); }
                yield return ReadToken(step, t);
            }
        }
    }

    private IEnumerator ReadToken(CountStep step, int token)
    {
        float t = 0f;
        while (t < marginReadTime)
        {
            float k = t / marginReadTime;
            k = 1f - (1f - k) * (1f - k);
            scoreText.text = StepText(step, token, Mathf.Lerp(readPopSize, readHoldSize, k));
            t += Time.deltaTime * RevealSpeed;
            yield return null;
        }
    }

    private float RevealSpeed =>
        (Keyboard.current != null && Keyboard.current.shiftKey.isPressed) ||
        (Mouse.current != null && Mouse.current.leftButton.isPressed) ? fastForward : 1f;

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
        yield return PlayMargin(margin, Mathf.Abs(diff), intensity, roll);

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
}
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
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

    [Header("Score UI")]
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private Gradient scoreGradient;
    [SerializeField] private StripDisplay strip;
    [SerializeField] private float maxLog = 3f;
    [SerializeField] private float maxShake = 15f;

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
    [SerializeField] private float introTime = 2f;
    [SerializeField] private float actBannerTime = 2.5f;

    [Header("Round Flow")]
    [SerializeField] private float timeBetweenRounds = 2f;
    [SerializeField] FaceDefinition[] rewardPool;
    private List<FaceDefinition> facePool;
    [SerializeField, Range(0, 100)] private int healPercent = 40;   // the heal reward gives this much of your max HP

    [Header("Tables")]
    [SerializeField] private int lowCap = 20;                 // at a Low table a distance counts this much at most (x the stake)
    [SerializeField] private TMP_Text tableText;              // "TARGET 437" / "CLOSEST TO 0" for the whole round. Empty: the strip shows it
    [SerializeField] private Color tableColor = new Color32(0xFF, 0x71, 0x34, 0xFF);
    [SerializeField] private float tableBannerTime = 2.5f;
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

    private const string formUrl = "https://docs.google.com/forms/d/e/1FAIpQLScix26Ts907fGNdApSgtKu10-K0fdspGZps9k1Yjl9vXGlMGg/viewform";
    private const string resultEntry = "entry.2115005332";
    private const string fightEntry = "entry.1084623422";
    private const string beatenEntry = "entry.1538550322";
    private const string minutesEntry = "entry.522183546";
    private const string diceEntry = "entry.1966468218";
    private const string versionEntry = "entry.173446048";

    private int score;
    private bool roundEnding;
    private bool runWon;
    private Coroutine scorePunchRoutine;
    private Coroutine scoreFadeRoutine;

    public int Score => score;

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
        StartCoroutine(BeginFight());
    }

    // ---------- Round flow ----------

    // Called by DiceSide when every slot is filled (or busted) and the last die has landed.
    // result is the plain equation, bust what the busted dice cost, kept apart because the table decides what a bust does.
    public void OnEquationComplete(DiceSide side, int result, int bust)
    {
        if (side == player) { playerResult = result; playerBust = bust; }
        else { enemyResult = result; enemyBust = bust; }
        ShowTotal(side, result, bust);

        if (playerResult.HasValue && enemyResult.HasValue && !roundEnding)
        {
            StartCoroutine(EndRoundRoutine());
        }
    }

    private IEnumerator BeginFight()
    {
        player.InputBlocked = true;   // nobody throws until the table is set (and the target drawn)
        SetTableText("");

        if (Act < actStarts.Length && Fight == actStarts[Act])   // first fight of a new act
        {
            Act++;
            strip.Flash($"ACT {Act}", Color.yellow, actBannerTime);
            yield return new WaitForSeconds(actBannerTime);
        }

        EnemyDefinition def = ladder[Fight - 1];   // Fight starts at 1
        Table = def.table;
        targetRange = def.targetRange;
        enemy.Load(def);
        player.ClearTotal();
        player.OnFightStart();
        enemy.SayIntro();
        strip.Flash($"VS {enemy.DisplayName}", Color.red, 3f);
        yield return new WaitForSeconds(introTime);
        scoreText.text = "";

        if (Table != TableType.High)
        {
            strip.Flash(TableBanner(), tableColor, tableBannerTime);
            yield return new WaitForSeconds(tableBannerTime);
            yield return reward.SwapRoutine(player, Table);   // the spare at Low tables, the Pocket Die at both
        }

        if (carry > 0)   // Carry the One: last fight's overkill hits this enemy, but never finishes it
        {
            int dealt = Mathf.Min(carry, enemy.Health - 1);
            carry = 0;
            if (dealt > 0)
            {
                enemy.TakeDamage(dealt);
                player.TriggerRelic(RelicType.CarryTheOne);
                strip.Flash($"CARRY THE ONE · {dealt}", tableColor, 2f);
                yield return new WaitForSeconds(1.5f);
            }
        }

        if (Fight == 1) Tutorial.Hint("throw", "DRAG A DIE AND LET GO TO THROW IT");

        StartRound();
    }

    // Every round: a Target table draws its number first, then both sides may throw
    private void StartRound()
    {
        if (Table == TableType.Target)
        {
            Target = Random.Range(targetRange.x, targetRange.y + 1);
            SetTableText($"TARGET {Target}");
            if (tableText == null) ShowTargetInScore();   // no Table Text: the big score number shows the target instead
        }
        else if (Table == TableType.Low) SetTableText("CLOSEST TO 0");
        else SetTableText("");

        player.InputBlocked = false;
        enemy.StartTurn();
    }

    // The score is the biggest, clearest text on the monitor. At a Target table the last round's damage matters less
    // than what you're aiming for, so it shows the target while you throw (the damage still flashes at round end).
    private void ShowTargetInScore()
    {
        if (scoreFadeRoutine != null) StopCoroutine(scoreFadeRoutine);
        scoreText.color = tableColor;
        scoreText.text = $"TARGET {Target}";
        if (scorePunchRoutine != null) StopCoroutine(scorePunchRoutine);
        scorePunchRoutine = StartCoroutine(Punch(scoreText.transform, 1.3f, 0.25f, 0f));
    }

    private string TableBanner()
    {
        if (Table == TableType.Low) return "LOW TABLE · CLOSEST TO 0 WINS";
        return $"TARGET TABLE · {targetRange.x} TO {targetRange.y}";
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

    private IEnumerator EndRoundRoutine()
    {
        roundEnding = true;
        player.InputBlocked = true;   // until the next round starts: no grabbing dice during the reward or relic screens

        // The table turns both results into scores (bigger is better), the difference is the damage
        int diff = TableScore(playerResult.Value, playerBust) - TableScore(enemyResult.Value, enemyBust);
        bool insured = false;

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

        score = diff;
        scoreText.text = diff.ToString();
        PlayResultFeedback(diff);
        if (enemy.IsDead) enemy.SayDefeat();
        else if (diff != 0) enemy.SayTurnResult(diff < 0);   // diff < 0 = the enemy won the turn
        if (diff > 0)
        {
            strip.Flash("YOU WIN", Color.green, timeBetweenRounds);
            audio.PlayOneShot(enemy.IsDead ? winClip : turnWinClip);
        }
        else if (insured) { strip.Flash("INSURANCE PAID OUT!", Color.yellow, timeBetweenRounds); audio.PlayOneShot(loseClip); }
        else if (diff < 0) { strip.Flash("YOU LOST", Color.red, timeBetweenRounds); audio.PlayOneShot(loseClip); }
        else { strip.Flash("DRAW", Color.white, timeBetweenRounds); }

        yield return new WaitForSeconds(timeBetweenRounds);

        if (player.IsDead)
        {
            player.RevertSwaps();                // so the survey reports your own dice, not the spare
            yield return EndRun(false);
            yield break;
        }

        if (enemy.IsDead)
        {
            player.RevertSwaps();                // your own dice come back before any reward
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
        StartRound();
    }

    private IEnumerator EndRun(bool won)
    {
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
    public void ToMenu() => SceneManager.LoadScene("MainMenu");

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
            + Field(versionEntry, Application.version);

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

    public static int Evaluate(List<FaceDefinition> faces)
    {
        int total = 0;
        int currentNumber = 0;
        Operator pendingOp = Operator.Add;

        foreach (FaceDefinition face in faces)
        {
            if (face.type == FaceType.Number)
            {
                currentNumber = (currentNumber * 10) + face.number;
            }
            else
            {
                total = Apply(pendingOp, total, currentNumber);
                pendingOp = face.op;
                currentNumber = 0;
            }
        }

        total = Apply(pendingOp, total, currentNumber);
        return total;
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
        return Table == TableType.High ? $" - {penalty}" : $" · BUST +{penalty}";
    }

    // The faint total that sits to the right of each finished equation. At Low and Target tables the distance
    // is the number that decides the round, so it goes in the brighter accent part: "= 425 · 12 OFF".
    private void ShowTotal(DiceSide side, int result, int bust)
    {
        switch (Table)
        {
            case TableType.Low:
                side.ShowTotal($"={result}", bust > 0 ? $"{Mathf.Min(Mathf.Abs(result) + bust, lowCap)} OFF" : "");
                break;
            case TableType.Target:
                side.ShowTotal($"={result}", $"{Mathf.Abs(result - Target) + bust} OFF");
                break;
            default:
                side.ShowTotal($"={result - bust}");
                break;
        }
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

        if (result <= 0)
        {
            flashColor = Color.gray;
            punchSize = 0.8f;
            shake = 0f;
        }
        else
        {
            // Measured against the stake, so a 500 hit in act 2 feels like a 50 in act 1 (instead of every act 3 hit maxing out)
            float intensity = Mathf.InverseLerp(0f, maxLog, Mathf.Log10((result + 1f) / player.Stake));
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
}
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;  

public class GameController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerController player;
    [SerializeField] private EnemyController enemy;
    [SerializeField] private RewardController reward;
    [SerializeField] private AudioSource audio;

    private int? playerResult;
    private int? enemyResult;

    [Header("Score UI")]
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private Gradient scoreGradient;
    [SerializeField] private StripDisplay strip;
    [SerializeField] private float maxLog = 3f;
    [SerializeField] private float maxShake = 15f;

    [Header("Bust")]
    [SerializeField] private int bustBase = 3;
    public int Fight { get; private set; } = 1;
    public int OperatorBustPenalty => bustBase * Fight;

    [Header("Audio")]
    [SerializeField] private AudioClip winClip;
    [SerializeField] private AudioClip turnWinClip;
    [SerializeField] private AudioClip loseClip;

    [Header("Run")]
    [SerializeField] private EnemyDefinition[] ladder;
    [SerializeField] private float introTime = 2f;

    [Header("Round Flow")]
    [SerializeField] private float timeBetweenRounds = 2f;
    [SerializeField] FaceDefinition[] rewardPool;
    private List<FaceDefinition> facePool;
    [SerializeField] private int healAmount = 20;

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
        StartCoroutine(BeginFight());
    }

    // ---------- Round flow ----------

    // Called by PlayerController when every slot is filled and the last die has landed
    public void OnEquationComplete(DiceSide side, int result)
    {
        if (side == player) playerResult = result;
        else enemyResult = result;

        if (playerResult.HasValue && enemyResult.HasValue && !roundEnding)
        {
            StartCoroutine(EndRoundRoutine());
        }
    }
    private IEnumerator BeginFight()
    {
        EnemyDefinition def = ladder[Fight - 1];   // Fight starts at 1
        enemy.Load(def);
        enemy.SayIntro();
        strip.Flash($"VS {enemy.DisplayName}", Color.red, 3f);
        yield return new WaitForSeconds(introTime);
        scoreText.text = "";

        if (Fight == 1) Tutorial.Hint("throw", "DRAG A DIE AND LET GO TO THROW IT");

        enemy.StartTurn();
    }

    private IEnumerator EndRoundRoutine()
    {
        roundEnding = true;

        int diff = playerResult.Value - enemyResult.Value;

        if (diff < 0) player.TakeDamage(-diff);
        else if (diff > 0) enemy.TakeDamage(diff);

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
        else if (diff < 0) { strip.Flash("YOU LOST", Color.red, timeBetweenRounds); audio.PlayOneShot(loseClip); }
        else { strip.Flash("DRAW", Color.white, timeBetweenRounds); }

        yield return new WaitForSeconds(timeBetweenRounds);

        if (player.IsDead)
        {
            yield return EndRun(false);
            yield break;
        }

        if (enemy.IsDead)
        {
            if (Fight == ladder.Length)          // beat the last enemy: no reward screen
            {
                yield return EndRun(true);
                yield break;
            }

            EnemyDefinition beaten = ladder[Fight - 1];
            facePool.AddRange(beaten.unlockFaces);
            yield return reward.RewardRoutine(player, facePool.ToArray(), beaten.dieDrop, healAmount);
            Fight++;

            player.ResetRound();
            playerResult = null;
            enemyResult = null;
            roundEnding = false;
            yield return BeginFight();   // next enemy, intro, then it throws
            yield break;
        }

        player.ResetRound();
        enemy.ResetRound();
        playerResult = null;
        enemyResult = null;
        roundEnding = false;
        enemy.StartTurn();
    }
    private IEnumerator EndRun(bool won)
    {
        equationPanel.SetActive(false);
        endPanel.SetActive(true);
        endStats.text = won ? $"ALL {ladder.Length} FIGHTS CLEARED"
                            : $"REACHED FIGHT {Fight} · BEATEN BY {ladder[Fight - 1].displayName.ToUpper()}";
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
            float intensity = Mathf.InverseLerp(0f, maxLog, Mathf.Log10(result + 1));
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
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

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
    [SerializeField] private InputActionReference clickAction;   // your LMB action

    [Header("Round Flow")]
    [SerializeField] private float timeBetweenRounds = 2f;
    [SerializeField] FaceDefinition[] rewardPool;

    private int score;
    private bool roundEnding;
    private Coroutine scorePunchRoutine;
    private Coroutine scoreFadeRoutine;

    public int Score => score;

    private void Start()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 144;
        scoreText.text = "0";
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

        scoreText.text = "VS " + def.displayName.ToUpper();
        // later: def.intro in the dialogue box
        yield return new WaitForSeconds(introTime);
        scoreText.text = "";

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
            yield return EndRun("GAME OVER", Color.red);
            yield break;
        }

        if (enemy.IsDead)
        {
            // later: def.defeatLine in the dialogue box
            yield return reward.RewardRoutine(player.GetDice(), rewardPool);
            Fight++;

            if (Fight > ladder.Length)
            {
                yield return EndRun("YOU WIN", Color.green);
                yield break;
            }

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
    private IEnumerator EndRun(string message, Color color)
    {
        scoreText.text = message;
        strip.Show("CLICK", color);
        yield return new WaitUntil(() => clickAction.action.WasPressedThisFrame());
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        yield break;
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
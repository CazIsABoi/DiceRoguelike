using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

// Records how each run goes and sends it to the Google Sheet, so playtesters don't need a form.
// Everything is static, call it from anywhere:
//   Telemetry.StartRun();
//   Telemetry.StartFight(enemyDef, player, enemy);
//   Telemetry.EndFight(playerWon);
//   Telemetry.Choice("face", offered, picked, replaced);
//   Telemetry.EndRun(won);
// DiceSide reports equations, busts and new rounds by itself, and damage is read from OnHealthChanged.
// Batches are saved before they're sent: a failed one goes out with the next batch or on the next launch.
public static class Telemetry
{
    private const string Url = "https://script.google.com/macros/s/AKfycbwLwxzycHPtkD4hLnh20CNmKuvK1yNSFCqzJnK4JNmQKzOHzGmFvFvhnunVhaFHxfBjoQ/exec";

    public static bool Enabled = true;          // false = nothing is recorded or sent
    public static bool SendFromEditor = true;   // your own test runs show platform "WindowsEditor" or "OSXEditor"

    private const string PlayerKey = "Telemetry.Player";
    private const string RunCountKey = "Telemetry.RunCount";
    private const string PendingKey = "Telemetry.PendingRun";
    private const string OutboxKey = "Telemetry.Outbox";
    private const int MaxOutbox = 40;   // PlayerPrefs is small in WebGL, the oldest batches go first

    // ---------- Rows: one class per tab, the fields become the columns in this order ----------

    [Serializable]
    private class RunRow
    {
        public string runId, playerId, session, version, platform;
        public int runNumber;                  // 1 = this player's first run ever
        public string result = "abandoned";    // won, lost, abandoned
        public int fightsReached, fightsWon;
        public string lastEnemy;               // who ended the run, or who they quit on
        public int playerHp, playerMaxHp;
        public int rounds, playerBusts;
        public float minutes;
        public string relics, startDice, finalDice, startedLocal;
    }

    [Serializable]
    private class FightRow
    {
        public string runId;
        public int fight, act;
        public string enemy, table, brain;
        public int enemyMaxHp;
        public int carried;
        public string result;                  // won, lost, abandoned
        public int rounds, roundsWon;
        public int damageDealt, damageTaken, healed;
        public int playerHpStart, playerHpEnd, playerMaxHp;
        public int playerBusts, enemyBusts, playerThrows, bonusRethrows;
        public float seconds;
        public string playerDice, relics;
    }

    [Serializable]
    private class RoundRow
    {
        public string runId;
        public int fight, round;
        public string enemy, table;
        public int target;                     // Target tables only
        public string playerEquation;
        public int playerResult, playerBustPenalty, playerBusts;

        public string playerBustFaces;
        public int playerThrows, bonusRethrows;
        public float playerSeconds;            // from the round starting to the player's equation being done
        public string enemyEquation;
        public int enemyResult, enemyBustPenalty, enemyBusts, enemyThrows;
        public int playerScore, enemyScore, diff;   // TableScore for each side (bigger is better), diff = the damage
        public string winner;                       // player, enemy or draw
        public int damageToEnemy, damageToPlayer, playerHealed;
        public int playerHp, enemyHp;
        public float seconds;
        [NonSerialized] public bool hasEquation;
        [NonSerialized] public bool scored;
    }

    [Serializable]
    private class ChoiceRow
    {
        public string runId;
        public int afterFight;
        public string kind, offered, picked, replaced;
        public float seconds;                  // time to pick, since the fight ended or the last pick
        public int playerHp, playerMaxHp;
    }

    [Serializable]
    private class FeedbackRow
    {
        public string runId, playerId, result;
        public int rating;
        public string comment;
    }

    [Serializable]
    private class Payload
    {
        public string batch;
        public RunRow[] runs = new RunRow[0];
        public FightRow[] fights = new FightRow[0];
        public RoundRow[] rounds = new RoundRow[0];
        public ChoiceRow[] choices = new ChoiceRow[0];
        public FeedbackRow[] feedback = new FeedbackRow[0];
    }

    [Serializable] private class Outbox { public List<string> items = new List<string>(); }

    // ---------- State ----------

    private static RunRow run;
    private static FightRow fight;
    private static RoundRow round;
    private static readonly List<RoundRow> fightRounds = new List<RoundRow>();
    private static readonly List<string> relics = new List<string>();
    private static DiceSide player, enemy;
    private static float runStart, fightStart, roundStart, lastEvent;
    private static int nextTarget;
    private static readonly int[] lastHp = new int[2], lastMax = new int[2];   // 0 = player, 1 = enemy
    private static string session, lastRunId, lastResult;
    private static Outbox outbox;
    private static bool sending;
    private static int badAnswers;

    private static float Now => Time.realtimeSinceStartup;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()   // in case domain reload is off
    {
        run = null; fight = null; round = null;
        fightRounds.Clear(); relics.Clear();
        player = null; enemy = null; nextTarget = 0;
        session = null; lastRunId = null; lastResult = null;
        outbox = null; sending = false; badAnswers = 0;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        Ready();
        SendPendingRun();   // a run from last time that never ended (tab closed, game quit)
        Flush();            // batches that didn't get through last time
    }

    private static void Ready()
    {
        if (session == null) session = NewId();
        if (outbox != null) return;
        string json = PlayerPrefs.GetString(OutboxKey, "");
        outbox = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<Outbox>(json);
        if (outbox == null) outbox = new Outbox();
    }

    // ---------- Run ----------

    public static void StartRun()
    {
        if (!Enabled) return;
        Ready();
        if (run != null) Finish("abandoned");   // a new run started before the last one ended
        SendPendingRun();

        relics.Clear();
        int number = PlayerPrefs.GetInt(RunCountKey, 0) + 1;
        PlayerPrefs.SetInt(RunCountKey, number);
        run = new RunRow
        {
            runId = NewId(),
            playerId = PlayerId(),
            session = session,
            version = Application.version,
            platform = Application.platform.ToString(),
            runNumber = number,
            startedLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
        };
        runStart = lastEvent = Now;
        SavePending();
    }

    public static void EndRun(bool won) => Finish(won ? "won" : "lost");

    // Optional, for a "quit to menu" button. Otherwise the next run or next launch sends it as abandoned
    public static void AbandonRun() => Finish("abandoned");

    // ---------- Fight ----------

    // When an enemy sits down. 'act' is optional, pass it if you track acts
    public static void StartFight(EnemyDefinition enemyDef, DiceSide playerSide, DiceSide enemySide, int act = 0, int carriedDamage = 0)
    {
        if (!Enabled) return;
        if (run == null) StartRun();
        if (fight != null) CloseFight("abandoned");

        Unwatch();
        player = playerSide;
        enemy = enemySide;
        Watch();

        run.fightsReached++;
        fight = new FightRow
        {
            runId = run.runId,
            fight = run.fightsReached,
            act = act,
            enemy = Name(enemyDef),
            table = enemyDef != null ? enemyDef.table.ToString() : "",
            brain = enemyDef != null ? enemyDef.brain.ToString() : "",
            enemyMaxHp = enemyDef != null ? enemyDef.maxHP : 0,
            playerHpStart = player != null ? player.Health : 0,
            playerMaxHp = player != null ? player.MaxHealth : 0,
            relics = run.relics,
            carried = carriedDamage
        };
        run.lastEnemy = fight.enemy;
        fightStart = Now;
        fightRounds.Clear();
        OpenRound();
        SavePending();
    }

    // When a fight is decided, after the last damage
    public static void EndFight(bool playerWon)
    {
        if (fight != null) CloseFight(playerWon ? "won" : "lost");
    }

    // ---------- Round ----------

    // Target tables: call it where the round's target is picked
    public static void SetTarget(int target)
    {
        if (round != null && !round.hasEquation) round.target = target;
        else nextTarget = target;
    }

    // DiceSide.ResetRound calls this
    public static void StartRound()
    {
        if (fight == null) return;
        if (round != null && !round.hasEquation) { roundStart = Now; return; }   // nothing happened in this one yet
        CloseRound();
        OpenRound();
    }

    // DiceSide calls this when a side's equation is done
    public static void EquationDone(bool isPlayer, string equation, int result, int bustPenalty, string dice)
    {
        if (round == null) return;
        round.hasEquation = true;
        if (isPlayer)
        {
            round.playerEquation = equation;
            round.playerResult = result;
            round.playerBustPenalty = bustPenalty;
            round.playerSeconds = Secs(Now - roundStart);
            if (!string.IsNullOrEmpty(dice))
            {
                fight.playerDice = dice;
                run.finalDice = dice;
                if (string.IsNullOrEmpty(run.startDice)) run.startDice = dice;
            }
        }
        else
        {
            round.enemyEquation = equation;
            round.enemyResult = result;
            round.enemyBustPenalty = bustPenalty;
        }
    }

    // DiceSide.Bust calls this
    public static void Bust(bool isPlayer, FaceDefinition face)
    {
        if (round == null) return;
        if (!isPlayer) { round.enemyBusts++; return; }
        round.playerBusts++;
        round.playerBustFaces = string.IsNullOrEmpty(round.playerBustFaces) ? Name(face) : round.playerBustFaces + " " + Name(face);
    }
    public static void RoundScored(int playerScore, int enemyScore)
    {
        if (round == null) return;
        round.playerScore = playerScore;
        round.enemyScore = enemyScore;
        round.diff = playerScore - enemyScore;
        round.scored = true;
    }

    // A die's faces, e.g. "[1 2 3 4 5 6]"
    public static string Name(Die die)
    {
        if (die == null) return "";
        List<string> faces = new List<string>();
        foreach (FaceDefinition f in die.GetCurrentFaces()) faces.Add(f == null ? "-" : Name(f));
        return "[" + string.Join(" ", faces) + "]";
    }

    // The run that just ended, for linking a survey answer to its row
    public static string LastRunId => lastRunId;

    // DiceController: every time a die is thrown (the first throw and every rethrow)
    public static void DieThrown(DiceSide side)
    {
        if (round == null || side == null) return;
        if (side == player) round.playerThrows++;
        else if (side == enemy) round.enemyThrows++;
    }

    // PlayerController.SpendBonusRethrow (Rabbit's Foot)
    public static void BonusRethrow()
    {
        if (round != null) round.bonusRethrows++;
    }

    // ---------- Rewards ----------

    // Call it once the choice is made. picked = null or "" if they skipped.
    // replaced = what a new face went over, e.g. "1 on die 2". Use kind "relic" for relics, so the relic list stays current
    public static void Choice(string kind, IEnumerable<string> offered, string picked, string replaced = "")
    {
        if (!Enabled || run == null) return;
        ChoiceRow row = new ChoiceRow
        {
            runId = run.runId,
            afterFight = run.fightsReached,
            kind = kind,
            offered = offered != null ? string.Join(" | ", offered) : "",
            picked = string.IsNullOrEmpty(picked) ? "skip" : picked,
            replaced = replaced ?? "",
            seconds = Secs(Now - lastEvent),
            playerHp = player != null ? player.Health : 0,
            playerMaxHp = player != null ? player.MaxHealth : 0
        };
        lastEvent = Now;

        if (kind == "relic" && row.picked != "skip")
        {
            relics.Add(row.picked);
            run.relics = string.Join(", ", relics);
        }
        Enqueue(new Payload { choices = new[] { row } });
        SavePending();
    }

    // Optional: a 1 to 5 rating and a comment from the game-over screen, tied to the run that just ended
    public static void Feedback(int rating, string comment = "")
    {
        if (!Enabled) return;
        FeedbackRow row = new FeedbackRow { runId = lastRunId, playerId = PlayerId(), result = lastResult, rating = rating, comment = comment ?? "" };
        Enqueue(new Payload { feedback = new[] { row } });
    }

    // ---------- Names (what shows in the sheet) ----------

    public static string Name(FaceDefinition face)
    {
        if (face == null) return "";
        return face.type == FaceType.Number ? face.number.ToString() : GameController.OpSymbol(face.op);
    }
    public static string Name(DieDefinition die) => die == null ? "" : (string.IsNullOrEmpty(die.displayName) ? die.name : die.displayName);
    public static string Name(RelicDefinition relic) => relic == null ? "" : (string.IsNullOrEmpty(relic.displayName) ? relic.name : relic.displayName);
    public static string Name(EnemyDefinition enemyDef) => enemyDef == null ? "" : (string.IsNullOrEmpty(enemyDef.displayName) ? enemyDef.name : enemyDef.displayName);

    // ---------- Inside ----------

    private static void OpenRound()
    {
        round = new RoundRow
        {
            runId = run.runId,
            fight = fight.fight,
            round = fightRounds.Count + 1,
            enemy = fight.enemy,
            table = fight.table,
            target = nextTarget
        };
        nextTarget = 0;
        roundStart = Now;
    }

    private static void CloseRound()
    {
        if (round == null) return;
        if (round.hasEquation)   // a round nobody finished an equation in isn't worth a row
        {
            if (round.scored) round.winner = round.diff > 0 ? "player" : round.diff < 0 ? "enemy" : "draw";
            else round.winner = round.damageToEnemy > round.damageToPlayer ? "player"
                              : round.damageToPlayer > round.damageToEnemy ? "enemy" : "draw";
            round.playerHp = player != null ? player.Health : 0;
            round.enemyHp = enemy != null ? enemy.Health : 0;
            round.seconds = Secs(Now - roundStart);
            fightRounds.Add(round);
        }
        round = null;
    }

    private static void CloseFight(string result)
    {
        CloseRound();
        Unwatch();

        fight.result = result;
        fight.rounds = fightRounds.Count;
        foreach (RoundRow r in fightRounds)
        {
            if (r.winner == "player") fight.roundsWon++;
            fight.damageDealt += r.damageToEnemy;
            fight.damageTaken += r.damageToPlayer;
            fight.healed += r.playerHealed;
            fight.playerBusts += r.playerBusts;
            fight.enemyBusts += r.enemyBusts;
            fight.playerThrows += r.playerThrows;
            fight.bonusRethrows += r.bonusRethrows;
        }
        fight.playerHpEnd = player != null ? player.Health : 0;
        fight.seconds = Secs(Now - fightStart);

        run.rounds += fight.rounds;
        run.playerBusts += fight.playerBusts;
        if (result == "won") run.fightsWon++;
        run.playerHp = fight.playerHpEnd;
        if (player != null) run.playerMaxHp = player.MaxHealth;

        Enqueue(new Payload { fights = new[] { fight }, rounds = fightRounds.ToArray() });
        fightRounds.Clear();
        fight = null;
        lastEvent = Now;
        SavePending();
    }

    private static void Finish(string result)
    {
        if (run == null) return;
        if (fight != null) CloseFight(result);
        if (player != null) { run.playerHp = player.Health; run.playerMaxHp = player.MaxHealth; }
        run.result = result;
        run.minutes = Mins(Now - runStart);
        Enqueue(new Payload { runs = new[] { run } });

        lastRunId = run.runId;
        lastResult = result;
        run = null;
        player = null;
        enemy = null;
        PlayerPrefs.DeleteKey(PendingKey);
        PlayerPrefs.Save();
    }

    // The run so far, saved after every step, so a closed tab still leaves a row
    private static void SavePending()
    {
        if (run == null) return;
        run.minutes = Mins(Now - runStart);
        PlayerPrefs.SetString(PendingKey, JsonUtility.ToJson(run));
        PlayerPrefs.Save();
    }

    private static void SendPendingRun()
    {
        string json = PlayerPrefs.GetString(PendingKey, "");
        if (string.IsNullOrEmpty(json)) return;
        PlayerPrefs.DeleteKey(PendingKey);
        RunRow old = JsonUtility.FromJson<RunRow>(json);
        if (old == null) return;
        old.result = "abandoned";
        Enqueue(new Payload { runs = new[] { old } });
    }

    // ---------- Health ----------

    private static void Watch()
    {
        if (player != null)
        {
            lastHp[0] = player.Health; lastMax[0] = player.MaxHealth;
            player.OnHealthChanged += OnPlayerHealth;
        }
        if (enemy != null)
        {
            lastHp[1] = enemy.Health; lastMax[1] = enemy.MaxHealth;
            enemy.OnHealthChanged += OnEnemyHealth;
        }
    }

    private static void Unwatch()
    {
        if (player != null) player.OnHealthChanged -= OnPlayerHealth;
        if (enemy != null) enemy.OnHealthChanged -= OnEnemyHealth;
    }

    private static void OnPlayerHealth(int hp, int max) => HealthChanged(0, hp, max);
    private static void OnEnemyHealth(int hp, int max) => HealthChanged(1, hp, max);

    // Damage and healing go to the open round, so it doesn't matter when GameController applies them
    private static void HealthChanged(int who, int hp, int max)
    {
        int change = hp - lastHp[who];
        bool newMax = max != lastMax[who];   // SetMaxHealthKeepRatio or a reset, not a hit
        lastHp[who] = hp;
        lastMax[who] = max;
        if (round == null || newMax || change == 0) return;

        if (change < 0)
        {
            if (who == 0) round.damageToPlayer -= change;
            else round.damageToEnemy -= change;
        }
        else if (who == 0) round.playerHealed += change;
    }

    // ---------- Sending ----------

    private static void Enqueue(Payload payload)
    {
        if (Application.isEditor && !SendFromEditor) return;
        Ready();
        payload.batch = Guid.NewGuid().ToString("N");
        outbox.items.Add(JsonUtility.ToJson(payload));
        if (!sending) while (outbox.items.Count > MaxOutbox) outbox.items.RemoveAt(0);
        SaveOutbox();
        Flush();
    }

    private static void SaveOutbox()
    {
        PlayerPrefs.SetString(OutboxKey, JsonUtility.ToJson(outbox));
        PlayerPrefs.Save();
    }

    // Sends the oldest batch, then the next one once that got through
    private static void Flush()
    {
        if (sending || outbox == null || outbox.items.Count == 0) return;
        if (Application.isEditor && !SendFromEditor) return;

        sending = true;
        string json = outbox.items[0];
        WWWForm form = new WWWForm();
        form.AddField("data", json);
        UnityWebRequest request = UnityWebRequest.Post(Url, form);
        request.timeout = 30;
        request.SendWebRequest().completed += _ =>
        {
            bool reached = request.result == UnityWebRequest.Result.Success;
            string answer = reached ? request.downloadHandler.text.Trim() : request.error;
            request.Dispose();
            sending = false;
            if (outbox == null) return;   // play mode ended while it was sending

            if (answer == "ok" || answer == "dup")
            {
                badAnswers = 0;
                outbox.items.Remove(json);
                SaveOutbox();
                Flush();
            }
            else if (reached && ++badAnswers >= 3)
            {
                // The script got it but answered with an error 3 times: drop it so it doesn't block the rest
                Debug.LogWarning("Telemetry: the sheet keeps rejecting a batch, dropping it. Answer: " + Short(answer));
                badAnswers = 0;
                outbox.items.Remove(json);
                SaveOutbox();
                Flush();
            }
            else
            {
                // Offline or the request failed: it stays saved and goes out with the next batch or next launch
                Debug.LogWarning("Telemetry: couldn't send (" + Short(answer) + "), will retry later");
            }
        };
    }

    // ---------- Small helpers ----------

    private static string PlayerId()
    {
        string id = PlayerPrefs.GetString(PlayerKey, "");
        if (id.Length == 0)
        {
            id = NewId();
            PlayerPrefs.SetString(PlayerKey, id);
        }
        return id;
    }

    private static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 10);
    private static float Secs(float s) => Mathf.Round(s * 10f) / 10f;
    private static float Mins(float s) => Mathf.Round(s / 6f) / 10f;
    private static string Short(string s) => s == null ? "" : (s.Length > 120 ? s.Substring(0, 120) : s);
}
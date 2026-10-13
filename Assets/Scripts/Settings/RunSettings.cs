using System.Collections.Generic;
using UnityEngine;

// The choices made in the menu, carried into the game scene. Static fields survive a scene load.
// Nothing chosen (you pressed Play in the game scene): everything falls back to what the scene already has.
public static class RunSettings
{
    public static DifficultyDefinition Difficulty;
    public static RaiseTable RaiseTable;   // MainMenu hands these over with the difficulty
    public static int Raise;               // 0 = no Raise

    public static float EnemyHpMultiplier => (Difficulty != null ? Difficulty.enemyHpMultiplier : 1f) * RaiseEnemyHp;
    public static string Name => Difficulty != null ? Difficulty.telemetryName : "Normal";
    public static string Label => Raise > 0 ? $"{Name} R{Raise}" : Name;   // for the telemetry sheet: "Normal R3"
    public static bool LeftToRight => Difficulty != null && Difficulty.leftToRight;

    // The faces this run started with, as text ("+ 2 3 4 5 6 | + + + + - 6 | ..."), for telemetry
    public static string StartingFaces { get; private set; } = "";

    // The dice the player starts with: the difficulty's, else the scene's own (PlayerController's Die list).
    // With Random Mixed Faces on, every call deals new faces, so call it once per run (PlayerController.Start).
    public static DieDefinition[] StartingDice(DieDefinition[] sceneDefault)
    {
        DieDefinition[] dice = Difficulty != null && Difficulty.startingDice != null && Difficulty.startingDice.Length > 0
            ? Difficulty.startingDice : sceneDefault;
        if (Difficulty != null && Difficulty.randomMixedFaces) dice = RandomizeMixedFaces(dice, Difficulty);
        RaiseLevel badBeat = FirstActive(RaiseRule.BadBeat);
        if (badBeat != null && badBeat.face != null) dice = BadBeat(dice, badBeat.face);
        StartingFaces = Describe(dice);
        return dice;
    }

    // ---------- Raise ----------

    // The levels in play: 1 to Raise
    private static System.Collections.Generic.IEnumerable<RaiseLevel> Active()
    {
        if (RaiseTable == null) yield break;
        for (int n = 1; n <= Mathf.Min(Raise, RaiseTable.Count); n++)
        {
            RaiseLevel level = RaiseTable.Level(n);
            if (level != null) yield return level;
        }
    }

    private static RaiseLevel FirstActive(RaiseRule rule)
    {
        foreach (RaiseLevel l in Active()) if (l.rule == rule) return l;
        return null;
    }

    private static int Sum(RaiseRule rule)
    {
        int total = 0;
        foreach (RaiseLevel l in Active()) if (l.rule == rule) total += l.amount;
        return total;
    }

    public static float RaiseEnemyHp => 1f + Sum(RaiseRule.EnemyHp) / 100f;
    public static int RaiseStartingHpPercent => -Sum(RaiseRule.StartingMaxHp);   // PlayerController's starting HpBonusPercent
    public static int RaiseBossThrows => Sum(RaiseRule.BossThrows);
    public static int RaiseBustExtra => Sum(RaiseRule.BustCost);
    public static float RaiseBossHp => 1f + Sum(RaiseRule.BossHp) / 100f;
    public static int RaiseLowCapExtra => Sum(RaiseRule.LowCap);

    public static float RaiseMaxHpRewardMultiplier
    {
        get
        {
            float m = 1f;
            foreach (RaiseLevel l in Active()) if (l.rule == RaiseRule.MaxHpReward) m *= l.amount / 100f;
            return m;
        }
    }

    public static float RaiseHealMultiplier
    {
        get
        {
            float m = 1f;
            foreach (RaiseLevel l in Active()) if (l.rule == RaiseRule.HealReward) m *= l.amount / 100f;
            return m;
        }
    }

    // How many relics a relic screen shows (the default unless a level changes it)
    public static int RelicChoices(int normal)
    {
        RaiseLevel l = FirstActive(RaiseRule.RelicChoices);
        return l != null ? Mathf.Max(1, l.amount) : normal;
    }

    // How much of the missing HP the heal after an act boss gives back, in %
    public static int RaiseBossHealPercent
    {
        get
        {
            RaiseLevel l = FirstActive(RaiseRule.BossHeal);
            return l != null ? Mathf.Clamp(l.amount, 0, 100) : 100;
        }
    }

    // An enemy's traits for this fight: its own, plus any a Raise level gives every enemy in this act
    public static EnemyTrait[] TraitsFor(EnemyTrait[] own, int act)
    {
        List<EnemyTrait> traits = new List<EnemyTrait>();
        if (own != null) foreach (EnemyTrait t in own) if (t != null && !traits.Contains(t)) traits.Add(t);
        foreach (RaiseLevel l in Active())
            if (l.rule == RaiseRule.ActTrait && l.trait != null && act >= l.amount && !traits.Contains(l.trait)) traits.Add(l.trait);
        return traits.ToArray();
    }

    // Bad Beat: the first number die's best number becomes 'face' (a copy of the die, for this run only)
    private static DieDefinition[] BadBeat(DieDefinition[] dice, FaceDefinition face)
    {
        DieDefinition[] result = (DieDefinition[])dice.Clone();
        for (int k = 0; k < result.Length; k++)
        {
            DieDefinition src = result[k];
            if (src == null || IsOperatorDie(src)) continue;
            FaceDefinition[] faces = (FaceDefinition[])src.faceDefinitions.Clone();
            int best = -1;
            for (int i = 0; i < faces.Length; i++)
                if (faces[i] != null && faces[i].type == FaceType.Number && (best < 0 || faces[i].number > faces[best].number)) best = i;
            if (best < 0) continue;
            faces[best] = face;
            DieDefinition copy = Object.Instantiate(src);
            copy.name = src.name + "_BadBeat";
            copy.hideFlags = HideFlags.DontUnloadUnusedAsset;
            copy.faceDefinitions = faces;
            copy.role = DieRole.Number;
            result[k] = copy;
            break;   // only the first number die
        }
        return result;
    }

    // The highest Raise you can pick for this difficulty (0 until you've cleared it once; a clear from before Raise
    // existed counts too, so your playtesters who already won get Raise 1)
    public static int HighestRaise(DifficultyDefinition d) =>
        d == null ? 0 : Mathf.Max(PlayerPrefs.GetInt("Raise_" + d.telemetryName, 0), HasCleared(d) ? 1 : 0);

    // The Raise you played this difficulty at last time, so the menu starts there
    public static int LastRaise(DifficultyDefinition d) => d == null ? 0 : Mathf.Min(PlayerPrefs.GetInt("LastRaise_" + d.telemetryName, 0), HighestRaise(d));

    public static void RememberRaise(DifficultyDefinition d, int raise)
    {
        if (d == null) return;
        PlayerPrefs.SetInt("LastRaise_" + d.telemetryName, raise);
        PlayerPrefs.Save();
    }

    // The boss dice for this run: the difficulty's own pool if it has one, else the scene's
    public static BossDicePool BossDice(BossDicePool sceneDefault)
    {
        return Difficulty != null && Difficulty.bossDice != null ? Difficulty.bossDice : sceneDefault;
    }

    // ---------- Random mixed faces ----------

    // One face that was drawn: which die's face list, and where in it
    private class Drawn
    {
        public FaceDefinition[] faces;
        public int index;
    }

    private static DieDefinition[] RandomizeMixedFaces(DieDefinition[] source, DifficultyDefinition d)
    {
        DieDefinition[] result = new DieDefinition[source.Length];
        List<List<Drawn>> perDie = new List<List<Drawn>>();

        for (int k = 0; k < source.Length; k++)
        {
            DieDefinition src = source[k];
            List<Drawn> drawn = new List<Drawn>();
            perDie.Add(drawn);
            if (src == null) continue;

            bool operatorDie = IsOperatorDie(src);
            FaceDefinition[] faces = (FaceDefinition[])src.faceDefinitions.Clone();
            for (int i = 0; i < faces.Length; i++)
            {
                FaceDefinition f = faces[i];
                if (f == null) continue;
                bool isOperator = f.type == FaceType.Operator;
                if (isOperator == operatorDie) continue;                  // a face of the die's own kind stays
                FaceDefinition[] list = isOperator ? d.randomOperators : d.randomNumbers;
                if (list == null || list.Length == 0) continue;
                faces[i] = list[Random.Range(0, list.Length)];
                if (isOperator) drawn.Add(new Drawn { faces = faces, index = i });
            }

            // A copy for this run only. Instantiate copies every field (prefab, icon...), then the faces and the
            // role are set. The role is always explicit, so a 3/3 die can't change sides when its faces change.
            DieDefinition copy = Object.Instantiate(src);
            copy.name = src.name + "_ThisRun";
            copy.hideFlags = HideFlags.DontUnloadUnusedAsset;
            copy.faceDefinitions = faces;
            copy.role = operatorDie ? DieRole.Operator : DieRole.Number;
            result[k] = copy;
        }

        // The + guarantee
        FaceDefinition plus = FindPlus(d.randomOperators);
        if (plus != null && d.minimumPlus > 0)
        {
            if (d.plusOnEachDie)
            {
                foreach (List<Drawn> drawn in perDie) Guarantee(drawn, plus, d.minimumPlus);
            }
            else
            {
                List<Drawn> all = new List<Drawn>();
                foreach (List<Drawn> drawn in perDie) all.AddRange(drawn);
                Guarantee(all, plus, d.minimumPlus);
            }
        }
        return result;
    }

    // Turns drawn faces into + (picked at random) until at least 'minimum' of them are a +
    private static void Guarantee(List<Drawn> drawn, FaceDefinition plus, int minimum)
    {
        int pluses = 0;
        List<Drawn> others = new List<Drawn>();
        foreach (Drawn x in drawn)
        {
            if (x.faces[x.index] == plus) pluses++;
            else others.Add(x);
        }
        while (pluses < minimum && others.Count > 0)
        {
            int pick = Random.Range(0, others.Count);
            Drawn x = others[pick];
            others.RemoveAt(pick);
            x.faces[x.index] = plus;
            pluses++;
        }
    }

    private static FaceDefinition FindPlus(FaceDefinition[] list)
    {
        if (list == null) return null;
        foreach (FaceDefinition f in list)
            if (f != null && f.type == FaceType.Operator && f.op == Operator.Add) return f;
        return null;
    }

    // Same rule as PlayerController.IsNumberDie: the Role if it's set, else whichever kind most of its faces are
    public static bool IsOperatorDie(DieDefinition def)
    {
        if (def.role == DieRole.Operator) return true;
        if (def.role == DieRole.Number) return false;
        int numbers = 0, operators = 0;
        foreach (FaceDefinition f in def.faceDefinitions)
        {
            if (f == null) continue;
            if (f.type == FaceType.Number) numbers++; else operators++;
        }
        return operators > numbers;
    }

    private static string Describe(DieDefinition[] dice)
    {
        List<string> parts = new List<string>();
        foreach (DieDefinition def in dice)
        {
            if (def == null) continue;
            List<string> faces = new List<string>();
            foreach (FaceDefinition f in def.faceDefinitions)
            {
                if (f == null) continue;
                faces.Add(f.type == FaceType.Number ? f.number.ToString() : GameController.OpSymbol(f.op));
            }
            parts.Add(string.Join(" ", faces));
        }
        return string.Join(" | ", parts);
    }

    // ---------- Unlocks (saved on this computer) ----------

    private const string CustomKey = "CustomUnlocked";

    public static bool CustomUnlocked => PlayerPrefs.GetInt(CustomKey, 0) == 1;

    // GameController calls this when a run is won: custom mode opens up, this difficulty gets its tick, and the next
    // Raise opens for it. Returns that newly opened Raise level (0 if none: already open, or no more levels).
    public static int RecordClear()
    {
        int before = HighestRaise(Difficulty);   // before this clear counts
        PlayerPrefs.SetInt(CustomKey, 1);
        PlayerPrefs.SetInt("Cleared_" + Name, 1);
        int opened = 0;
        if (Difficulty != null && RaiseTable != null)
        {
            int next = Raise + 1;
            if (next <= RaiseTable.Count && next > before)   // at the top level there's nothing left to open
            {
                PlayerPrefs.SetInt("Raise_" + Difficulty.telemetryName, next);
                opened = next;
            }
        }
        PlayerPrefs.Save();
        return opened;
    }

    public static bool HasCleared(DifficultyDefinition d) => d != null && PlayerPrefs.GetInt("Cleared_" + d.telemetryName, 0) == 1;

    public static void ResetUnlocks()   // for testing (MainMenu's right-click menu)
    {
        PlayerPrefs.DeleteKey(CustomKey);
        PlayerPrefs.Save();
    }

    public static void SetHighestRaise(DifficultyDefinition d, int level)   // for testing (MainMenu's right-click menu)
    {
        if (d == null) return;
        PlayerPrefs.SetInt("Raise_" + d.telemetryName, Mathf.Max(0, level));
        if (level == 0) PlayerPrefs.DeleteKey("Cleared_" + d.telemetryName);   // or the old clear would open Raise 1 again
        PlayerPrefs.Save();
    }
}

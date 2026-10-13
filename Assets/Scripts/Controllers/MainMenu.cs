using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The start menu. PLAY opens the difficulty screen (Easy, Normal, Hard, Custom, Back).
// Custom stays locked until you've cleared a full run.
// Raise: once you've cleared a difficulty, picking it opens the Raise screen first (RAISE 2  < >, then PLAY or BACK).
// Every clear opens the next Raise level for that difficulty. Until then, picking a difficulty starts the run as before.
public class MainMenu : MonoBehaviour
{
    [SerializeField] private string gameScene = "Game";
    [SerializeField] private DifficultyDefinition defaultDifficulty;    // what QuickPlay() starts
    [SerializeField] private TMP_Text descriptionText;                  // optional
    [SerializeField] private Button customButton;                       // optional (a real UI Button only)
    [SerializeField] private TMP_Text customLabel;
    [SerializeField] private GameObject mainPanel;                      // PLAY / QUIT
    [SerializeField] private GameObject difficultyPanel;                // EASY / NORMAL / HARD / CUSTOM / BACK
    [SerializeField] private GameObject customPanel;                    // the custom builder
    [SerializeField] private ThrowStylePicker throwPicker;              // asks how to throw on the first launch

    [Header("Raise")]
    [SerializeField] private RaiseTable raiseTable;                     // the levels. Empty = no Raise at all
    [SerializeField] private GameObject raisePanel;                     // RAISE n, the arrows, PLAY, BACK
    [SerializeField] private TMP_Text raiseTitle;                       // "NORMAL · RAISE 3"
    [SerializeField] private TMP_Text raiseNewest;                      // the newest rule: "NO COMPS: HEALS ARE A QUARTER SMALLER"
    [SerializeField] private TMP_Text raiseAll;                         // optional: every rule in play, "HOUSE EDGE · SHORT STACK · NO COMPS"

    private DifficultyDefinition raiseFor;   // the difficulty the Raise screen is open for
    private int raise;                       // the level showing on it

    private void Start()
    {
        bool unlocked = RunSettings.CustomUnlocked;
        if (customButton != null) customButton.interactable = unlocked;
        if (customLabel != null) customLabel.text = unlocked ? "CUSTOM" : "CUSTOM (LOCKED)";
        Show(mainPanel);
        Describe(null);
        if (throwPicker != null) throwPicker.OpenIfFirstTime();
    }

    // Only one panel at a time
    private void Show(GameObject panel)
    {
        if (mainPanel != null) mainPanel.SetActive(panel == mainPanel);
        if (difficultyPanel != null) difficultyPanel.SetActive(panel == difficultyPanel);
        if (customPanel != null) customPanel.SetActive(panel == customPanel);
        if (raisePanel != null) raisePanel.SetActive(panel == raisePanel);
    }

    public void Play() => Show(difficultyPanel);                        // your PLAY button
    public void BackToMain() => Show(mainPanel);
    public void QuickPlay() => StartRun(defaultDifficulty, 0);          // straight into the default difficulty, no Raise
    public void ClearDescription() => Describe(null);

    // Your EASY / NORMAL / HARD buttons. Cleared it before? The Raise screen comes first.
    public void PlayDifficulty(DifficultyDefinition difficulty)
    {
        if (raiseTable == null || raisePanel == null || RunSettings.HighestRaise(difficulty) == 0)
        {
            StartRun(difficulty, 0);
            return;
        }
        raiseFor = difficulty;
        raise = RunSettings.LastRaise(difficulty);
        Show(raisePanel);
        ShowRaise();
    }

    // ---------- The Raise screen (point its buttons at these) ----------

    public void RaiseUp() { raise = Mathf.Min(raise + 1, RunSettings.HighestRaise(raiseFor)); ShowRaise(); }
    public void RaiseDown() { raise = Mathf.Max(raise - 1, 0); ShowRaise(); }
    public void PlayRaise() => StartRun(raiseFor, raise);
    public void CloseRaise() => Show(difficultyPanel);

    private void ShowRaise()
    {
        int highest = RunSettings.HighestRaise(raiseFor);
        string name = raiseFor != null ? raiseFor.displayName : "";
        if (raiseTitle != null) raiseTitle.text = raise == 0 ? $"{name} · NO RAISE" : $"{name} · RAISE {raise} OF {highest}";

        RaiseLevel newest = raiseTable.Level(raise);
        if (raiseNewest != null)
            raiseNewest.text = newest == null ? "THE GAME AS IT COMES" : $"{newest.displayName}: {newest.line}";

        if (raiseAll != null)
        {
            List<string> names = new List<string>();
            for (int n = 1; n < raise; n++)
            {
                RaiseLevel l = raiseTable.Level(n);
                if (l != null) names.Add(l.displayName);
            }
            raiseAll.text = names.Count == 0 ? "" : "AND " + string.Join(" · ", names);
        }
    }

    private void StartRun(DifficultyDefinition difficulty, int raiseLevel)
    {
        RunSettings.Difficulty = difficulty;
        RunSettings.RaiseTable = raiseTable;
        RunSettings.Raise = raiseLevel;
        RunSettings.RememberRaise(difficulty, raiseLevel);
        SceneManager.LoadScene(gameScene);
    }

    public void Describe(DifficultyDefinition difficulty)
    {
        if (descriptionText == null) return;
        if (difficulty == null) { descriptionText.text = ""; return; }
        int highest = RunSettings.HighestRaise(difficulty);
        string cleared = highest > 0 ? $"  RAISE {highest} OPEN" : RunSettings.HasCleared(difficulty) ? "  CLEARED" : "";
        descriptionText.text = difficulty.description + cleared;
        descriptionText.color = difficulty.color;
    }

    public void OpenCustom()
    {
        if (!RunSettings.CustomUnlocked) return;    // the locked button does nothing
        Show(customPanel);
    }

    public void CloseCustom() => Show(difficultyPanel);

    public void Quit() => Application.Quit();

    [ContextMenu("Unlock Custom (testing)")]
    private void UnlockForTesting() { PlayerPrefs.SetInt("CustomUnlocked", 1); PlayerPrefs.Save(); Start(); }

    [ContextMenu("Lock Custom again (testing)")]
    private void LockForTesting() { RunSettings.ResetUnlocks(); Start(); }

    [ContextMenu("Open every Raise for the default difficulty (testing)")]
    private void OpenRaiseForTesting() { RunSettings.SetHighestRaise(defaultDifficulty, raiseTable != null ? raiseTable.Count : 0); }

    [ContextMenu("Close every Raise for the default difficulty (testing)")]
    private void CloseRaiseForTesting() { RunSettings.SetHighestRaise(defaultDifficulty, 0); }
}
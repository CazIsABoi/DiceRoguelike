using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The settings / pause screen. TAB opens and closes it (not Escape: in a WebGL build Escape leaves fullscreen).
// Middle: volumes, throw style, resume, main menu. Left: your dice and every face on them. Right: what each operator does.
// Works in both scenes: in the menu there's no run, so the dice list and the Main Menu button hide themselves.
public class SettingsMenu : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    [SerializeField] private GameObject panel;                 // the full-screen panel, inactive to start with
    [SerializeField] private Key toggleKey = Key.Tab;
    [SerializeField] private bool pauseWhileOpen = true;      // in the game scene: stops time while it's open

    [Header("Audio")]
    [SerializeField] private AudioMixer mixer;                 // optional: needed for Music and Sound Effects
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Slider diceSlider;

    [Header("Buttons")]
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button mainMenuButton;
    [SerializeField] private Button throwStyleButton;
    [SerializeField] private TMP_Text throwStyleLabel;         // the button's text: "THROW: DRAG" / "THROW: CLICK"

    [Header("Your Dice (left)")]
    [SerializeField] private GameObject diceSection;          // the whole left column, hidden in the menu scene
    [SerializeField] private Transform diceList;               // a Vertical Layout Group
    [SerializeField] private DieShowcaseRow rowPrefab;
    [SerializeField] private FaceCell faceCellPrefab;          // the same prefab as the custom builder
    [SerializeField] private Color numberColor = Color.white;
    [SerializeField] private Color operatorColor = new Color32(0xFF, 0x71, 0x34, 0xFF);
    [SerializeField] private int fitColumns = 6;              // how many cells fit across at the prefab's cell size
    [SerializeField] private int maxColumns = 9;              // a die with more different faces than this wraps to a second line
    [SerializeField] private float countSize = 0.4f;          // the "×3" in a cell's corner, as a share of the cell's height

    [Header("Operators (right)")]
    [SerializeField] private TMP_Text orderText;               // one line under the guide: the order sums are worked out in

    private float timeScaleBefore = 1f;

    private void Awake()
    {
        GameSettings.Mixer = mixer;
        GameSettings.Apply();

        Hook(masterSlider, v => GameSettings.Master = v);
        Hook(musicSlider, v => GameSettings.Music = v);
        Hook(sfxSlider, v => GameSettings.Sfx = v);
        Hook(diceSlider, v => GameSettings.Dice = v);
        if (resumeButton != null) resumeButton.onClick.AddListener(Close);
        if (mainMenuButton != null) mainMenuButton.onClick.AddListener(ToMainMenu);
        if (throwStyleButton != null) throwStyleButton.onClick.AddListener(SwapThrowStyle);

        IsOpen = false;
        if (panel != null) panel.SetActive(false);
    }

    private static void Hook(Slider slider, UnityEngine.Events.UnityAction<float> onChange)
    {
        if (slider == null) return;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.onValueChanged.AddListener(onChange);
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
        {
            if (IsOpen) Close(); else Open();
        }
    }

    public void Toggle() { if (IsOpen) Close(); else Open(); }   // for an on-screen gear button

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        panel.SetActive(true);

        if (masterSlider != null) masterSlider.SetValueWithoutNotify(GameSettings.Master);
        if (musicSlider != null) musicSlider.SetValueWithoutNotify(GameSettings.Music);
        if (sfxSlider != null) sfxSlider.SetValueWithoutNotify(GameSettings.Sfx);
        if (diceSlider != null) diceSlider.SetValueWithoutNotify(GameSettings.Dice);
        ShowThrowStyle();

        GameController game = FindAnyObjectByType<GameController>();
        if (mainMenuButton != null) mainMenuButton.gameObject.SetActive(game != null);
        if (orderText != null)
            orderText.text = RunSettings.LeftToRight ? "THIS RUN: LEFT TO RIGHT, IN THE ORDER THEY SIT"
                                                     : "× ÷ % FIRST, THEN + −";
        BuildDice();

        if (pauseWhileOpen && game != null)
        {
            timeScaleBefore = Time.timeScale;
            Time.timeScale = 0f;
        }
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        panel.SetActive(false);
        if (pauseWhileOpen && Time.timeScale == 0f) Time.timeScale = timeScaleBefore > 0f ? timeScaleBefore : 1f;
    }

    public void ToMainMenu()
    {
        Time.timeScale = 1f;
        IsOpen = false;
        GameController game = FindAnyObjectByType<GameController>();
        if (game != null) game.ToMenu();                // records the abandoned run in telemetry, then loads MainMenu
        else SceneManager.LoadScene("MainMenu");
    }

    private void OnDestroy()
    {
        if (IsOpen) Time.timeScale = 1f;                // never leave the next scene frozen
        IsOpen = false;
    }

    // ---------- Throw style ----------

    private void SwapThrowStyle()
    {
        ThrowStyle.Current = ThrowStyle.ClickToThrow ? ThrowMode.Drag : ThrowMode.Click;
        ShowThrowStyle();
    }

    private void ShowThrowStyle()
    {
        if (throwStyleLabel != null) throwStyleLabel.text = ThrowStyle.ClickToThrow ? "THROW: CLICK" : "THROW: DRAG";
    }

    // ---------- Your dice ----------

    private readonly List<GameObject> rows = new List<GameObject>();

    private void BuildDice()
    {
        foreach (GameObject r in rows) Destroy(r);
        rows.Clear();

        PlayerController player = FindAnyObjectByType<PlayerController>();
        bool show = player != null && player.GetDice() != null && player.GetDice().Length > 0;
        if (diceSection != null) diceSection.SetActive(show);
        if (!show) return;

        foreach (Die d in player.GetDice())
        {
            if (d == null) continue;
            FaceDefinition[] faces = d.GetCurrentFaces();
            DieShowcaseRow row = Instantiate(rowPrefab, diceList);
            rows.Add(row.gameObject);
            row.label.text = (IsOperatorDie(d, faces) ? "OPERATOR DIE" : "NUMBER DIE") + " · D" + faces.Length;

            // Each different face once, with "×3" in the corner when the die has it more than once.
            // A Golden D20 (1 to 9, three 9s) is 9 cells instead of 20.
            List<FaceDefinition> kinds = new List<FaceDefinition>();
            List<int> counts = new List<int>();
            foreach (FaceDefinition f in faces)
            {
                int k = kinds.FindIndex(x => SameFace(x, f));
                if (k < 0) { kinds.Add(f); counts.Add(1); } else counts[k]++;
            }

            float cellHeight = FitRow(row.faces, kinds.Count);
            for (int k = 0; k < kinds.Count; k++)
            {
                FaceDefinition f = kinds[k];
                FaceCell cell = Instantiate(faceCellPrefab, row.faces);
                if (f == null) cell.Show(null, "", numberColor);   // a blank side
                else
                {
                    bool op = f.type == FaceType.Operator;
                    cell.Show(f.icon, op ? GameController.OpSymbol(f.op) : f.number.ToString(), op ? operatorColor : numberColor);
                }
                if (counts[k] > 1) AddCount(cell, row.label, counts[k], cellHeight);
            }
        }
    }

    private static bool SameFace(FaceDefinition a, FaceDefinition b)
    {
        if (a == null || b == null) return a == b;
        if (a.type != b.type) return false;
        return a.type == FaceType.Number ? a.number == b.number : a.op == b.op;
    }

    // More than fitColumns different faces: the cells shrink so up to maxColumns fit on one line
    // Returns the cell height (0 if the faces have no Grid Layout Group)
    private float FitRow(Transform facesParent, int cells)
    {
        GridLayoutGroup grid = facesParent.GetComponent<GridLayoutGroup>();
        if (grid == null) return 0f;
        if (!gridSizes.ContainsKey(grid)) gridSizes[grid] = grid.cellSize;   // the prefab's size, before any shrinking
        Vector2 baseSize = gridSizes[grid];
        int columns = Mathf.Clamp(cells, 1, Mathf.Max(fitColumns, maxColumns));
        float width = baseSize.x * fitColumns + grid.spacing.x * (fitColumns - 1);
        float cell = columns <= fitColumns ? baseSize.x : (width - grid.spacing.x * (columns - 1)) / columns;
        grid.cellSize = new Vector2(cell, cell * baseSize.y / baseSize.x);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Mathf.Max(fitColumns, columns);
        return grid.cellSize.y;
    }

    private readonly Dictionary<GridLayoutGroup, Vector2> gridSizes = new Dictionary<GridLayoutGroup, Vector2>();

    // A small "×3" in the bottom right of a cell (a copy of the row's label font)
    private void AddCount(FaceCell cell, TMP_Text fontFrom, int count, float cellHeight)
    {
        TMP_Text t = Instantiate(fontFrom, cell.transform);
        t.text = "×" + count;
        t.raycastTarget = false;
        t.enableAutoSizing = false;
        t.alignment = TextAlignmentOptions.BottomRight;
        t.color = numberColor;
        RectTransform rt = t.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        LayoutElement le = t.GetComponent<LayoutElement>() ?? t.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        t.fontSize = cellHeight > 0f ? Mathf.Max(8f, cellHeight * countSize) : t.fontSize * 0.6f;
    }

    // The same rule as PlayerController.IsNumberDie: its Role if set, else whichever kind most faces are
    private static bool IsOperatorDie(Die d, FaceDefinition[] faces)
    {
        if (d.Role == DieRole.Operator) return true;
        if (d.Role == DieRole.Number) return false;
        int ops = 0, nums = 0;
        foreach (FaceDefinition f in faces)
        {
            if (f == null) continue;
            if (f.type == FaceType.Operator) ops++; else nums++;
        }
        return ops > nums;
    }
}
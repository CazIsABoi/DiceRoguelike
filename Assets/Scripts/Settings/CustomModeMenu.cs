using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The custom run builder, unlocked by clearing a full run. Players edit the six faces of each of their three
// starting dice (left click a face for the next one, right click for the one before), say which die is the
// operator die, and set enemy HP and the maths order. The setup is saved, so it's still there next time.
//
// SETUP (on the Custom Panel):
// - Die Rows: three empty objects with a Horizontal Layout Group. The menu fills each with six FaceCells.
// - Face Cell Prefab: a small UI Image with a FaceCell component and a TMP text child.
// - Role Buttons: one Button per row; its TMP child gets "NUMBER DIE" or "OPERATOR DIE". Clicking flips it.
// - All Faces: every FaceDefinition players can pick, in the order a click cycles through (0 to 9, then + - × ÷ %).
// - Template Die: Die_D6. The custom dice reuse its prefab and icon.
// - Presets: your Easy, Normal and Hard assets. Buttons calling UsePreset(0), UsePreset(1), UsePreset(2) copy their dice.
// - Enemy Hp Slider (min 0.5, max 2) with Enemy Hp Label, Left To Right Toggle, Start Button, Warning Text.
// - A Back button: OnClick > MainMenu.CloseCustom.
public class CustomModeMenu : MonoBehaviour
{
    [SerializeField] private string gameScene = "Game";
    [SerializeField] private Transform[] dieRows = new Transform[3];
    [SerializeField] private FaceCell faceCellPrefab;
    [SerializeField] private Button[] roleButtons = new Button[3];
    [SerializeField] private FaceDefinition[] allFaces;
    [SerializeField] private DieDefinition templateDie;
    [SerializeField] private DifficultyDefinition[] presets;
    [SerializeField] private Slider enemyHpSlider;
    [SerializeField] private TMP_Text enemyHpLabel;
    [SerializeField] private Toggle leftToRightToggle;
    [SerializeField] private Button startButton;
    [SerializeField] private TMP_Text warningText;
    [SerializeField] private Color numberColor = Color.white;
    [SerializeField] private Color operatorColor = new Color32(0xFF, 0x71, 0x34, 0xFF);

    private const int Dice = 3;
    private const int Sides = 6;
    private const string SaveKey = "CustomSetup";

    // What gets saved. Faces are positions in All Faces, die by die (six for die 0, six for die 1...)
    [System.Serializable]
    private class Setup
    {
        public int[] faces = new int[Dice * Sides];
        public bool[] operatorDie = { false, true, false };
        public float enemyHp = 1f;
        public bool leftToRight;
    }

    private Setup setup;
    private readonly List<FaceCell> cells = new List<FaceCell>();
    private bool refreshing;    // stops the slider and toggle from reacting to our own changes

    private void OnEnable()
    {
        if (cells.Count == 0) Build();
        Load();
        Refresh();
    }

    // ---------- Building the panel ----------

    private void Build()
    {
        for (int d = 0; d < Dice; d++)
        {
            for (int i = 0; i < Sides; i++)
            {
                FaceCell cell = Instantiate(faceCellPrefab, dieRows[d]);
                int index = d * Sides + i;               // a copy for the lambda: each cell steps its own face
                cell.OnStep = step => StepFace(index, step);
                cells.Add(cell);
            }
            int die = d;
            roleButtons[d].onClick.AddListener(() => ToggleRole(die));
        }
        enemyHpSlider.onValueChanged.AddListener(v =>
        {
            if (refreshing) return;
            setup.enemyHp = Mathf.Round(v * 20f) / 20f;   // steps of 5%
            Changed();
        });
        leftToRightToggle.onValueChanged.AddListener(on =>
        {
            if (refreshing) return;
            setup.leftToRight = on;
            Changed();
        });
        startButton.onClick.AddListener(StartRun);
    }

    // ---------- Editing ----------

    private void StepFace(int index, int step)
    {
        int n = allFaces.Length;
        setup.faces[index] = ((setup.faces[index] + step) % n + n) % n;   // wraps around both ways
        Changed();
    }

    private void ToggleRole(int die)
    {
        setup.operatorDie[die] = !setup.operatorDie[die];
        Changed();
    }

    // Copies a difficulty's starting dice into the editor (the first three dice, six faces each)
    public void UsePreset(int presetIndex)
    {
        if (presets == null || presetIndex < 0 || presetIndex >= presets.Length || presets[presetIndex] == null) return;
        DieDefinition[] dice = presets[presetIndex].startingDice;
        for (int d = 0; d < Dice && d < dice.Length; d++)
        {
            FaceDefinition[] faces = dice[d].faceDefinitions;
            int numbers = 0;
            for (int i = 0; i < Sides; i++)
            {
                FaceDefinition f = i < faces.Length ? faces[i] : null;
                setup.faces[d * Sides + i] = Mathf.Max(0, System.Array.IndexOf(allFaces, f));
                if (f != null && f.type == FaceType.Number) numbers++;
            }
            setup.operatorDie[d] = dice[d].role == DieRole.Operator
                                || (dice[d].role == DieRole.Auto && Sides - numbers > numbers);
        }
        setup.enemyHp = presets[presetIndex].enemyHpMultiplier;
        Changed();
    }

    private void Changed()
    {
        Save();
        Refresh();
    }

    // ---------- Showing it ----------

    private void Refresh()
    {
        refreshing = true;
        for (int d = 0; d < Dice; d++)
        {
            for (int i = 0; i < Sides; i++)
            {
                FaceDefinition f = allFaces[setup.faces[d * Sides + i]];
                bool op = f.type == FaceType.Operator;
                cells[d * Sides + i].Show(f.icon, op ? GameController.OpSymbol(f.op) : f.number.ToString(), op ? operatorColor : numberColor);
            }
            TMP_Text label = roleButtons[d].GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = setup.operatorDie[d] ? "OPERATOR DIE" : "NUMBER DIE";
        }
        enemyHpSlider.value = setup.enemyHp;
        enemyHpLabel.text = $"ENEMY HP {Mathf.RoundToInt(setup.enemyHp * 100)}%";
        leftToRightToggle.isOn = setup.leftToRight;

        string problem = Problem();
        warningText.text = problem;
        startButton.interactable = problem == "" || problem.StartsWith("NOTE");
        refreshing = false;
    }

    // Empty when the setup can be played. "NOTE" problems are warnings, the rest block the Start button.
    private string Problem()
    {
        int numberDice = 0, operatorDice = 0;
        bool operatorFace = false;
        for (int d = 0; d < Dice; d++)
        {
            if (setup.operatorDie[d]) operatorDice++; else numberDice++;
            for (int i = 0; i < Sides; i++)
                if (allFaces[setup.faces[d * Sides + i]].type == FaceType.Operator) operatorFace = true;
        }
        if (numberDice < 2) return "YOU NEED AT LEAST 2 NUMBER DICE";
        if (operatorDice == 0)
            return operatorFace ? "NOTE: NO OPERATOR DIE · ONE BIG NUMBER, OPERATOR FACES WILL BUST"
                                : "NOTE: NO OPERATOR DIE · YOUR DICE MAKE ONE BIG NUMBER";
        if (!operatorFace) return "NOTE: NO OPERATOR FACES AT ALL · EVERY SUM WILL BE A +";
        return "";
    }

    // ---------- Saving ----------

    private void Load()
    {
        setup = null;
        string json = PlayerPrefs.GetString(SaveKey, "");
        if (json != "")
        {
            setup = JsonUtility.FromJson<Setup>(json);
            if (setup.faces == null || setup.faces.Length != Dice * Sides || setup.operatorDie == null
                || setup.operatorDie.Length != Dice) setup = null;
            else
                for (int i = 0; i < setup.faces.Length; i++)
                    setup.faces[i] = Mathf.Clamp(setup.faces[i], 0, allFaces.Length - 1);   // in case All Faces changed
        }
        if (setup == null)
        {
            setup = new Setup();
            UsePreset(Mathf.Min(1, (presets != null ? presets.Length : 0) - 1));   // start from Normal
        }
    }

    private void Save()
    {
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(setup));
        PlayerPrefs.Save();
    }

    // ---------- Starting the run ----------

    private void StartRun()
    {
        DieDefinition[] dice = new DieDefinition[Dice];
        for (int d = 0; d < Dice; d++)
        {
            FaceDefinition[] faces = new FaceDefinition[Sides];
            for (int i = 0; i < Sides; i++) faces[i] = allFaces[setup.faces[d * Sides + i]];

            // Made in code, for this run only. DontUnloadUnusedAsset keeps Unity from cleaning it up on the scene load.
            DieDefinition def = ScriptableObject.CreateInstance<DieDefinition>();
            def.name = "Custom_D6_" + (d + 1);
            def.hideFlags = HideFlags.DontUnloadUnusedAsset;
            def.prefab = templateDie.prefab;
            def.displayName = templateDie.displayName;
            def.icon = templateDie.icon;
            def.faceDefinitions = faces;
            def.role = setup.operatorDie[d] ? DieRole.Operator : DieRole.Number;
            dice[d] = def;
        }

        DifficultyDefinition custom = ScriptableObject.CreateInstance<DifficultyDefinition>();
        custom.name = "Difficulty_Custom";
        custom.hideFlags = HideFlags.DontUnloadUnusedAsset;
        custom.displayName = "CUSTOM";
        custom.telemetryName = "Custom";
        custom.startingDice = dice;
        custom.enemyHpMultiplier = setup.enemyHp;
        custom.leftToRight = setup.leftToRight;

        RunSettings.Difficulty = custom;
        SceneManager.LoadScene(gameScene);
    }
}

using UnityEngine;
using UnityEngine.InputSystem;

// F1: the creative panel, for setting up scenarios to test or record. H: hide the HUD (and the panel).
// Runs in the editor and development builds only, unless Allow In Builds is ticked.
public class DevPanel : MonoBehaviour
{
    public static float BaseTimeScale = 1f;   // slow-mo for recording; PlayerController's speed-up returns to this

    [Header("References")]
    [SerializeField] private GameController game;
    [SerializeField] private PlayerController player;
    [SerializeField] private EnemyController enemy;

    [Header("What the panel can hand out")]
    [SerializeField] private RelicDefinition[] relics;
    [SerializeField] private DieDefinition[] dice;      // e.g. Die_D20_Gold: the act-boss dice, for Act 2/3 scenarios
    [SerializeField] private FaceDefinition[] faces;    // faces you can carve onto a die (Face_9, Face_Multiply...)
    public enum RollMode { Normal, Best, Worst }
    public static RollMode EnemyRoll;


    [Header("HUD hider")]
    [SerializeField] private Canvas[] hudCanvases;      // screen-space canvases: relic bar, hint label, tooltips
    [SerializeField] private GameObject[] hudObjects;   // anything else to hide while recording
    [SerializeField] private bool allowInBuilds = false;

    private bool open, hudHidden;
    private string fightText = "1";
    private int dieIndex, sideIndex, faceIndex;
    private Rect window = new Rect(20, 20, 340, 560);
    private string enemyHpText = "9790";
    private Vector2 scroll;
    private bool Allowed => Application.isEditor || Debug.isDebugBuild || allowInBuilds;

    private void Update()
    {
        if (!Allowed || Keyboard.current == null) return;
        if (Keyboard.current.f1Key.wasPressedThisFrame) open = !open;
        if (Keyboard.current.hKey.wasPressedThisFrame) SetHud(!hudHidden);
    }

    private void SetHud(bool hide)
    {
        hudHidden = hide;
        foreach (Canvas c in hudCanvases) if (c != null) c.enabled = !hide;   // enabled, not SetActive: their scripts keep running
        foreach (GameObject g in hudObjects) if (g != null) g.SetActive(!hide);
        Cursor.visible = !hide;
    }

    private void OnGUI()
    {
        if (!Allowed || !open || hudHidden) return;
        window = GUILayout.Window(7331, window, Draw, "CREATIVE PANEL  (F1 close · H hide HUD)");
    }

    private void Draw(int id)
    {
        scroll = GUILayout.BeginScrollView(scroll);

        // ---- Fight ----
        GUILayout.Label($"<b>Fight {game.Fight} · Act {game.Act} · {enemy.DisplayName}</b>", Rich());
        GUILayout.BeginHorizontal();
        fightText = GUILayout.TextField(fightText, GUILayout.Width(50));
        if (GUILayout.Button("Jump to fight") && int.TryParse(fightText, out int n)) game.DebugJumpToFight(n);
        GUILayout.EndHorizontal();

        // ---- Health ----
        GUILayout.Space(8);
        GUILayout.Label($"<b>You {player.Health}/{player.MaxHealth} · Enemy {enemy.Health}</b>", Rich());
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Full HP")) player.Heal(player.MaxHealth - player.Health);
        if (GUILayout.Button("Me at 1 HP")) player.TakeDamage(player.Health - 1);
        if (GUILayout.Button("Enemy at 1 HP")) enemy.TakeDamage(enemy.Health - 1);
        GUILayout.BeginHorizontal();
        enemyHpText = GUILayout.TextField(enemyHpText, GUILayout.Width(90));
        if (GUILayout.Button("Set enemy HP") && int.TryParse(enemyHpText, out int hp) && hp > 0) enemy.DebugSetHealth(hp);
        GUILayout.EndHorizontal();
        GUILayout.EndHorizontal();

        // ---- Relics and dice ----
        GUILayout.Space(8);
        GUILayout.Label("<b>Give relic</b>", Rich());
        foreach (RelicDefinition r in relics)
        {
            if (r == null || player.HasRelic(r.type)) continue;
            if (GUILayout.Button(r.displayName)) player.AddRelic(r);
        }
        GUILayout.Label("<b>Give die</b> (one more digit, ×10 max HP)", Rich());
        foreach (DieDefinition d in dice)
        {
            if (d == null) continue;
            if (GUILayout.Button(d.name)) { player.AddDie(d); player.Heal(player.MaxHealth); }
        }

        // ---- Faces ----
        GUILayout.Space(8);
        Die[] mine = player.GetDice();
        if (mine.Length > 0)
        {
            dieIndex = Mathf.Clamp(dieIndex, 0, mine.Length - 1);
            GUILayout.Label("<b>Die</b>", Rich());
            string[] dieNames = new string[mine.Length];
            for (int i = 0; i < mine.Length; i++) dieNames[i] = $"{i + 1}";
            dieIndex = GUILayout.Toolbar(dieIndex, dieNames);

            Die die = mine[dieIndex];
            FaceDefinition[] current = die.GetCurrentFaces();
            sideIndex = Mathf.Clamp(sideIndex, 0, current.Length - 1);
            GUILayout.Label("<b>Side</b>", Rich());
            string[] sideNames = new string[current.Length];
            for (int i = 0; i < current.Length; i++) sideNames[i] = Label(current[i]);
            sideIndex = GUILayout.SelectionGrid(sideIndex, sideNames, 6);

            if (GUILayout.Button("Turn this side to the top")) { TurnToTop(die, sideIndex); player.NotifyDiceChanged(); }

            if (faces.Length > 0)
            {
                faceIndex = Mathf.Clamp(faceIndex, 0, faces.Length - 1);
                string[] faceNames = new string[faces.Length];
                for (int i = 0; i < faces.Length; i++) faceNames[i] = Label(faces[i]);
                faceIndex = GUILayout.SelectionGrid(faceIndex, faceNames, 6);
                if (GUILayout.Button($"Carve {faceNames[faceIndex]} onto that side"))
                    StartCoroutine(CarveThenNotify(die, sideIndex, faces[faceIndex]));
            }
        }

        // ---- Recording ----
        GUILayout.Space(8);
        GUILayout.Label($"<b>Time scale {BaseTimeScale:0.00}</b> (slow-mo for clips)", Rich());
        GUILayout.Label("<b>Enemy rolls</b>", Rich());
        EnemyRoll = (RollMode)GUILayout.Toolbar((int)EnemyRoll, new[] { "Normal", "Best", "Worst" });
        BaseTimeScale = GUILayout.HorizontalSlider(BaseTimeScale, 0.1f, 1f);
        if (Time.timeScale > 0f && !Keyboard.current.shiftKey.isPressed) Time.timeScale = BaseTimeScale;   // not during hit-stops or Shift

        GUILayout.EndScrollView();
        GUI.DragWindow(new Rect(0, 0, 10000, 22));
    }
    private System.Collections.IEnumerator CarveThenNotify(Die die, int side, FaceDefinition face)
    {
        yield return die.SwapFace(side, face);
        player.NotifyDiceChanged();
    }

    // Same maths as the enemy slam: rotate so this side's socket points straight up
    private void TurnToTop(Die die, int side)
    {
        Transform socket = die.GetSocket(side);
        die.transform.rotation = Quaternion.FromToRotation(socket.up, Vector3.up) * die.transform.rotation;
    }

    private static string Label(FaceDefinition f)
    {
        if (f == null) return "·";
        return f.type == FaceType.Number ? f.number.ToString() : GameController.OpSymbol(f.op);
    }

    private static GUIStyle Rich() => new GUIStyle(GUI.skin.label) { richText = true };
}
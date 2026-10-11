using System.Text;
using TMPro;
using UnityEngine;

// The ladder on the clipboard: the current act's opponents as a handwritten list (guide section 9).
// Beaten ones are struck through, the current one is marked in red pen, far-away ones are still "???",
// and act bosses are in bold. It answers "how far am I, and what's next".
//
// It's generated from DATA (GameController's ladder), so adding an enemy to the ladder adds a line by itself,
// and it listens to GameController.OnFightStarted, so GameController never needs to know the clipboard exists.
//
// SETUP: a World Space canvas lying on the clipboard's Paper, with one TMP text on it. Drag both in.
public class LadderBoard : MonoBehaviour
{
    [SerializeField] private GameController game;
    [SerializeField] private TMP_Text sheet;
    [SerializeField] private int revealAhead = 2;                                   // only the next few show their name
    [SerializeField] private Color inkColor = new Color(0.12f, 0.12f, 0.18f);       // ordinary pen
    [SerializeField] private Color doneColor = new Color(0.45f, 0.45f, 0.45f);      // faded, struck through
    [SerializeField] private Color currentColor = new Color(0.75f, 0.1f, 0.1f);     // red pen
    [SerializeField] private string currentMark = "> ";

    private void OnEnable()
    {
        if (game != null) game.OnFightStarted += Redraw;
    }

    private void OnDisable()
    {
        if (game != null) game.OnFightStarted -= Redraw;
    }

    private void Start()
    {
        Redraw(game.Fight);
    }

    private void Redraw(int fight)
    {
        EnemyDefinition[] ladder = game.Ladder;
        int[] starts = game.ActStarts;
        if (ladder == null || ladder.Length == 0 || sheet == null) return;

        // Which act is this fight in, and which fights belong to it?
        int act = 1;
        for (int i = 0; i < starts.Length; i++)
        {
            if (fight >= starts[i]) act = i + 1;
        }
        int first = starts[act - 1];
        int last = act < starts.Length ? starts[act] - 1 : ladder.Length;
        last = Mathf.Min(last, ladder.Length);

        StringBuilder text = new StringBuilder();
        text.AppendLine($"<b><u>ACT {act}</u></b>");
        text.AppendLine();

        for (int f = first; f <= last; f++)
        {
            EnemyDefinition enemy = ladder[f - 1];   // fights count from 1, the array from 0
            bool boss = enemy.dieDrop != null;       // the act bosses are the ones that hand over a die
            bool hidden = f - fight > revealAhead;

            string name = hidden ? "???" : enemy.displayName.ToUpper();
            if (boss) name = $"<b>{name}</b>" + (hidden ? "  (BOSS)" : "");

            if (f < fight)
                text.AppendLine($"<color=#{Hex(doneColor)}><s>{f}. {name}</s></color>");
            else if (f == fight)
                text.AppendLine($"<color=#{Hex(currentColor)}>{currentMark}{f}. {name}</color>");
            else
                text.AppendLine($"<color=#{Hex(inkColor)}>{f}. {name}</color>");
        }

        sheet.text = text.ToString();
    }

    private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
}
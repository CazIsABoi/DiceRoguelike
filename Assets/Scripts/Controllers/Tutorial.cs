using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Hints stay up until the player actually does the thing, then never come back this session.
//   Tutorial.Hint("throw", "DRAG A DIE AND LET GO TO THROW IT");   // show it (does nothing if already learned)
//   Tutorial.Done("throw");                                        // call where the player does it
//   Tutorial.Notice("THROW AWAY THE 6? GRAB IT AGAIN");           // one-off message, the hint comes back after
public class Tutorial : MonoBehaviour
{
    private static Tutorial instance;
    private static readonly HashSet<string> shown = new HashSet<string>();
    private static readonly HashSet<string> learned = new HashSet<string>();

    [SerializeField] private StripDisplay strip;
    [SerializeField] private float minShowTime = 2.5f;   // a hint stays at least this long, even if done right away
    [SerializeField] private float noticeTime = 3f;      // how long a one-off notice stays
    [SerializeField] private Color color = new Color32(0xFF, 0x71, 0x34, 0xFF);

    private string activeKey;
    private string activeMessage;
    private float shownAt;
    private int version;   // bumps on every change, so an old coroutine knows it's out of date

    private void Awake()
    {
        instance = this;
    }

    public static void Hint(string key, string message)
    {
        if (instance == null || learned.Contains(key) || instance.activeKey == key) return;

        shown.Add(key);
        instance.activeKey = key;
        instance.activeMessage = message;
        instance.shownAt = Time.time;
        instance.version++;
        instance.Display(message, true);
    }

    public static void Done(string key)
    {
        if (!shown.Contains(key)) return;   // you can't learn a hint you never saw
        learned.Add(key);

        if (instance == null || instance.activeKey != key) return;
        instance.activeKey = null;
        instance.version++;
        instance.StartCoroutine(instance.HideAfterMinTime(instance.version));
    }

    public static void Notice(string message)
    {
        if (instance == null) return;
        instance.version++;
        instance.StartCoroutine(instance.NoticeRoutine(message, instance.version));
    }

    private IEnumerator HideAfterMinTime(int myVersion)
    {
        float wait = shownAt + minShowTime - Time.time;
        if (wait > 0f) yield return new WaitForSeconds(wait);
        if (myVersion != version) yield break;   // something newer is showing now, leave it alone
        Display(null, false);
    }

    private IEnumerator NoticeRoutine(string message, int myVersion)
    {
        Display(message, false);
        yield return new WaitForSeconds(noticeTime);
        if (myVersion != version) yield break;

        if (activeKey != null) Display(activeMessage, true);   // bring the unfinished hint back
        else Display(null, false);
    }

    private void Display(string message, bool stay)
    {
        if (message == null)
        {
            strip.StopAll();
            strip.Clear();
            Subtitles.ClearHint();   // delete this line if you didn't add Subtitles
            return;
        }

        strip.ShowScrolling(message, color, stay ? -1 : 1);   // -1 = keep scrolling until cleared
        Subtitles.Hint(message, stay);                 // delete this line if you didn't add Subtitles
    }
}
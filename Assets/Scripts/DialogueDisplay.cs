using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DialogueDisplay : MonoBehaviour
{
    [SerializeField] private TMP_Text text;
    [SerializeField] private TMP_Text nameLabel;   // sits above or beside the text box
    [SerializeField] private AudioSource voiceSource;   // its own AudioSource, see the note below
    [SerializeField] private AudioClip voice;
    [SerializeField] private float charDelay = 0.04f;
    [SerializeField] private float holdTime = 1.5f;    // how long a finished line stays up
    [SerializeField] private Image portrait;
    private Sprite idleSprite;
    private Sprite talkSprite;

    private readonly Queue<string> pages = new Queue<string>();
    private Coroutine playRoutine;

    public void SetVoice(AudioClip clip) { if (clip != null) voice = clip; }

    // Queues a line. "|" splits it into pages: "NICE ROLL|SHAME ABOUT YOU"
    public void Say(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        foreach (string page in line.Split('|')) pages.Enqueue(page);
        if (playRoutine == null) playRoutine = StartCoroutine(Play());
    }

    // Drops whatever is queued and says this right away (intro, defeat)
    public void SayNow(string line)
    {
        pages.Clear();
        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
            playRoutine = null;
        }
        Say(line);
    }

    private IEnumerator Play()
    {
        while (pages.Count > 0)
        {
            string page = pages.Dequeue();
            text.text = page;
            text.maxVisibleCharacters = 0;

            for (int i = 1; i <= page.Length; i++)
            {
                text.maxVisibleCharacters = i;
                if (page[i - 1] != ' ')
                {
                    voiceSource.pitch = Random.Range(0.9f, 1.1f);
                    voiceSource.PlayOneShot(voice);
                    portrait.sprite = (i % 2 == 0) ? talkSprite : idleSprite;   // mouth flaps
                }
                yield return new WaitForSeconds(charDelay);
            }

            portrait.sprite = idleSprite;   // mouth closed while the line hangs there
            yield return new WaitForSeconds(holdTime);
            voiceSource.pitch = 1f;
            text.text = "";
        }
        playRoutine = null;
    }
    public void SetSpeaker(string speakerName, Sprite idle, Sprite talk, AudioClip voiceClip)
    {
        if (nameLabel != null) nameLabel.text = speakerName.ToUpper();
        idleSprite = idle;
        talkSprite = talk != null ? talk : idle;   // no talk frame = just don't animate
        portrait.sprite = idleSprite;
        portrait.enabled = idle != null;
        SetVoice(voiceClip);
    }
}
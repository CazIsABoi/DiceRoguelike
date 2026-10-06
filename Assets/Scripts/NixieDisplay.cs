using TMPro;
using UnityEngine;
using System.Collections;

public class NixieDisplay : MonoBehaviour
{
    [SerializeField] private DiceSide side;      // whose HP to show
    [SerializeField] private TMP_Text[] tubes;   // left to right

    private void OnEnable() { side.OnHealthChanged += ShowHealth; }
    private void OnDisable() { side.OnHealthChanged -= ShowHealth; }

    [SerializeField] private float flickerTime = 0.4f;
    private string lastDigits;
    private Coroutine[] flickers;

    private void ShowHealth(int current, int max)
    {
        string digits = current.ToString("D" + tubes.Length);
        if (flickers == null) flickers = new Coroutine[tubes.Length];

        for (int i = 0; i < tubes.Length; i++)
        {
            bool changed = lastDigits != null && lastDigits[i] != digits[i];
            if (changed)
            {
                if (flickers[i] != null) StopCoroutine(flickers[i]);
                flickers[i] = StartCoroutine(Flicker(i, digits[i]));
            }
            else
            {
                tubes[i].text = digits[i].ToString();
            }
        }
        lastDigits = digits;
    }

    private IEnumerator Flicker(int i, char final)
    {
        float end = Time.time + flickerTime;
        while (Time.time < end)
        {
            tubes[i].text = Random.Range(0, 10).ToString();
            tubes[i].enabled = Random.value > 0.2f;
            yield return new WaitForSeconds(Random.Range(0.03f, 0.08f));
        }
        tubes[i].enabled = true;          
        tubes[i].text = final.ToString();
    }
}
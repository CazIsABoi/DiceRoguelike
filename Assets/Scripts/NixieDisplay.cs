using TMPro;
using UnityEngine;

public class NixieDisplay : MonoBehaviour
{
    [SerializeField] private DiceSide side;      // whose HP to show
    [SerializeField] private TMP_Text[] tubes;   // left to right

    private void OnEnable() { side.OnHealthChanged += ShowHealth; }
    private void OnDisable() { side.OnHealthChanged -= ShowHealth; }

    private void ShowHealth(int current, int max)
    {
        string digits = current.ToString("D" + tubes.Length);

        for (int i = 0; i < tubes.Length; i++)
        {
            tubes[i].text = digits[i].ToString();
        }
    }
}
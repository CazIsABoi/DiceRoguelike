using UnityEngine;

public enum ThrowMode { Drag, Click }

// How the player throws dice, saved on this computer.
//   Drag:  grab a die and fling it (the original way)
//   Click: click a die and it throws itself, the same way the enemies' dice do. Easier on the hands.
// ThrowStylePicker asks on the first launch; a Settings button can open the picker again.
public static class ThrowStyle
{
    private const string Key = "ThrowStyle";

    public static bool HasChosen => PlayerPrefs.HasKey(Key);

    public static ThrowMode Current
    {
        get => (ThrowMode)PlayerPrefs.GetInt(Key, (int)ThrowMode.Drag);
        set { PlayerPrefs.SetInt(Key, (int)value); PlayerPrefs.Save(); }
    }

    public static bool ClickToThrow => Current == ThrowMode.Click;

    // For hints: "CLICK A DIE TO THROW IT" or "DRAG A DIE AND LET GO TO THROW IT"
    public static string ThrowHint => ClickToThrow ? "CLICK A DIE TO THROW IT" : "DRAG A DIE AND LET GO TO THROW IT";
    public static string AgainHint => ClickToThrow ? "CLICK TO THROW AGAIN" : "DRAG TO THROW AGAIN";

    public static void Forget()   // for testing: the picker shows again on the next launch
    {
        PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.Save();
    }
}

using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    [SerializeField] private string gameScene = "Game";

    public void Play() => SceneManager.LoadScene(gameScene);   // hook to the button's OnClick
}
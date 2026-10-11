using UnityEngine;

public class BGM : MonoBehaviour
{
    [SerializeField] private AudioClip[] bgmClips;
    private AudioSource audioSource;

    private void Start()
    {
        audioSource = GetComponent<AudioSource>();
    }

    private void Update()
    {
        if (!audioSource.isPlaying)
        {
            PlayRandomBGM();
        }
    }

    private void PlayRandomBGM()
    {
        if (bgmClips.Length == 0) return;
        int randomIndex = Random.Range(0, bgmClips.Length);
        audioSource.clip = bgmClips[randomIndex];
        audioSource.Play();
    }
}

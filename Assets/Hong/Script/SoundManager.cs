using System.Collections.Generic;
using UnityEngine;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance;
    
    public Dictionary<string, AudioClip> audioClips = new Dictionary<string, AudioClip>();
    public List<AudioClip> audioList = new List<AudioClip>();
    public AudioSource sfx = null;
    public AudioSource bgm = null;
    public float sfxVolume = 1f;
    public float bgmVolume = 1f;
    public float masterVolume = 1f;
    public AudioClip currentSfx = null;
    public AudioClip currentBgm = null;

    public void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(this);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
        
        PlayBgm("Lobby", bgmVolume * masterVolume);
        foreach (AudioClip audioClip in audioList)
        {
            string id = audioClip.name;
            audioClips.Add(id, audioClip);
        }
    }

    public void PlaySfx(string clipName, float volume = 1.0f)
    {
        volume = sfxVolume * masterVolume;
        AudioClip clip = audioClips[clipName];
        currentSfx = clip;
        sfx.PlayOneShot(clip, volume);
    }

    public void PlayBgm(string clipName, float volume = 1.0f)
    {
        volume = bgmVolume * masterVolume;

        AudioClip clip = audioClips[clipName];
        currentBgm = clip;
        bgm.loop = true;
        bgm.PlayOneShot(clip, volume);
    }
}

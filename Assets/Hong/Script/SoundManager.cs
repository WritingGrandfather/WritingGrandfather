using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance;

    public List<AudioClip> audioList = new List<AudioClip>();
    public AudioSource bgm = null;
    public AudioMixerGroup sfxMixerGroup = null;

    public float sfxVolume    = 1f;
    public float bgmVolume    = 1f;
    public float masterVolume = 1f;

    [SerializeField] int sfxPoolSize = 8;

    Dictionary<string, AudioClip> audioClips = new Dictionary<string, AudioClip>();
    AudioSource[] sfxPool;
    int poolIndex = 0;

    public void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(this); }
        else { Destroy(gameObject); return; }

        foreach (var clip in audioList)
            audioClips[clip.name] = clip;

        sfxPool = new AudioSource[sfxPoolSize];
        for (int i = 0; i < sfxPoolSize; i++)
        {
            var go = new GameObject($"SfxSource_{i}");
            go.transform.SetParent(transform);
            var src = go.AddComponent<AudioSource>();
            src.outputAudioMixerGroup = sfxMixerGroup;
            sfxPool[i] = src;
        }

        PlayBgm("Lobby", bgmVolume * masterVolume);
    }

    AudioSource GetFreeSource()
    {
        for (int i = 0; i < sfxPool.Length; i++)
        {
            int idx = (poolIndex + i) % sfxPool.Length;
            if (!sfxPool[idx].isPlaying)
            {
                poolIndex = (idx + 1) % sfxPool.Length;
                return sfxPool[idx];
            }
        }
        // 모두 사용 중이면 라운드로빈으로 하나 강제 점유
        var stolen = sfxPool[poolIndex];
        poolIndex = (poolIndex + 1) % sfxPool.Length;
        return stolen;
    }

    public AudioSource PlaySfx(string clipName, bool loop = false)
    {
        if (!audioClips.TryGetValue(clipName, out var clip))
        {
            Debug.LogWarning($"[Sound] 클립 없음: {clipName}");
            return null;
        }

        var source    = GetFreeSource();
        source.loop   = loop;
        source.clip   = clip;
        source.volume = sfxVolume * masterVolume;
        source.Play();
        return source;
    }

    public void PlayBgm(string clipName, float volume = 1f)
    {
        if (!audioClips.TryGetValue(clipName, out var clip)) return;
        bgm.clip   = clip;
        bgm.volume = bgmVolume * masterVolume;
        bgm.loop   = true;
        bgm.Play();
    }
}

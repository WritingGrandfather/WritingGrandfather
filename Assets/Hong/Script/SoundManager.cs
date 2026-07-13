using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance;

    // 어느 씬에서 시작하든(직접 재생 포함) 앱이 뜰 때 Resources의 SoundManager 프리팹을
    // 자동으로 하나 만든다. 씬에 SoundManager를 배치할 필요가 없고, 이미 씬에 있다면
    // Awake의 중복 가드가 알아서 정리한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var prefab = Resources.Load<GameObject>("SoundManager");
        if (prefab != null) Instantiate(prefab);
        else Debug.LogWarning("[Sound] Resources/SoundManager 프리팹을 찾지 못했습니다.");
    }

    public List<AudioClip> audioList = new List<AudioClip>();
    public AudioSource bgm = null;
    public AudioMixerGroup sfxMixerGroup = null;

    public float sfxVolume    = 1f;
    public float bgmVolume    = 1f;
    public float masterVolume = 1f;

    // 설정화면의 on/off 토글 상태 (인스펙터의 기본 볼륨 레벨과 곱해진다).
    // PlayerPrefs에 저장돼 씬 전환/재실행 후에도 유지된다.
    public const string PrefMaster = "sound.master";
    public const string PrefBgm    = "sound.bgm";
    public const string PrefSfx    = "sound.sfx";
    bool masterOn = true;
    bool bgmOn    = true;
    bool sfxOn    = true;

    float MasterMul => masterOn ? masterVolume : 0f;
    float SfxLevel  => (sfxOn ? sfxVolume : 0f) * MasterMul;
    float BgmLevel  => (bgmOn ? bgmVolume : 0f) * MasterMul;

    [SerializeField] int sfxPoolSize = 8;

    Dictionary<string, AudioClip> audioClips = new Dictionary<string, AudioClip>();
    AudioSource[] sfxPool;
    int poolIndex = 0;

    public void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(this); }
        else { Destroy(gameObject); return; }

        LoadVolumePrefs();

        // bgm AudioSource를 인스펙터에서 안 꽂아둬도 BGM이 나오도록 런타임에 하나 만든다.
        if (bgm == null)
        {
            bgm = gameObject.AddComponent<AudioSource>();
            bgm.playOnAwake = false;
        }

        foreach (var clip in audioList)
            if (clip != null) audioClips[clip.name] = clip;

        sfxPool = new AudioSource[sfxPoolSize];
        for (int i = 0; i < sfxPoolSize; i++)
        {
            var go = new GameObject($"SfxSource_{i}");
            go.transform.SetParent(transform);
            var src = go.AddComponent<AudioSource>();
            src.outputAudioMixerGroup = sfxMixerGroup;
            sfxPool[i] = src;
        }

        PlayBgm("Lobby");
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
        source.volume = SfxLevel;
        source.Play();
        return source;
    }

    public void PlayBgm(string clipName, float volume = 1f)
    {
        if (!audioClips.TryGetValue(clipName, out var clip)) return;
        bgm.clip   = clip;
        bgm.volume = BgmLevel;
        bgm.loop   = true;
        bgm.Play();
    }

    // PlayerPrefs에서 on/off 토글 상태를 읽어 온다 (기본값: 전부 켜짐).
    void LoadVolumePrefs()
    {
        masterOn = PlayerPrefs.GetInt(PrefMaster, 1) == 1;
        bgmOn    = PlayerPrefs.GetInt(PrefBgm, 1)    == 1;
        sfxOn    = PlayerPrefs.GetInt(PrefSfx, 1)    == 1;
    }

    // 설정화면에서 토글이 바뀐 뒤 호출 - 저장된 값을 다시 읽어 즉시 반영한다.
    public void ReloadVolumePrefs()
    {
        LoadVolumePrefs();
        ApplyVolumes();
    }

    // 현재 재생 중인 BGM과 루프 효과음(연필/지우개 등)의 볼륨을 즉시 갱신한다.
    // 새로 재생되는 효과음은 PlaySfx에서 SfxLevel을 다시 읽으므로 자동 반영된다.
    void ApplyVolumes()
    {
        if (bgm != null) bgm.volume = BgmLevel;
        if (sfxPool != null)
            foreach (var s in sfxPool)
                if (s != null && s.isPlaying) s.volume = SfxLevel;
    }
}

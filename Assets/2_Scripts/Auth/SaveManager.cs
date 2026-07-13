using System;
using System.IO;
using UnityEngine;
#if FIREBASE_ENABLED
using Firebase.Firestore;
using Firebase.Extensions;
#endif

/// <summary>
/// 게임 데이터 저장/불러오기.
///  - 게스트  → 이 기기에만 저장 (로컬 JSON 파일)
///  - 이메일/구글 계정 → Firebase(Firestore) 클라우드에 저장 (기기 바뀌어도 유지)
///
/// 라우팅 기준: AuthManager.IsGuest
/// 사용법: SaveManager.Instance.Save(data) / Load(data => ...)
/// </summary>
public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // 클라우드 저장 대상인가? (로그인된 실제 계정이면 클라우드, 게스트/비로그인은 로컬)
    bool UseCloud =>
        AuthManager.Instance != null && AuthManager.Instance.IsSignedIn && !AuthManager.Instance.IsGuest;

    string LocalPath => Path.Combine(Application.persistentDataPath, "playerdata.json");

    // ── 저장 ────────────────────────────────────────────────────────
    public void Save(PlayerData data, Action<bool, string> onDone = null)
    {
        data.lastPlayedIso = DateTime.UtcNow.ToString("o");

        if (UseCloud) SaveCloud(data, onDone);
        else SaveLocal(data, onDone);
    }

    // ── 불러오기 ────────────────────────────────────────────────────
    public void Load(Action<PlayerData> onLoaded)
    {
        if (UseCloud) LoadCloud(onLoaded);
        else LoadLocal(onLoaded);
    }

    // ── 로컬 (게스트/비로그인) ──────────────────────────────────────
    void SaveLocal(PlayerData data, Action<bool, string> onDone)
    {
        try
        {
            File.WriteAllText(LocalPath, JsonUtility.ToJson(data));
            onDone?.Invoke(true, "로컬 저장 완료");
        }
        catch (Exception e)
        {
            onDone?.Invoke(false, "로컬 저장 실패: " + e.Message);
        }
    }

    void LoadLocal(Action<PlayerData> onLoaded)
    {
        try
        {
            if (File.Exists(LocalPath))
                onLoaded?.Invoke(JsonUtility.FromJson<PlayerData>(File.ReadAllText(LocalPath)));
            else
                onLoaded?.Invoke(new PlayerData()); // 없으면 새 데이터
        }
        catch
        {
            onLoaded?.Invoke(new PlayerData());
        }
    }

    // ── 클라우드 (Firestore) ────────────────────────────────────────
    void SaveCloud(PlayerData data, Action<bool, string> onDone)
    {
#if FIREBASE_ENABLED
        string uid = AuthManager.Instance.UserId;
        var dict = new System.Collections.Generic.Dictionary<string, object>
        {
            { "clearedStage", data.clearedStage },
            { "bestScore", data.bestScore },
            { "totalCharacters", data.totalCharacters },
            { "lastPlayedIso", data.lastPlayedIso },
        };
        FirebaseFirestore.DefaultInstance.Collection("users").Document(uid)
            .SetAsync(dict).ContinueWithOnMainThread(t =>
            {
                if (t.IsFaulted || t.IsCanceled) onDone?.Invoke(false, "클라우드 저장 실패");
                else onDone?.Invoke(true, "클라우드 저장 완료");
            });
#else
        onDone?.Invoke(false, "Firebase 미설정");
#endif
    }

    void LoadCloud(Action<PlayerData> onLoaded)
    {
#if FIREBASE_ENABLED
        string uid = AuthManager.Instance.UserId;
        FirebaseFirestore.DefaultInstance.Collection("users").Document(uid)
            .GetSnapshotAsync().ContinueWithOnMainThread(t =>
            {
                if (t.IsFaulted || t.IsCanceled || !t.Result.Exists)
                {
                    onLoaded?.Invoke(new PlayerData());
                    return;
                }
                var d = t.Result.ToDictionary();
                onLoaded?.Invoke(new PlayerData
                {
                    clearedStage = GetInt(d, "clearedStage"),
                    bestScore = GetInt(d, "bestScore"),
                    totalCharacters = GetInt(d, "totalCharacters"),
                    lastPlayedIso = d.TryGetValue("lastPlayedIso", out var v) ? v as string : "",
                });
            });
#else
        onLoaded?.Invoke(new PlayerData());
#endif
    }

#if FIREBASE_ENABLED
    static int GetInt(System.Collections.Generic.IDictionary<string, object> d, string key)
        => d.TryGetValue(key, out var v) && v != null ? Convert.ToInt32(v) : 0;
#endif
}

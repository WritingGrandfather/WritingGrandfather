using System;
using System.Collections.Generic;
using UnityEngine;
#if FIRESTORE_ENABLED
using Firebase.Firestore;
using Firebase.Extensions;
#endif

/// <summary>
/// 랭킹 저장/조회. (로컬 Node 서버 → Firebase Firestore로 전환)
/// 컬렉션 "ranking", 문서 = 유저 id, 필드 { id, score }. 최고 점수만 유지.
///
/// Firestore 코드는 FIRESTORE_ENABLED 심볼로 가드 (SaveManager와 동일).
/// 켜는 법: FirebaseFirestore 임포트 → Tools > Firebase > Enable FIRESTORE_ENABLED
/// </summary>
public class RankingManager : MonoBehaviour
{
    const string COLLECTION = "ranking";

    [Serializable]
    public class RankingData
    {
        public string id;
        public int score;
    }

    /// <summary>최초 등록(점수 0). 기존 기록이 있으면 유지됨.</summary>
    public void RegisterUser(string id, Action onSuccess = null)
    {
        UploadScore(id, 0, onSuccess);
    }

    /// <summary>점수 업로드. 기존 최고점보다 높을 때만 갱신.</summary>
    public void UploadScore(string id, int score, Action onSuccess = null)
    {
#if FIRESTORE_ENABLED
        if (string.IsNullOrEmpty(id)) { Debug.LogError("[Ranking] id가 비어 있습니다."); return; }

        var doc = FirebaseFirestore.DefaultInstance.Collection(COLLECTION).Document(id);

        // 기존 점수 확인 → 더 높을 때만 갱신 (최고 점수 유지)
        doc.GetSnapshotAsync().ContinueWithOnMainThread(t =>
        {
            if (!t.IsFaulted && !t.IsCanceled && t.Result.Exists)
            {
                var d = t.Result.ToDictionary();
                if (d.TryGetValue("score", out var v) && v != null && Convert.ToInt32(v) >= score)
                {
                    onSuccess?.Invoke(); // 기존 점수가 더 높거나 같음 → 갱신 안 함
                    return;
                }
            }

            var data = new Dictionary<string, object> { { "id", id }, { "score", score } };
            doc.SetAsync(data).ContinueWithOnMainThread(s =>
            {
                if (s.IsFaulted || s.IsCanceled) Debug.LogError("[Ranking] 점수 저장 실패");
                else { Debug.Log("[Ranking] 점수 저장 성공"); onSuccess?.Invoke(); }
            });
        });
#else
        Debug.LogWarning("[Ranking] Firestore 미설정 — FIRESTORE_ENABLED 심볼과 Firestore SDK가 필요합니다.");
        onSuccess?.Invoke();
#endif
    }

    /// <summary>점수 높은 순 랭킹 조회 (상위 100명).</summary>
    public void GetRanking(Action<List<RankingData>> callback)
    {
#if FIRESTORE_ENABLED
        FirebaseFirestore.DefaultInstance.Collection(COLLECTION)
            .OrderByDescending("score").Limit(100)
            .GetSnapshotAsync().ContinueWithOnMainThread(t =>
            {
                var list = new List<RankingData>();
                if (!t.IsFaulted && !t.IsCanceled)
                {
                    foreach (var snap in t.Result.Documents)
                    {
                        var d = snap.ToDictionary();
                        list.Add(new RankingData
                        {
                            id = d.TryGetValue("id", out var idv) ? idv as string : snap.Id,
                            score = d.TryGetValue("score", out var sv) && sv != null ? Convert.ToInt32(sv) : 0,
                        });
                    }
                }
                else Debug.LogError("[Ranking] 랭킹 조회 실패");

                callback?.Invoke(list);
            });
#else
        Debug.LogWarning("[Ranking] Firestore 미설정 — FIRESTORE_ENABLED 심볼과 Firestore SDK가 필요합니다.");
        callback?.Invoke(new List<RankingData>());
#endif
    }
}

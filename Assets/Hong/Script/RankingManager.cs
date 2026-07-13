using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class RankingManager : MonoBehaviour
{
    private const string SERVER_URL = "http://localhost:3000";

    [Serializable]
    public class ScoreData
    {
        public string id;
        public int score;
    }

    [Serializable]
    public class RankingData
    {
        public string id;
        public int score;
    }

    [Serializable]
    private class RankingList
    {
        public RankingData[] items;
    }

    public void RegisterUser(string id, Action onSuccess = null)
    {
        StartCoroutine(UploadScoreCoroutine(id, 0, onSuccess));
    }

    public void UploadScore(string id, int score)
    {
        StartCoroutine(UploadScoreCoroutine(id, score));
    }

    IEnumerator UploadScoreCoroutine(string id, int score, Action onSuccess = null)
    {
        ScoreData data = new ScoreData
        {
            id = id,
            score = score
        };

        string json = JsonUtility.ToJson(data);

        UnityWebRequest request = new UnityWebRequest(SERVER_URL + "/score", "POST");

        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);

        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("점수 저장 성공 : " + request.downloadHandler.text);
            onSuccess?.Invoke();
        }
        else
        {
            Debug.LogError(request.error);
        }
    }

    public void GetRanking(Action<List<RankingData>> callback)
    {
        StartCoroutine(GetRankingCoroutine(callback));
    }

    IEnumerator GetRankingCoroutine(Action<List<RankingData>> callback)
    {
        UnityWebRequest request =
            UnityWebRequest.Get(SERVER_URL + "/ranking");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError(request.error);
            yield break;
        }

        string json = request.downloadHandler.text;

        // JsonUtility는 배열을 바로 파싱 못해서 감싸줌
        json = "{\"items\":" + json + "}";

        RankingList list =
            JsonUtility.FromJson<RankingList>(json);

        callback?.Invoke(new List<RankingData>(list.items));
    }
}
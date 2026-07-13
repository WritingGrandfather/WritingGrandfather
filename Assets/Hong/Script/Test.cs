using UnityEngine;

public class Test : MonoBehaviour
{
    public RankingManager rankingManager;

    void Start()
    {
        rankingManager.UploadScore("Hong", Random.Range(0, 10000));

        rankingManager.GetRanking((ranking) =>
        {
            foreach (var user in ranking)
            {
                Debug.Log(user.id + " : " + user.score);
            }
        });
    }
}
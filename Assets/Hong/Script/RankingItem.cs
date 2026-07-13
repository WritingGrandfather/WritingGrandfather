using TMPro;
using UnityEngine;

public class RankingItem : MonoBehaviour
{
    public TMP_Text rankText;
    public TMP_Text idText;
    public TMP_Text scoreText;

    public void SetData(int rank, string id, int score)
    {
        rankText.text = rank.ToString();
        idText.text = id;
        scoreText.text = score.ToString();
    }
}
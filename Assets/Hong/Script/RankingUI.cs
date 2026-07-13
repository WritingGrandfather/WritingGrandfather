using TMPro;
using UnityEngine;

public class RankingUI : MonoBehaviour
{
    public RankingManager rankingManager;

    public Transform content;

    public RankingItem itemPrefab;

    public TMP_InputField idInputField;

    public void OnRegisterButtonClicked()
    {
        string id = idInputField.text.Trim();
        if (string.IsNullOrEmpty(id)) return;

        rankingManager.RegisterUser(id, () =>
        {
            idInputField.text = "";
            Refresh();
        });
    }

    public void Refresh()
    {
        foreach (Transform child in content)
        {
            Destroy(child.gameObject);
        }

        rankingManager.GetRanking((ranking) =>
        {
            for (int i = 0; i < ranking.Count; i++)
            {
                RankingItem item = Instantiate(itemPrefab, content);

                item.SetData(
                    i + 1,
                    ranking[i].id,
                    ranking[i].score
                );
            }
        });
    }
}
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RankingUI : MonoBehaviour
{
    public RankingManager rankingManager;

    public Transform content;

    public RankingItem itemPrefab;

    public TMP_InputField idInputField;

    [Tooltip("뒤로가기 버튼이 돌아갈 씬 이름")]
    public string lobbyScene = "LobbyScene";

    // 랭킹 화면이 열리면 자동으로 최신 순위를 불러온다.
    void OnEnable()
    {
        if (rankingManager == null) rankingManager = FindObjectOfType<RankingManager>();
        Refresh();
    }

    // 뒤로가기 버튼(OnClick)에 연결 → 이전 씬(CallerScene)으로 복귀, 없으면 lobbyScene.
    public void BackToLobby()
    {
        string target = !string.IsNullOrEmpty(RankingScreenController.CallerScene)
            ? RankingScreenController.CallerScene
            : lobbyScene;
        RankingScreenController.CallerScene = null;
        SceneManager.LoadScene(target);
    }

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
        if (rankingManager == null || content == null || itemPrefab == null)
        {
            Debug.LogWarning("[RankingUI] 참조(rankingManager/content/itemPrefab)가 비어 있어 갱신을 건너뜁니다.");
            return;
        }

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
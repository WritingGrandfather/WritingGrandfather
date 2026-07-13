using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// UI Toolkit 기반 랭킹 화면. Ranking.uxml/Ranking.uss와 함께 쓰인다.
/// RankingManager(Firestore)에서 순위를 받아 목록을 만들고, 내 순위는 강조 표시한다.
///
/// 씬 설정:
///  1) 빈 오브젝트에 UIDocument 추가 → Panel Settings = LobbyPanelSettings, Source Asset = Ranking.uxml
///  2) 같은 오브젝트에 이 컴포넌트 추가
///  3) rankingManager는 비워둬도 씬에서 자동으로 찾는다
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class RankingScreenController : MonoBehaviour
{
    [Tooltip("뒤로가기 시 돌아갈 씬")]
    [SerializeField] string lobbyScene = "LobbyScene";

    [Tooltip("순위를 조회할 랭킹 매니저 (비우면 씬에서 자동 검색)")]
    [SerializeField] RankingManager rankingManager;

    VisualElement root;
    Label title;
    Label headerRank;
    Label headerName;
    Label headerScore;
    ScrollView list;
    Label emptyLabel;
    Button backButton;

    void OnEnable()
    {
        root = GetComponent<UIDocument>().rootVisualElement;

        title = root.Q<Label>("ranking-title");
        headerRank = root.Q<Label>("header-rank");
        headerName = root.Q<Label>("header-name");
        headerScore = root.Q<Label>("header-score");
        list = root.Q<ScrollView>("ranking-list");
        emptyLabel = root.Q<Label>("ranking-empty");
        backButton = root.Q<Button>("btn-back");

        if (backButton != null) backButton.clicked += OnBackClicked;

        LocalizationManager.OnLanguageChanged += ApplyLocalization;
        ApplyLocalization();

        if (rankingManager == null) rankingManager = FindObjectOfType<RankingManager>();
        LoadRanking();
    }

    void OnDisable()
    {
        if (backButton != null) backButton.clicked -= OnBackClicked;
        LocalizationManager.OnLanguageChanged -= ApplyLocalization;
    }

    void ApplyLocalization()
    {
        if (title != null) title.text = LocalizationManager.Get("ranking.title");
        if (headerRank != null) headerRank.text = LocalizationManager.Get("ranking.header_rank");
        if (headerName != null) headerName.text = LocalizationManager.Get("ranking.header_name");
        if (headerScore != null) headerScore.text = LocalizationManager.Get("ranking.header_score");
        if (emptyLabel != null) emptyLabel.text = LocalizationManager.Get("ranking.empty");
        if (backButton != null) backButton.text = LocalizationManager.Get("ranking.back");
    }

    void OnBackClicked() => SceneManager.LoadScene(lobbyScene);

    void LoadRanking()
    {
        if (rankingManager == null)
        {
            Debug.LogWarning("[RankingScreen] RankingManager를 찾지 못했습니다.");
            ShowEmpty(true);
            return;
        }
        rankingManager.GetRanking(BuildRows);
    }

    void BuildRows(List<RankingManager.RankingData> data)
    {
        if (list == null) return;
        list.Clear();

        if (data == null || data.Count == 0)
        {
            ShowEmpty(true);
            return;
        }
        ShowEmpty(false);

        string myId = AuthManager.Instance != null ? AuthManager.Instance.UserId : "";

        for (int i = 0; i < data.Count; i++)
        {
            var d = data[i];
            bool isMe = !string.IsNullOrEmpty(myId) && d.id == myId;

            var row = new VisualElement();
            row.AddToClassList("ranking-row");
            if (i < 3) row.AddToClassList($"rank-{i + 1}");
            if (isMe) row.AddToClassList("ranking-row--me");

            var rank = new Label((i + 1).ToString());
            rank.AddToClassList("cell-rank");

            var name = new Label(DisplayNameFor(d.id, isMe));
            name.AddToClassList("cell-name");

            var score = new Label(d.score.ToString());
            score.AddToClassList("cell-score");

            row.Add(rank);
            row.Add(name);
            row.Add(score);
            list.Add(row);
        }
    }

    // 표시 이름: 내 행이면 내 표시 이름(없으면 "나"), 남이면 긴 id를 짧게 줄여서.
    string DisplayNameFor(string id, bool isMe)
    {
        if (isMe)
        {
            string dn = AuthManager.Instance != null ? AuthManager.Instance.DisplayName : "";
            return string.IsNullOrEmpty(dn) ? LocalizationManager.Get("ranking.me") : dn;
        }
        if (string.IsNullOrEmpty(id)) return "-";
        return id.Length > 10 ? id.Substring(0, 8) + "…" : id;
    }

    void ShowEmpty(bool show)
    {
        if (emptyLabel == null) return;
        if (show) emptyLabel.RemoveFromClassList("hidden");
        else emptyLabel.AddToClassList("hidden");
    }
}

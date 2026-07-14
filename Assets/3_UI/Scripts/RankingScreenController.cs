using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// UI Toolkit 기반 랭킹 화면. Ranking.uxml/Ranking.uss와 함께 쓰인다.
/// RankingManager(Firestore)에서 순위를 받아 상위 10명만 고정 목록으로 보여주고,
/// 그 아래 별도 카드에 내 순위/점수/등급을 표시한다. (스크롤 없음 — 예전 ScrollView가
/// 손가락으로 내용을 끌어 움직일 수 있어 "점수가 움직인다"는 문제가 있었음)
///
/// 씬 설정:
///  1) 빈 오브젝트에 UIDocument 추가 → Panel Settings = LobbyPanelSettings, Source Asset = Ranking.uxml
///  2) 같은 오브젝트에 이 컴포넌트 추가
///  3) rankingManager는 비워둬도 씬에서 자동으로 찾는다
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class RankingScreenController : MonoBehaviour
{
    // 랭킹 씬을 열기 전 호출한 씬이 여기에 저장됨 — 뒤로가기에서 그 씬으로 복귀
    public static string CallerScene;

    const int TopCount = 10;

    [Tooltip("CallerScene이 없을 때 fallback으로 돌아갈 씬")]
    [SerializeField] string lobbyScene = "LobbyScene";

    [Tooltip("순위를 조회할 랭킹 매니저 (비우면 씬에서 자동 검색)")]
    [SerializeField] RankingManager rankingManager;

    [Header("등급 기준 (점수 이상일 때 해당 등급)")]
    [SerializeField] int bronzeScore = 100;
    [SerializeField] int silverScore = 400;
    [SerializeField] int goldScore = 700;
    [SerializeField] int diamondScore = 1000;

    // SafeArea 기준 최소 여백 — Ranking.uss의 .ranking-root padding 값과 맞춤
    [SerializeField] float baseMarginH = 32f;
    [SerializeField] float baseMarginV = 48f;

    VisualElement root;
    VisualElement rankingRoot;
    Label title;
    Label headerRank;
    Label headerName;
    Label headerScore;
    VisualElement list;
    Label emptyLabel;
    Label myRankLabel;
    Label myRankValue;
    Label myScoreLabel;
    Label myScoreValue;
    Label myGradeLabel;
    Label myGradeValue;
    Button backButton;

    Rect _appliedSafeArea;
    Vector2Int _appliedScreenSize;
    Vector2 _appliedPanelSize;

    void OnEnable()
    {
        root = GetComponent<UIDocument>().rootVisualElement;
        UIClickSound.Attach(root);

        rankingRoot = root.Q<VisualElement>("ranking-root");
        title = root.Q<Label>("ranking-title");
        headerRank = root.Q<Label>("header-rank");
        headerName = root.Q<Label>("header-name");
        headerScore = root.Q<Label>("header-score");
        list = root.Q<VisualElement>("ranking-list");
        emptyLabel = root.Q<Label>("ranking-empty");
        myRankLabel = root.Q<Label>("my-rank-label");
        myRankValue = root.Q<Label>("my-rank-value");
        myScoreLabel = root.Q<Label>("my-score-label");
        myScoreValue = root.Q<Label>("my-score-value");
        myGradeLabel = root.Q<Label>("my-grade-label");
        myGradeValue = root.Q<Label>("my-grade-value");
        backButton = root.Q<Button>("btn-back");

        if (backButton != null) backButton.clicked += OnBackClicked;

        LocalizationManager.OnLanguageChanged += ApplyLocalization;
        ApplyLocalization();

        if (rankingManager == null) rankingManager = FindObjectOfType<RankingManager>();
        LoadRanking();

        // 레이아웃이 처음 계산되는 시점에 즉시 SafeArea를 적용해 첫 프레임 플래시 방지
        if (rankingRoot != null)
            rankingRoot.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

        ApplySafeArea();
    }

    void OnDisable()
    {
        if (backButton != null) backButton.clicked -= OnBackClicked;
        LocalizationManager.OnLanguageChanged -= ApplyLocalization;
        rankingRoot?.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
    }

    void OnGeometryChanged(GeometryChangedEvent evt) => ApplySafeArea();

    void Update() => ApplySafeArea();

    void ApplySafeArea()
    {
        if (rankingRoot == null || rankingRoot.panel == null) return;

        var safeArea = Screen.safeArea;
        var screenSize = new Vector2Int(Screen.width, Screen.height);
        var panel = rankingRoot.panel;
        float panelW = panel.visualTree.resolvedStyle.width;
        float panelH = panel.visualTree.resolvedStyle.height;
        if (panelW <= 0f || panelH <= 0f) return;

        var panelSize = new Vector2(panelW, panelH);
        if (safeArea == _appliedSafeArea && screenSize == _appliedScreenSize && panelSize == _appliedPanelSize)
            return;

        // 스크린 좌표(좌하단 원점) → 패널 좌표(좌상단 원점) 변환
        Vector2 topLeft = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(safeArea.xMin, screenSize.y - safeArea.yMax));
        Vector2 bottomRight = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(safeArea.xMax, screenSize.y - safeArea.yMin));

        rankingRoot.style.paddingLeft = Mathf.Max(0f, topLeft.x) + baseMarginH;
        rankingRoot.style.paddingRight = Mathf.Max(0f, panelW - bottomRight.x) + baseMarginH;
        rankingRoot.style.paddingTop = Mathf.Max(0f, topLeft.y) + baseMarginV;
        rankingRoot.style.paddingBottom = Mathf.Max(0f, panelH - bottomRight.y) + baseMarginV;

        _appliedSafeArea = safeArea;
        _appliedScreenSize = screenSize;
        _appliedPanelSize = panelSize;
    }

    void ApplyLocalization()
    {
        if (title != null) title.text = LocalizationManager.Get("ranking.title");
        if (headerRank != null) headerRank.text = LocalizationManager.Get("ranking.header_rank");
        if (headerName != null) headerName.text = LocalizationManager.Get("ranking.header_name");
        if (headerScore != null) headerScore.text = LocalizationManager.Get("ranking.header_score");
        if (emptyLabel != null) emptyLabel.text = LocalizationManager.Get("ranking.empty");
        if (myRankLabel != null) myRankLabel.text = LocalizationManager.Get("ranking.my_rank");
        if (myScoreLabel != null) myScoreLabel.text = LocalizationManager.Get("ranking.my_score");
        if (myGradeLabel != null) myGradeLabel.text = LocalizationManager.Get("ranking.my_grade");
        if (backButton != null) backButton.text = LocalizationManager.Get("ranking.back");
    }

    void OnBackClicked()
    {
        string target = !string.IsNullOrEmpty(CallerScene) ? CallerScene : lobbyScene;
        CallerScene = null;
        SceneManager.LoadScene(target);
    }

    void LoadRanking()
    {
        if (rankingManager == null)
        {
            Debug.LogWarning("[RankingScreen] RankingManager를 찾지 못했습니다.");
            ShowEmpty(true);
            ShowMyLocalFallback();
            return;
        }
        try
        {
            rankingManager.GetRanking(BuildScreen);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[RankingScreen] 랭킹 불러오기 실패 (Firebase 미지원 환경일 수 있음): {e.Message}");
            ShowEmpty(true);
            ShowMyLocalFallback();
        }
    }

    void BuildScreen(List<RankingManager.RankingData> data)
    {
        if (list == null) return;
        list.Clear();

        if (data == null || data.Count == 0)
        {
            ShowEmpty(true);
            ShowMyLocalFallback();
            return;
        }
        ShowEmpty(false);

        string myId = AuthManager.Instance != null ? AuthManager.Instance.UserId : "";
        int myIndex = -1;

        int rowCount = Mathf.Min(TopCount, data.Count);
        for (int i = 0; i < rowCount; i++)
        {
            var d = data[i];
            bool isMe = !string.IsNullOrEmpty(myId) && d.id == myId;
            if (isMe) myIndex = i;
            AddRow(i, d, isMe);
        }

        // 상위 10위 밖이면 전체(최대 100명) 목록에서 내 순위를 찾아본다.
        if (myIndex < 0 && !string.IsNullOrEmpty(myId))
        {
            for (int i = rowCount; i < data.Count; i++)
            {
                if (data[i].id == myId) { myIndex = i; break; }
            }
        }

        if (myIndex >= 0)
            ShowMyRank(myIndex + 1, data[myIndex].score);
        else
            ShowMyLocalFallback(); // 100위 밖이거나 아직 기록이 없음 → 로컬 최고점수로 등급만 표시
    }

    void AddRow(int index, RankingManager.RankingData d, bool isMe)
    {
        var row = new VisualElement();
        row.AddToClassList("ranking-row");
        if (index < 3) row.AddToClassList($"rank-{index + 1}");
        if (isMe) row.AddToClassList("ranking-row--me");

        var rank = new Label((index + 1).ToString());
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

    // 리더보드에서 내 순위/점수를 찾았을 때 — 정확한 순위 숫자를 표시.
    void ShowMyRank(int rank, int score)
    {
        if (myRankValue != null) myRankValue.text = rank.ToString();
        if (myScoreValue != null) myScoreValue.text = score.ToString();
        ApplyGrade(score);
    }

    // 리더보드(상위 100명) 밖이거나 조회 자체가 안 될 때 — 로컬에 저장된 최고점수로
    // 등급만 보여주고, 순위는 "-"로 둔다 (전체 순위를 알 방법이 없음).
    void ShowMyLocalFallback()
    {
        if (myRankValue != null) myRankValue.text = "-";

        if (SaveManager.Instance == null)
        {
            if (myScoreValue != null) myScoreValue.text = "0";
            ApplyGrade(0);
            return;
        }

        SaveManager.Instance.Load(data =>
        {
            int score = data != null ? data.bestScore : 0;
            if (myScoreValue != null) myScoreValue.text = score.ToString();
            ApplyGrade(score);
        });
    }

    void ApplyGrade(int score)
    {
        if (myGradeValue == null) return;

        myGradeValue.RemoveFromClassList("grade-bronze");
        myGradeValue.RemoveFromClassList("grade-silver");
        myGradeValue.RemoveFromClassList("grade-gold");
        myGradeValue.RemoveFromClassList("grade-diamond");

        string key;
        string cssClass;
        if (score >= diamondScore) { key = "ranking.grade_diamond"; cssClass = "grade-diamond"; }
        else if (score >= goldScore) { key = "ranking.grade_gold"; cssClass = "grade-gold"; }
        else if (score >= silverScore) { key = "ranking.grade_silver"; cssClass = "grade-silver"; }
        else if (score >= bronzeScore) { key = "ranking.grade_bronze"; cssClass = "grade-bronze"; }
        else { key = "ranking.grade_none"; cssClass = null; }

        myGradeValue.text = LocalizationManager.Get(key);
        if (cssClass != null) myGradeValue.AddToClassList(cssClass);
    }

    void ShowEmpty(bool show)
    {
        if (emptyLabel == null) return;
        if (show) emptyLabel.RemoveFromClassList("hidden");
        else emptyLabel.AddToClassList("hidden");
    }
}

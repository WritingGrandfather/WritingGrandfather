using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 도전 모드 게임오버 결과창 - 별점 대신 이번 판 점수와 최고 점수를 보여준다.
/// ChallengeScoreTracker.onResultReady를 구독해서 뜬다.
/// ChallengeScoreTracker와 같은 방식(RuntimeInitializeOnLoadMethod)으로 씬에 아무것도
/// 배치하지 않아도 ChallengeScene이 로드되면 자동으로 만들어진다.
/// </summary>
public class ChallengeResultScreenController : MonoBehaviour
{
    const string ChallengeSceneName = "ChallengeScene";
    const string LobbySceneName = "LobbyScene";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void AutoBootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded; // 중복 등록 방지
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != ChallengeSceneName) return;
        if (FindObjectOfType<ChallengeResultScreenController>() != null) return;
        var go = new GameObject("ChallengeResultScreen (auto)");
        go.AddComponent<ChallengeResultScreenController>();
    }

    ChallengeScoreTracker tracker;
    GameObject overlay;
    Text scoreLabel;
    Text bestLabel;

    void OnEnable()
    {
        // ChallengeScoreTracker도 같은 방식으로 자동 생성되므로, 씬 로드 시점에 이미 있을 수도
        // 있고 아직 없을 수도 있다 - 없으면 다음 프레임에 다시 찾는다.
        tracker = FindObjectOfType<ChallengeScoreTracker>();
        if (tracker == null)
        {
            StartCoroutine(WaitForTracker());
            return;
        }
        Subscribe();
    }

    System.Collections.IEnumerator WaitForTracker()
    {
        while (tracker == null)
        {
            yield return null;
            tracker = FindObjectOfType<ChallengeScoreTracker>();
        }
        Subscribe();
    }

    void Subscribe()
    {
        if (tracker.onResultReady == null) tracker.onResultReady = new ScoreResultEvent();
        tracker.onResultReady.AddListener(ShowResult);
    }

    void OnDisable()
    {
        if (tracker != null && tracker.onResultReady != null)
            tracker.onResultReady.RemoveListener(ShowResult);
        StopAllCoroutines();
    }

    void ShowResult(int finalScore, int bestScore)
    {
        BuildIfNeeded();
        bool isNewBest = finalScore >= bestScore;
        scoreLabel.text = $"이번 점수: {finalScore}";
        bestLabel.text = isNewBest ? $"신기록! 최고 점수: {bestScore}" : $"최고 점수: {bestScore}";
        overlay.SetActive(true);
    }

    void BuildIfNeeded()
    {
        if (overlay != null) return;

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvasGo = new GameObject("ChallengeResultCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000; // 다른 UI들보다 항상 위에 그려지도록

        overlay = new GameObject("Overlay", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(canvasGo.transform, false);
        var overlayRt = overlay.GetComponent<RectTransform>();
        overlayRt.anchorMin = Vector2.zero;
        overlayRt.anchorMax = Vector2.one;
        overlayRt.offsetMin = Vector2.zero;
        overlayRt.offsetMax = Vector2.zero;
        overlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

        var box = new GameObject("Box", typeof(RectTransform), typeof(Image));
        box.transform.SetParent(overlay.transform, false);
        var boxRt = box.GetComponent<RectTransform>();
        boxRt.anchorMin = boxRt.anchorMax = new Vector2(0.5f, 0.5f);
        boxRt.sizeDelta = new Vector2(560, 440);
        boxRt.anchoredPosition = Vector2.zero;
        box.GetComponent<Image>().color = new Color(1f, 0.99f, 0.97f, 1f);

        var titleLabel = CreateLabel(box.transform, "게임 오버", font, 44, FontStyle.Bold, new Color(0.35f, 0.24f, 0.16f));
        titleLabel.rectTransform.anchoredPosition = new Vector2(0, 160);

        scoreLabel = CreateLabel(box.transform, "", font, 32, FontStyle.Normal, new Color(0.35f, 0.24f, 0.16f));
        scoreLabel.rectTransform.anchoredPosition = new Vector2(0, 60);

        bestLabel = CreateLabel(box.transform, "", font, 32, FontStyle.Bold, new Color(0.84f, 0.42f, 0.23f));
        bestLabel.rectTransform.anchoredPosition = new Vector2(0, 0);

        var retryBtn = CreateButton(box.transform, "다시하기", font, new Vector2(-130, -160), new Color(0.84f, 0.42f, 0.23f));
        retryBtn.onClick.AddListener(OnRetry);

        var exitBtn = CreateButton(box.transform, "나가기", font, new Vector2(130, -160), new Color(0.47f, 0.47f, 0.47f));
        exitBtn.onClick.AddListener(OnExit);

        overlay.SetActive(false);
    }

    static Text CreateLabel(Transform parent, string text, Font font, int fontSize, FontStyle style, Color color)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(520, 60);

        var label = go.GetComponent<Text>();
        label.text = text;
        label.font = font;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.color = color;
        label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false;
        return label;
    }

    static Button CreateButton(Transform parent, string text, Font font, Vector2 pos, Color color)
    {
        var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(220, 80);
        rt.anchoredPosition = pos;

        var image = go.GetComponent<Image>();
        image.color = color;

        var button = go.GetComponent<Button>();

        var label = CreateLabel(go.transform, text, font, 30, FontStyle.Bold, Color.white);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.sizeDelta = Vector2.zero;
        label.rectTransform.anchoredPosition = Vector2.zero;

        return button;
    }

    void OnRetry()
    {
        Time.timeScale = 1f; // PlayerHp.Die()가 걸어 둔 정지를 풀어야 다음 씬이 멈춰있지 않는다
        SceneManager.LoadScene(ChallengeSceneName);
    }

    void OnExit()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(LobbySceneName);
    }
}

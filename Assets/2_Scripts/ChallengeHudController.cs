using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 도전 모드 화면 최상단 바 HUD - 플레이어 체력(하트, Fill/Null 스프라이트)과
/// 시간 제한 게이지를 절차적으로 만들어 표시한다.
/// PlayerHp.OnHpChanged / ChallengeSurvivalController.OnTimeChanged를 구독해서
/// 값이 바뀔 때마다 갱신하며, 하트 개수는 PlayerHp.maxHp에 맞춰 자동으로 정해진다.
///
/// heartFillSprite / heartNullSprite는 Assets/7_Sprites의 PlayerHearts_Fill,
/// PlayerHearts_Null로 씬에 미리 연결해 뒀다 - 비어 있다면 인스펙터에서 다시 드래그.
/// </summary>
public class ChallengeHudController : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] PlayerHp playerHp;
    [SerializeField] ChallengeSurvivalController survival;
    [Tooltip("초기화 버튼이 획을 지울 때 사용")]
    [SerializeField] DrowLine drawLine;
    [Tooltip("되돌리기/초기화 버튼을 이 글쓰기 카드 좌상단에 붙인다 - 비워두면 화면 좌상단 모서리에 붙는다")]
    [SerializeField] WritingCell writingCell;

    [Header("체력 하트 (Assets/7_Sprites) - 크기는 baseHeartSize에 화면 크기 배율을 곱해서 정한다")]
    [SerializeField] Sprite heartFillSprite;
    [SerializeField] Sprite heartNullSprite;
    [SerializeField] float baseHeartSize = 96f;
    [SerializeField] float baseHeartSpacing = 16f;

    [Header("시간 게이지 - 왼쪽에 고정된 채 오른쪽부터 줄어드는 막대 1개")]
    [SerializeField] Color timeBarColor = new Color(0.84f, 0.42f, 0.23f); // 테마 오렌지
    [SerializeField] Color timeBarBgColor = new Color(0.12f, 0.09f, 0.07f, 0.92f); // 꽉 찬 상태에서도 트랙이 보이도록 거의 불투명하게
    [SerializeField] Color timeBarBorderColor = new Color(1f, 0.92f, 0.75f, 0.9f);
    [SerializeField] float baseTimeBarHeight = 64f;
    [SerializeField] float baseTimeBarBorder = 4f;

    [Header("위치")]
    [Tooltip("상단 세이프에어리어 바로 아래에 추가로 두는 여백 - 화면 맨 위에 붙는다")]
    [SerializeField] float baseTopOffset = 20f;

    [Header("나가기 버튼")]
    [SerializeField] string lobbySceneName = "LobbyScene";
    [SerializeField] float baseExitButtonSize = 100f;
    [SerializeField] Color exitButtonColor = new Color(0.47f, 0.47f, 0.47f, 0.9f);

    [Header("되돌리기 / 초기화 버튼 - 정밀쓰기 씬과 다르게 반투명하게")]
    [SerializeField] float baseUndoResetButtonSize = 100f;
    [Tooltip("되돌리기-초기화 버튼 사이 간격 - 좁을수록 두 버튼이 붙어서 손이 덜 왔다갔다한다")]
    [SerializeField] float baseUndoResetGap = 16f;
    [Tooltip("글쓰기 카드 좌상단 모서리에서 안쪽으로 얼마나 들어와 겹치게 둘지")]
    [SerializeField] float baseUndoResetCardInset = 14f;
    [SerializeField] Color undoButtonColor = new Color(0.59f, 0.44f, 0.28f, 0.5f);
    [SerializeField] Color resetButtonColor = new Color(0.47f, 0.47f, 0.47f, 0.5f);

    // Canvas가 Constant Pixel Size라 실제 기기 해상도와 무관하게 1px=1px로 그려진다.
    // 디자인 기준 폭(1080px, 일반적인 세로 모바일 폭)을 기준으로 실제 화면 폭에 맞춰
    // 크기를 비례시킨다 - 안 그러면 고해상도 기기에서 하트/바가 화면에 비해 너무 작아진다.
    const float DesignWidth = 1080f;
    float UiScale => Mathf.Clamp(Screen.width / DesignWidth, 0.6f, 3f);

    float HeartSize => baseHeartSize * UiScale;
    float HeartSpacing => baseHeartSpacing * UiScale;
    float TimeBarHeight => baseTimeBarHeight * UiScale;
    float TimeBarBorder => baseTimeBarBorder * UiScale;
    float TopOffset => baseTopOffset * UiScale;
    float ExitButtonSize => baseExitButtonSize * UiScale;
    float UndoResetButtonSize => baseUndoResetButtonSize * UiScale;
    float UndoResetGap => baseUndoResetGap * UiScale;
    float UndoResetCardInset => baseUndoResetCardInset * UiScale;

    RectTransform bottomBar;
    RectTransform timeBarBorderRt;
    RectTransform timeBarBgRt;
    RectTransform timeBarFillRt;
    RectTransform exitButtonRt;
    RectTransform undoButtonRt;
    RectTransform resetButtonRt;
    Image[] hearts;
    Image timeBarFill;
    Text timeBarLabel;
    int builtHeartCount = -1;
    Vector2Int lastScreenSize;

    void Awake()
    {
        BuildBar();
    }

    void Update()
    {
        // 세이프 에어리어/화면 크기가 바뀔 수 있으므로(기기 회전, 디바이스 시뮬레이터 전환 등)
        // 바뀐 경우에만 다시 배치한다.
        var size = new Vector2Int(Screen.width, Screen.height);
        if (size != lastScreenSize)
        {
            lastScreenSize = size;
            LayoutBar();
        }
    }

    void OnEnable()
    {
        if (playerHp != null) playerHp.OnHpChanged += HandleHpChanged;
        if (survival != null) survival.OnTimeChanged += HandleTimeChanged;
        LocalizationManager.OnLanguageChanged += ApplyLocalization;
    }

    void OnDisable()
    {
        if (playerHp != null) playerHp.OnHpChanged -= HandleHpChanged;
        if (survival != null) survival.OnTimeChanged -= HandleTimeChanged;
        LocalizationManager.OnLanguageChanged -= ApplyLocalization;
    }

    void Start()
    {
        // Awake/OnEnable 순서상 값이 이미 바뀐 뒤에 구독했을 수 있으니, 시작 값을 한 번 직접 반영한다.
        if (playerHp != null) HandleHpChanged(playerHp.HP, playerHp.maxHp);
        if (survival != null) HandleTimeChanged(survival.Remaining, survival.StartTime);
    }

    // 화면 최상단에 고정되는 바 컨테이너 + 시간 게이지(배경/채움)를 만든다.
    // 하트는 최대 체력을 알아야 개수를 정할 수 있어 HandleHpChanged에서 만든다.
    void BuildBar()
    {
        var bar = new GameObject("ChallengeTopBar", typeof(RectTransform));
        bar.transform.SetParent(transform, false);
        bottomBar = bar.GetComponent<RectTransform>();
        // 화면 맨 위에 붙인다 (예전엔 하단에 붙였음 - 요청에 따라 최상단으로 변경).
        bottomBar.anchorMin = new Vector2(0f, 1f);
        bottomBar.anchorMax = new Vector2(1f, 1f);
        bottomBar.pivot = new Vector2(0.5f, 1f);

        // 밝은 테두리 - 시간이 다 차 있어(fillAmount=1) 트랙이 안 보일 때도 바 전체 길이가
        // 뚜렷하게 인식되도록 배경보다 살짝 크게 감싼다.
        // 요청에 따라 체력 슬라이드(시간바)가 스프라이트 체력(하트)보다 위에 오도록
        // 컨테이너 상단에 붙인다 (하트는 아래쪽, BuildHearts 참고).
        var border = new GameObject("TimeBarBorder", typeof(RectTransform), typeof(Image));
        border.transform.SetParent(bar.transform, false);
        timeBarBorderRt = border.GetComponent<RectTransform>();
        timeBarBorderRt.anchorMin = new Vector2(0f, 1f);
        timeBarBorderRt.anchorMax = new Vector2(1f, 1f);
        timeBarBorderRt.pivot = new Vector2(0.5f, 1f);
        timeBarBorderRt.anchoredPosition = new Vector2(0f, 0f);
        border.GetComponent<Image>().color = timeBarBorderColor;

        var bg = new GameObject("TimeBarBg", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(border.transform, false);
        timeBarBgRt = bg.GetComponent<RectTransform>();
        timeBarBgRt.anchorMin = new Vector2(0f, 0f);
        timeBarBgRt.anchorMax = new Vector2(1f, 1f);
        timeBarBgRt.offsetMin = Vector2.zero;
        timeBarBgRt.offsetMax = Vector2.zero;
        bg.GetComponent<Image>().color = timeBarBgColor;

        // 왼쪽에 고정된 채 오른쪽부터 줄어드는 막대 1개. Image.Type.Filled 대신 RectTransform의
        // anchorMax.x를 직접 조절한다 - 매 프레임 앵커를 다시 계산해 배치하므로 값이 바뀌면
        // 반드시 다시 그려진다(Filled 모드의 메시 재생성 타이밍에 의존하지 않는 더 확실한 방식).
        var fill = new GameObject("TimeBarFill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(bg.transform, false);
        timeBarFillRt = fill.GetComponent<RectTransform>();
        timeBarFillRt.anchorMin = new Vector2(0f, 0f);
        timeBarFillRt.anchorMax = new Vector2(1f, 1f);
        timeBarFillRt.offsetMin = Vector2.zero;
        timeBarFillRt.offsetMax = Vector2.zero;
        timeBarFill = fill.GetComponent<Image>();
        timeBarFill.color = timeBarColor;

        // 막대가 실제로 줄어드는지 한눈에 확인할 수 있도록 남은 초를 숫자로도 같이 보여준다.
        var label = new GameObject("TimeBarLabel", typeof(RectTransform), typeof(Text));
        label.transform.SetParent(border.transform, false);
        var labelRt = label.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        timeBarLabel = label.GetComponent<Text>();
        // OS 폰트 이름에 의존하는 CreateDynamicFontFromOSFont는 안드로이드 등에서
        // "Arial"이 없으면 실패할 수 있어, 항상 존재하는 유니티 내장 폰트를 쓴다.
        timeBarLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        timeBarLabel.alignment = TextAnchor.MiddleCenter;
        timeBarLabel.color = Color.white;
        timeBarLabel.fontStyle = FontStyle.Bold;
        timeBarLabel.raycastTarget = false;
        timeBarLabel.text = "";

        // 나가기 버튼 - 화면 우상단 고정, HUD 바와는 별개로 독립 배치한다.
        // 텍스트는 정밀쓰기 씬과 같은 키를 재사용 - 언어가 바뀌면 ApplyLocalization()에서 갱신된다.
        exitButtonRt = CreateCornerButton("ExitButton", LocalizationManager.Get("precise_writing.exit_button"), exitButtonColor, new Vector2(1f, 1f), OnExitClicked);

        // 되돌리기 / 초기화 버튼 - 화면 좌상단, 정밀쓰기 씬과 달리 반투명하게.
        undoButtonRt = CreateCornerButton("UndoButton", LocalizationManager.Get("precise_writing.undo_button"), undoButtonColor, new Vector2(0f, 1f), OnUndoClicked);
        resetButtonRt = CreateCornerButton("ResetButton", LocalizationManager.Get("precise_writing.reset_button"), resetButtonColor, new Vector2(0f, 1f), OnResetClicked);

        lastScreenSize = new Vector2Int(Screen.width, Screen.height);
        LayoutBar();
        ApplyLocalization();
    }

    // 나가기/되돌리기/초기화 버튼 글자를 현재 언어로 갱신한다 - 정밀쓰기 씬과 같은
    // 로컬라이제이션 키를 재사용한다(도전 모드 전용 새 키를 따로 만들 필요 없음).
    void ApplyLocalization()
    {
        SetButtonText(exitButtonRt, LocalizationManager.Get("precise_writing.exit_button"));
        SetButtonText(undoButtonRt, LocalizationManager.Get("precise_writing.undo_button"));
        SetButtonText(resetButtonRt, LocalizationManager.Get("precise_writing.reset_button"));
    }

    static void SetButtonText(RectTransform buttonRt, string text)
    {
        if (buttonRt == null) return;
        var label = buttonRt.GetComponentInChildren<Text>();
        if (label != null) label.text = text;
    }

    // 화면 모서리에 붙는 정사각형 아이콘 버튼(배경 + 흰 글씨 라벨) 하나를 만든다.
    // corner: (0,1)=좌상단, (1,1)=우상단처럼 anchor/pivot으로 어느 모서리에 붙을지 정한다.
    RectTransform CreateCornerButton(string name, string label, Color color, Vector2 corner, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = corner;
        rt.anchorMax = corner;
        rt.pivot = corner;
        go.GetComponent<Image>().color = color;
        go.GetComponent<Button>().onClick.AddListener(onClick);

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        var text = labelGo.GetComponent<Text>();
        text.text = label;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.fontStyle = FontStyle.Bold;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        return rt;
    }

    void OnExitClicked() => SceneManager.LoadScene(lobbySceneName);

    void OnUndoClicked() => UndoManager.Instance?.Undo();

    void OnResetClicked()
    {
        drawLine?.ClearAll();
        UndoManager.Instance?.Clear();
    }

    // Screen.safeArea 기준으로 상단/좌우 여백을 다시 계산한다 - 노치/둥근 모서리에
    // 하트나 시간 바가 가려지지 않도록. 화면 크기가 바뀔 때마다(Update에서) 다시 호출된다.
    void LayoutBar()
    {
        if (bottomBar == null) return;

        var sa = Screen.safeArea;
        float topInset = Screen.height - sa.yMax; // 상단 세이프에어리어(노치 등) 높이
        float sideInset = Mathf.Max(sa.xMin, Screen.width - sa.xMax);
        float margin = HeartSpacing;

        // 나가기 버튼이 우상단에 따로 떠 있어서, 시간바/하트 바 오른쪽을 그만큼 당겨 겹치지 않게 한다
        // (되돌리기/초기화는 이제 글쓰기 카드 쪽에 붙으므로 왼쪽은 더 이상 좁힐 필요 없음).
        float rightGap = ExitButtonSize + margin;
        bottomBar.sizeDelta = new Vector2(-sideInset * 2f - rightGap, HeartSize + TimeBarHeight + margin * 3f);
        // 위쪽 앵커/피벗이라 아래로 내려가는 방향(음수)으로 오프셋을 준다.
        bottomBar.anchoredPosition = new Vector2(-rightGap * 0.5f, -(topInset + TopOffset));

        if (timeBarBorderRt != null)
        {
            timeBarBorderRt.sizeDelta = new Vector2(-margin * 2f, TimeBarHeight);

            // 배경(bg)을 테두리(border)보다 안쪽으로 밀어 넣어 테두리가 프레임처럼 보이게 한다.
            float border = TimeBarBorder;
            timeBarBgRt.offsetMin = new Vector2(border, border);
            timeBarBgRt.offsetMax = new Vector2(-border, -border);
        }

        if (timeBarLabel != null)
            timeBarLabel.fontSize = Mathf.RoundToInt(32f * UiScale);

        if (exitButtonRt != null)
        {
            float rightInset = Screen.width - sa.xMax;
            exitButtonRt.sizeDelta = new Vector2(ExitButtonSize, ExitButtonSize);
            exitButtonRt.anchoredPosition = new Vector2(-(rightInset + TopOffset), -(topInset + TopOffset));

            var label = exitButtonRt.GetComponentInChildren<Text>();
            if (label != null) label.fontSize = Mathf.RoundToInt(26f * UiScale);
        }

        // 되돌리기 + 초기화를 나란히 배치한다 - 글쓰기 카드가 있으면 그 좌상단 모서리에
        // 살짝 겹치게 붙이고(정밀쓰기 씬과 같은 방식), 없으면 화면 좌상단 모서리를 쓴다.
        if (undoButtonRt != null && resetButtonRt != null)
        {
            float ursize = UndoResetButtonSize;
            float startX, startY;
            if (TryGetCardTopLeft(out float cardLeft, out float cardTop))
            {
                startX = cardLeft + UndoResetCardInset;
                startY = cardTop + UndoResetCardInset;
            }
            else
            {
                startX = sideInset + TopOffset;
                startY = topInset + TopOffset;
            }

            undoButtonRt.sizeDelta = new Vector2(ursize, ursize);
            undoButtonRt.anchoredPosition = new Vector2(startX, -startY);

            resetButtonRt.sizeDelta = new Vector2(ursize, ursize);
            resetButtonRt.anchoredPosition = new Vector2(startX + ursize + UndoResetGap, -startY);

            var undoLabel = undoButtonRt.GetComponentInChildren<Text>();
            if (undoLabel != null) undoLabel.fontSize = Mathf.RoundToInt(22f * UiScale);
            var resetLabel = resetButtonRt.GetComponentInChildren<Text>();
            if (resetLabel != null) resetLabel.fontSize = Mathf.RoundToInt(22f * UiScale);
        }

        PositionHearts();
    }

    // writingCell의 월드 사각형 좌상단 모서리를 화면 기준 "왼쪽에서부터 거리"/"위에서부터 거리"로
    // 변환한다 - 정밀쓰기의 SyncWritingCellToGuideBox()와 반대 방향 변환.
    bool TryGetCardTopLeft(out float distFromLeft, out float distFromTop)
    {
        distFromLeft = 0f;
        distFromTop = 0f;

        var cam = Camera.main;
        if (writingCell == null || cam == null) return false;

        Rect r = writingCell.WorldRect;
        Vector3 screenPos = cam.WorldToScreenPoint(new Vector3(r.xMin, r.yMax, 0f));

        // WorldToScreenPoint는 화면 픽셀(왼쪽 아래 원점)을 반환하므로, "위에서부터 거리"로
        // 쓰려면 세로만 뒤집으면 된다 (가로는 원점이 이미 왼쪽이라 그대로 사용).
        distFromLeft = screenPos.x;
        distFromTop = Screen.height - screenPos.y;
        return true;
    }

    void PositionHearts()
    {
        if (hearts == null) return;

        float size = HeartSize;
        float spacing = HeartSpacing;
        for (int i = 0; i < hearts.Length; i++)
        {
            var rt = hearts[i].rectTransform;
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(spacing + i * (size + spacing), 0f);
        }
    }

    // 최대 체력만큼 하트 슬롯을 만든다 (기본 3칸 - PlayerHp.maxHp 기준).
    void BuildHearts(int count)
    {
        if (hearts != null)
            foreach (var h in hearts)
                if (h != null) Destroy(h.gameObject);

        hearts = new Image[count];
        for (int i = 0; i < count; i++)
        {
            var heartGo = new GameObject($"Heart_{i}", typeof(RectTransform), typeof(Image));
            heartGo.transform.SetParent(bottomBar, false);
            var rt = heartGo.GetComponent<RectTransform>();
            // 시간바(위)보다 아래쪽에 오도록 컨테이너 하단에 붙인다.
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);

            var img = heartGo.GetComponent<Image>();
            img.sprite = heartFillSprite;
            img.preserveAspect = true;
            hearts[i] = img;
        }

        builtHeartCount = count;
        PositionHearts();
    }

    void HandleHpChanged(int current, int max)
    {
        if (hearts == null || builtHeartCount != max)
            BuildHearts(Mathf.Max(max, 1));

        for (int i = 0; i < hearts.Length; i++)
            hearts[i].sprite = i < current ? heartFillSprite : heartNullSprite;
    }

    void HandleTimeChanged(float current, float max)
    {
        if (timeBarFillRt == null || max <= 0f) return;
        float t = Mathf.Clamp01(current / max);
        timeBarFillRt.anchorMax = new Vector2(t, 1f);
        if (timeBarLabel != null) timeBarLabel.text = Mathf.CeilToInt(current).ToString();
    }
}

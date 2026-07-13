using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

/// <summary>
/// 한 화면에 단어/낱말 하나만 크게 띄우고,
/// 넘기면(지금은 Enter, 나중엔 필기 인식 성공) 현재 글자가 왼쪽으로 밀려나가고
/// 다음 글자가 오른쪽에서 부드럽게 들어온다.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private Font gungseoFont; // 궁서체 폰트 파일(.ttf/.otf)을 그대로 드래그

    [Header("모드")]
    [SerializeField] private GameMode mode = GameMode.Letter;

    [Header("스테이지 (순서대로 진행, 비워두면 아래 랜덤 풀 사용)")]
    [SerializeField] private StageData[] stages;

    [Header("표시")]
    [Tooltip("출제 단어 라벨 표시 여부")]
    [SerializeField] private bool showWordLabel = true;
    [SerializeField] private int fontSize = 140;
    [Range(0f, 1f)][SerializeField] private float verticalRatio = 0.35f; // 화면 높이 대비 글자 위치

    [Header("애니메이션")]
    [SerializeField] private float slideDuration = 0.4f; // 넘어가는 시간(초)

    [Header("스테이지 전환 연출")]
    [SerializeField] private int bannerFontSize = 90;
    [SerializeField] private float bannerFadeDuration = 0.3f; // 배너 페이드 인/아웃 시간
    [SerializeField] private float bannerHoldDuration = 1.2f; // 배너가 떠 있는 시간

    private Label currentLabel;
    private bool transitioning;
    private string pendingBanner; // 다음 글자 진입 전에 보여줄 스테이지 배너 텍스트

    // 스테이지 진행 상태
    private int stageIndex;
    private readonly System.Collections.Generic.List<string> stageQueue = new(); // 셔플된 남은 텍스트
    private string[] currentStageTexts; // 현재 스테이지의 전체 텍스트 (필기 인식 후보군용)

    /// <summary>현재 스테이지의 전체 글자 목록. 필기 인식의 후보군으로 사용 (없으면 null)</summary>
    public string[] CurrentCandidates => currentStageTexts;

    /// <summary>현재 화면에 떠 있는 단어/낱말 (필기 인식 채점에 사용)</summary>
    public string CurrentText { get; private set; }

    /// <summary>현재 모드</summary>
    public GameMode Mode => mode;

    /// <summary>현재 스테이지 번호 (1부터), 스테이지 미사용이면 0</summary>
    public int CurrentStage => UseStages ? stageIndex + 1 : 0;

    /// <summary>스테이지가 바뀔 때 호출 (UI에서 구독)</summary>
    public System.Action<int, StageData> OnStageChanged;

    /// <summary>모든 스테이지를 끝냈을 때 호출</summary>
    public System.Action OnAllStagesCleared;

    private bool UseStages => stages != null && stages.Length > 0;

    // 캐시하지 않고 매번 가져온다 (UIDocument가 패널을 재생성하면 캐시가 무효화됨)
    private VisualElement Root => uiDocument.rootVisualElement;

    private void OnEnable()
    {
        // root가 화면 전체를 채우도록 강제 (기본은 내용물 크기라서 높이가 0이 됨)
        VisualElement root = Root;
        root.style.position = Position.Absolute;
        root.style.top = 0;
        root.style.bottom = 0;
        root.style.left = 0;
        root.style.right = 0;
    }

    private void Start()
    {
        if (gungseoFont == null)
            Debug.LogWarning("[EnemySpawner] 폰트가 비어 있습니다. 기본 폰트에는 한글이 없어 글자가 안 보일 수 있어요.");
    }

    private void Update()
    {
        // 레이아웃 계산 전이면 대기
        if (float.IsNaN(Root.resolvedStyle.width) || Root.resolvedStyle.width <= 0f)
            return;

        // 첫 단어 표시 (첫 스테이지 배너 포함)
        if (currentLabel == null && !transitioning)
        {
            transitioning = true;
            ShowIncoming(PickText());
            return;
        }

        // 임시 입력: Enter = 성공 처리 (나중에 필기 인식이 Advance()를 직접 호출)
        if (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)
            Advance();
    }

    /// <summary>다음 단어로 넘긴다. 현재 글자는 왼쪽으로, 다음 글자는 오른쪽에서 진입.</summary>
    public void Advance()
    {
        if (transitioning || currentLabel == null) return;
        transitioning = true;

        float width = Root.resolvedStyle.width;
        int durationMs = Mathf.RoundToInt(slideDuration * 1000f);

        // 현재 글자: 왼쪽 화면 밖으로
        Label old = currentLabel;
        old.experimental.animation
            .Start(0f, 1f, durationMs, (ve, t) =>
                ve.style.translate = new Translate(-width * Easing.OutCubic(t), 0))
            .OnCompleted(() => old.RemoveFromHierarchy());

        // 다음 글자 준비 (스테이지가 바뀌면 배너 연출 후 진입)
        currentLabel = null;
        ShowIncoming(PickText());
    }

    /// <summary>다음 텍스트를 표시한다. 스테이지 배너가 대기 중이면 배너 먼저.</summary>
    private void ShowIncoming(string text)
    {
        CurrentText = text;

        if (pendingBanner != null)
        {
            string banner = pendingBanner;
            pendingBanner = null;
            ShowStageBanner(banner, () => SlideInWord(text));
        }
        else
        {
            SlideInWord(text);
        }
    }

    /// <summary>글자를 오른쪽 화면 밖에서 중앙으로 밀어 넣는다.</summary>
    private void SlideInWord(string text)
    {
        float width = Root.resolvedStyle.width;
        int durationMs = Mathf.RoundToInt(slideDuration * 1000f);

        currentLabel = CreateLabel(text);
        currentLabel.style.translate = new Translate(width, 0);
        Root.Add(currentLabel);
        currentLabel.experimental.animation
            .Start(0f, 1f, durationMs, (ve, t) =>
                ve.style.translate = new Translate(width * (1f - Easing.OutCubic(t)), 0))
            .OnCompleted(() => transitioning = false);
    }

    /// <summary>스테이지 배너 연출: 확대되며 페이드 인 → 잠시 유지 → 페이드 아웃.</summary>
    private void ShowStageBanner(string bannerText, System.Action onDone)
    {
        var banner = new Label(bannerText);
        banner.AddToClassList("stage-banner"); // USS로 꾸밀 때 사용
        banner.style.position = Position.Absolute;
        banner.style.left = 0;
        banner.style.right = 0;
        banner.style.top = Length.Percent(verticalRatio * 100f);
        banner.style.unityTextAlign = TextAnchor.MiddleCenter;
        banner.style.fontSize = bannerFontSize;
        banner.style.color = Color.white;
        banner.style.opacity = 0f;
        banner.style.whiteSpace = WhiteSpace.Normal;
        if (gungseoFont != null)
            banner.style.unityFontDefinition = new StyleFontDefinition(gungseoFont);
        Root.Add(banner);

        int fadeMs = Mathf.RoundToInt(bannerFadeDuration * 1000f);
        long holdMs = (long)(bannerHoldDuration * 1000f);

        // 페이드 인 + 살짝 확대
        banner.experimental.animation
            .Start(0f, 1f, fadeMs, (ve, t) =>
            {
                float e = Easing.OutCubic(t);
                ve.style.opacity = e;
                ve.style.scale = new Scale(Vector2.one * (0.8f + 0.2f * e));
            })
            .OnCompleted(() =>
            {
                // 유지 후 페이드 아웃
                banner.schedule.Execute(() =>
                {
                    banner.experimental.animation
                        .Start(0f, 1f, fadeMs, (ve, t) => ve.style.opacity = 1f - t)
                        .OnCompleted(() =>
                        {
                            banner.RemoveFromHierarchy();
                            onDone?.Invoke();
                        });
                }).StartingIn(holdMs);
            });
    }

    /// <summary>모드 변경. 스테이지를 처음부터 다시 시작하고 새 단어로 교체된다.</summary>
    public void SetMode(GameMode newMode)
    {
        mode = newMode;
        stageIndex = 0;
        stageQueue.Clear();
        if (currentLabel != null && !transitioning)
            Advance();
    }

    /// <summary>현재 스테이지의 텍스트들을 셔플해서 큐에 채운다.</summary>
    private void FillStageQueue()
    {
        stageQueue.Clear();
        string[] texts = stages[stageIndex].GetTexts(mode);
        currentStageTexts = texts;
        stageQueue.AddRange(texts);

        // Fisher-Yates 셔플
        for (int i = stageQueue.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (stageQueue[i], stageQueue[j]) = (stageQueue[j], stageQueue[i]);
        }
    }

    private Label CreateLabel(string text)
    {
        var label = new Label(text);
        label.AddToClassList("current-word"); // USS로 꾸밀 때 사용
        if (!showWordLabel)
            label.style.display = DisplayStyle.None; // 본보기가 표시를 대신 (로직은 유지)
        label.style.position = Position.Absolute;
        label.style.left = 0;
        label.style.right = 0; // 가로 전체 폭 → 텍스트 가운데 정렬로 중앙 배치
        label.style.top = Length.Percent(verticalRatio * 100f);
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        label.style.fontSize = fontSize;
        label.style.color = Color.white;
        if (gungseoFont != null)
            label.style.unityFontDefinition = new StyleFontDefinition(gungseoFont);
        return label;
    }

    /// <summary>다음 텍스트를 스테이지 큐에서 뽑는다.</summary>
    private string PickText()
    {
        if (!UseStages)
        {
            Debug.LogWarning("[EnemySpawner] 스테이지가 비어 있습니다. Stages 배열에 StageData를 넣어주세요.");
            return "가";
        }
        return PickFromStage();
    }

    /// <summary>스테이지 큐에서 하나 꺼낸다. 큐가 비면 다음 스테이지로.</summary>
    private string PickFromStage()
    {
        // 빈 스테이지는 건너뛰며 큐 채우기
        while (stageQueue.Count == 0)
        {
            if (stageIndex >= stages.Length)
            {
                OnAllStagesCleared?.Invoke();
                Debug.Log("[EnemySpawner] 모든 스테이지 클리어! 마지막 스테이지를 반복합니다.");
                stageIndex = stages.Length - 1; // 일단 마지막 스테이지 반복
            }

            FillStageQueue();

            if (stageQueue.Count > 0)
            {
                StageData stage = stages[stageIndex];
                pendingBanner = string.IsNullOrEmpty(stage.stageName)
                    ? $"{stageIndex + 1}단계"
                    : $"{stageIndex + 1}단계\n{stage.stageName}";
                OnStageChanged?.Invoke(stageIndex + 1, stage);
                Debug.Log($"[EnemySpawner] 스테이지 {stageIndex + 1} 시작: {stage.stageName} ({stageQueue.Count}개)");
            }
            else
            {
                stageIndex++; // 현재 모드에 해당하는 텍스트가 없는 스테이지는 스킵
                if (stageIndex >= stages.Length) return CurrentText ?? "가"; // 전부 비어 있으면 안전 탈출
            }
        }

        string text = stageQueue[stageQueue.Count - 1];
        stageQueue.RemoveAt(stageQueue.Count - 1);

        // 이번 스테이지 마지막 항목을 꺼냈으면 다음 진입 때 다음 스테이지로
        if (stageQueue.Count == 0)
            stageIndex++;

        return text;
    }

}

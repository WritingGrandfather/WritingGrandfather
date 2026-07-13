using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace WritingGrandfather.UI.PreciseWriting
{
    [RequireComponent(typeof(UIDocument))]
    public class PreciseWritingUIController : MonoBehaviour
    {
        // 위→아래로 실제 그려진 크기를 이어 붙여 배치한다 (guide-box가 폭 기준 정사각형이라
        // 세로 공간이 남을 수 있으므로, 고정 비율 밴드 대신 이전 요소의 실제 하단을 기준으로 다음 요소를 배치).
        private const float TopBarTopFrac = 0.04f;
        private const float TopBarHeightFrac = 0.085f;
        private const float GuideGapFrac = 0.035f;
        private const float GuideWidthFrac = 0.92f;
        private const float ButtonGapFrac = 0.035f;
        private const float CompleteHeightFrac = 0.095f;
        private const float ToggleGapFrac = 0.03f;
        private const float ToggleHeightFrac = 0.08f;

        [SerializeField] private Font koreanFont;

        [Tooltip("단어가 바뀌거나 다시 시작할 때 이전 획을 지우기 위한 참조")]
        [SerializeField] private DrowLine drawLine;

        [Tooltip("AI 채점 파이프라인 (WritingCell + StrokeCapture + CellCapture + Evaluator를 조율). 비워두면 데모 점수로 대체.")]
        [SerializeField] private WritingFeedbackController feedbackController;

        [Tooltip("실제 캡처 영역을 정의하는 WritingCell — guide-box와 매 프레임 위치/크기를 맞춘다.")]
        [SerializeField] private WritingCell writingCell;

        [Tooltip("교정 겹쳐보기 (선택) — 연결하면 카드가 표시됐다 사라진 뒤에 다음 글자로 넘어간다")]
        [SerializeField] private CompareOverlay compareOverlay;

        [Tooltip("연습할 스테이지 데이터 — 연결하면 아래 데모 단어 대신 스테이지 글자들을 순서대로 출제")]
        [SerializeField] private StageData[] stages;

        [Tooltip("스테이지에서 낱말(Letter)/단어(Word) 중 어느 목록을 쓸지")]
        [SerializeField] private GameMode stageMode = GameMode.Letter;

        [Tooltip("스테이지 하나를 클리어하는 데 필요한 라운드(글자) 수 - 실제 스테이지 데이터에 더 많은 글자가 있어도 이 개수만 쓰고 다음 스테이지로 넘어간다")]
        [SerializeField] private int roundsPerStage = 5;

        [Tooltip("데모용 연습 단어 목록 — 스테이지가 비어 있을 때만 사용")]
        [SerializeField] private string[] practiceWords = { "가", "나", "다" };

        [Tooltip("결과 화면에 표시할 데모 점수(%) — feedbackController가 비어있을 때만 사용")]
        [SerializeField] private int demoScorePercent = 85;

        private VisualElement root;
        private VisualElement safeArea;
        private VisualElement writingScreen;
        private VisualElement analyzingScreen;
        private VisualElement resultScreen;
        private VisualElement topBar;
        private VisualElement guideBox;
        private VisualElement bottomBar;
        private VisualElement drawMaskTop;
        private VisualElement drawMaskBottom;
        private VisualElement drawMaskLeft;
        private VisualElement drawMaskRight;
        private Label ghostCharacterLabel;
        private VisualElement strokeOrderLayer;
        private Toggle toggleShowCharacter;
        private Toggle toggleShowStrokeOrder;
        private Button completeButton;
        private Button continueButton;
        private Button retryStageButton;
        private Button resultExitButton;
        private Button resetButton;
        private Button undoButton;
        private Button topBarStopButton;
        private Label currentWordLabel;
        private Label wordProgressLabel;
        private Label strokeFeedbackLabel;
        private VisualElement guideCrossH;
        private VisualElement guideCrossV;
        private Label resultStrokeOrderScoreLabel;
        private Label resultSimilarityScoreLabel;
        private Label resultPositionScoreLabel;
        private Label resultMessageLabel;
        private Label resultTitleLabel;
        private Label resultStrokeOrderCaptionLabel;
        private Label resultSimilarityCaptionLabel;
        private Label resultPositionCaptionLabel;
        private Label analyzingLabel;
        private Label toggleShowCharacterLabel;
        private Label toggleShowStrokeOrderLabel;
        private VisualElement resultStarsRow;

        private int wordIndex;

        // 스테이지 전환 연출용 — practiceWords[i]가 속한 스테이지 번호(1부터)와 이름
        private int[] wordStageNums;
        private string[] wordStageNames;
        private int lastBannerStage = -1;

        // 무한모드: 마지막 스테이지까지 다 돌면 다시 1번째 스테이지 데이터로 순환하되,
        // 화면에 보여주는 스테이지 번호는 계속 누적해서 커진다(1~5, 6~10, 11~15, ...).
        private int stageLoopCount;

        // 점수 누적 (다시하기로 초기화되기 전까지 계속 쌓이며, 그만하기를 누르면 평균으로 표시)
        private int stageSimilaritySum;
        private int stageOrderSum;
        private int stagePositionSum;
        private int stageScoredCount;
        private string lastFeedbackMessage = "";

        // ── 실시간 획순 미리보기/교정 ──────────────────────────────────
        // (StrokeOrderValidator의 표준 필순 계획을 그려서 쓰기 전엔 미리보기 화살표,
        //  획순을 틀리면 그 획만 다시 알려주는 교정 화살표 + 글자 전체 빨간색으로 표시)
        private static readonly Color PreviewArrowColor = new Color(214f / 255f, 108f / 255f, 58f / 255f); // 테마 오렌지
        private static readonly Color CorrectionArrowColor = new Color(0.86f, 0.15f, 0.15f); // FeedbackDisplayUI.failColor과 동일 계열

        private List<StrokeOrderValidator.PlanStep> strokeOrderPlan;
        private int strokeOrderPlanIndex;
        private bool strokeOrderErrorActive;

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            Cache();
            ApplyFont();
            toggleShowCharacterLabel = SetupToggleButton(toggleShowCharacter);
            toggleShowStrokeOrderLabel = SetupToggleButton(toggleShowStrokeOrder);
            Bind();
            ApplyToggles();
            BuildGuideCross();

            // 스테이지 데이터가 연결돼 있으면 데모 단어 대신 스테이지 글자들로 교체
            // (스테이지 순서는 유지, 각 스테이지 안에서는 셔플. 단어별 소속 스테이지를 기록해 전환 연출에 사용)
            if (stages != null && stages.Length > 0)
            {
                var list = new System.Collections.Generic.List<string>();
                var nums = new System.Collections.Generic.List<int>();
                var names = new System.Collections.Generic.List<string>();

                for (int si = 0; si < stages.Length; si++)
                {
                    if (stages[si] == null) continue;
                    var texts = new System.Collections.Generic.List<string>(stages[si].GetTexts(stageMode));
                    for (int i = texts.Count - 1; i > 0; i--) // Fisher-Yates 셔플
                    {
                        int k = Random.Range(0, i + 1);
                        (texts[i], texts[k]) = (texts[k], texts[i]);
                    }
                    // 스테이지 데이터에 글자가 더 있어도 roundsPerStage개만 써서, 그만큼만
                    // 채우면 스테이지가 클리어되도록 한다 (나머지는 이번 판에서 쓰지 않음).
                    int count = Mathf.Min(roundsPerStage, texts.Count);
                    for (int i = 0; i < count; i++)
                    {
                        list.Add(texts[i]);
                        nums.Add(si + 1);
                        names.Add(stages[si].stageName);
                    }
                }

                if (list.Count > 0)
                {
                    practiceWords = list.ToArray();
                    wordStageNums = nums.ToArray();
                    wordStageNames = names.ToArray();
                }
            }

            wordIndex = 0;
            lastBannerStage = -1;
            UpdateWordLabel();

            ApplyLocalization();
            LocalizationManager.OnLanguageChanged += ApplyLocalization;

            safeArea?.RegisterCallback<GeometryChangedEvent>(OnLayoutGeo);
            writingScreen?.RegisterCallback<GeometryChangedEvent>(OnLayoutGeo);
            guideBox?.RegisterCallback<GeometryChangedEvent>(OnGuideBoxGeo);
            feedbackController?.onFeedback?.AddListener(OnFeedbackReceived);
            root.schedule.Execute(ApplyLayout).StartingIn(0);

            // PointerMoveEvent로 미리 계산해 둔 값 대신, 실제로 그리기를 시작하려는 그
            // 순간 DrowLine이 직접 이 델리게이트를 호출해 판정하게 한다 - 자세한 이유는
            // CanDrawAtScreenPoint 주석 참고. drawingEnabled는 더 이상 위치별로 매 프레임
            // 갱신하지 않으므로(그 역할을 canDrawAt이 대신함), OnDisable에서 꺼뒀던 것을
            // 다시 켜서 "전체 그리기 허용" 스위치로만 사용한다.
            if (drawLine != null)
            {
                drawLine.drawingEnabled = true;
                drawLine.canDrawAt = CanDrawAtScreenPoint;
                drawLine.OnStrokeCompleted += HandleStrokeCompleted;
            }

            // UndoManager.Awake()가 이 컴포넌트의 OnEnable()보다 먼저 실행된다는
            // 보장이 없다(Unity는 서로 다른 GameObject 간 실행 순서를 보장하지
            // 않음) - 여기서 바로 UndoManager.Instance를 구독하면 아직 null이라
            // 구독 자체가 실패하고, 되돌리기 버튼이 영원히 비활성 상태로 굳어버렸다.
            // 한 프레임 미뤄서 씬의 모든 Awake가 끝난 뒤에 구독하도록 한다.
            root.schedule.Execute(SubscribeUndoManager).StartingIn(0);
        }

        private void OnDisable()
        {
            safeArea?.UnregisterCallback<GeometryChangedEvent>(OnLayoutGeo);
            writingScreen?.UnregisterCallback<GeometryChangedEvent>(OnLayoutGeo);
            guideBox?.UnregisterCallback<GeometryChangedEvent>(OnGuideBoxGeo);
            feedbackController?.onFeedback?.RemoveListener(OnFeedbackReceived);
            LocalizationManager.OnLanguageChanged -= ApplyLocalization;
            if (drawLine != null)
            {
                drawLine.drawingEnabled = false;
                drawLine.canDrawAt = null;
                drawLine.OnStrokeCompleted -= HandleStrokeCompleted;
            }
            if (UndoManager.Instance != null)
                UndoManager.Instance.OnStateChanged -= RefreshUndoButton;
        }

        private void SubscribeUndoManager()
        {
            if (UndoManager.Instance == null) return;
            UndoManager.Instance.OnStateChanged += RefreshUndoButton;
            RefreshUndoButton();
        }

        // 되돌릴 획이 없을 때는 되돌리기 버튼을 비활성화해서, 이미 비어있는 히스토리를
        // 향해 눌러도 아무 반응 없어 보이는 대신 명확하게 눌리지 않게 한다.
        // UndoManager.Instance가 아직 없을 때는(초기화 순서 문제) 섣불리 비활성화하지
        // 않고 그대로 둔다 - 클릭 시점엔 Instance가 다시 지연 평가되므로 문제없다.
        private void RefreshUndoButton()
        {
            if (undoButton == null || UndoManager.Instance == null) return;
            undoButton.SetEnabled(UndoManager.Instance.CanUndo);
        }

        // guide-box의 화면상 위치/크기가 바뀔 때마다 WritingCell의 월드 좌표를 그대로 따라가게 해서
        // AI 캡처(CellCapture/StrokeCapture)가 실제 손글씨가 그려지는 영역과 정확히 일치하도록 한다.
        // guide-box 스크린 좌표 → Camera.main.ScreenToWorldPoint 변환은 DrowLine이 잉크를 찍을 때 쓰는 것과
        // 동일한 방식이라 서로 어긋나지 않는다.
        private void OnGuideBoxGeo(GeometryChangedEvent evt) => SyncWritingCellToGuideBox();

        private void SyncWritingCellToGuideBox()
        {
            if (writingCell == null || guideBox == null) return;
            var cam = Camera.main;
            if (cam == null) return;

            var panel = guideBox.panel;
            if (panel == null) return;
            float panelW = panel.visualTree.resolvedStyle.width;
            float panelH = panel.visualTree.resolvedStyle.height;
            if (panelW <= 0f || panelH <= 0f || Screen.width <= 0 || Screen.height <= 0) return;

            Rect r = guideBox.worldBound;
            if (r.width <= 0f || r.height <= 0f) return;

            // guide-box.worldBound는 UI Toolkit 패널 좌표계인데, PanelSettings가
            // Constant Physical Size 등 1:1이 아닌 스케일 모드면 실제 화면 픽셀과 어긋난다.
            // SafeAreaApplier/LobbyController와 같은 방식(화면 크기 대비 패널 크기 비율)으로
            // 패널 좌표 → 화면 픽셀로 변환한 뒤 world 좌표로 옮긴다.
            // 패널 좌표는 top-left 원점, Pointer.current/ScreenToWorldPoint는 bottom-left
            // 원점이라 Y를 뒤집어야 한다.
            float sx = Screen.width / panelW;
            float sy = Screen.height / panelH;

            Vector2 screenTopLeft = new Vector2(r.xMin * sx, Screen.height - r.yMin * sy);
            Vector2 screenBottomRight = new Vector2(r.xMax * sx, Screen.height - r.yMax * sy);

            Vector3 worldA = cam.ScreenToWorldPoint(new Vector3(screenTopLeft.x, screenTopLeft.y, 0));
            Vector3 worldB = cam.ScreenToWorldPoint(new Vector3(screenBottomRight.x, screenBottomRight.y, 0));

            writingCell.transform.position = new Vector3(
                (worldA.x + worldB.x) * 0.5f,
                (worldA.y + worldB.y) * 0.5f,
                writingCell.transform.position.z);
            writingCell.size = new Vector2(
                Mathf.Abs(worldB.x - worldA.x),
                Mathf.Abs(worldA.y - worldB.y));
        }

        // DrowLine.OnDrawStart()/DrawLoop()가 "지금 이 화면 좌표에서 그려도 되는가"를 물어볼 때마다
        // 호출된다 - guide-box/safe-area 밖이거나 버튼(되돌리기/초기화) 위면 거부한다.
        // 예전에는 이 판정을 PointerMoveEvent로 미리 계산해 둔 drawingEnabled 값으로 대신했는데,
        // 새 Input System의 액션 콜백(OnDrawStart)과 UI Toolkit의 PointerMoveEvent 처리가 같은
        // 프레임 안에서 어느 쪽이 먼저 실행되는지 보장이 없어, 버튼을 막 눌렀을 때 아직 갱신되지
        // 않은(한 프레임 전) 값을 참조해 클릭이 씹히거나 반대로 버튼 위에서 그림이 같이 그려지는
        // 문제가 있었다. 이 메서드는 물어보는 그 순간 직접 새로 계산하므로 그런 지연이 없다.
        private bool CanDrawAtScreenPoint(Vector2 screenPos)
        {
            if (guideBox == null || root == null || root.panel == null) return false;

            bool onWritingScreen = writingScreen != null && !writingScreen.ClassListContains("hidden");
            if (!onWritingScreen) return false;

            var panel = root.panel;
            float panelW = panel.visualTree.resolvedStyle.width;
            float panelH = panel.visualTree.resolvedStyle.height;
            if (panelW <= 0f || panelH <= 0f || Screen.width <= 0 || Screen.height <= 0) return false;

            // screenPos(Pointer.current 기준, bottom-left 원점) → 패널 좌표(top-left 원점).
            // SyncWritingCellToGuideBox()의 반대 방향 변환.
            float sx = Screen.width / panelW;
            float sy = Screen.height / panelH;
            Vector2 panelPos = new Vector2(screenPos.x / sx, (Screen.height - screenPos.y) / sy);

            bool insideSafeArea = safeArea == null || safeArea.worldBound.Contains(panelPos);
            bool insideGuideBox = guideBox.worldBound.Contains(panelPos);
            if (!insideSafeArea || !insideGuideBox) return false;

            return !(panel.Pick(panelPos) is Button);
        }

        private void Cache()
        {
            safeArea = root.Q("safe-area");
            writingScreen = root.Q("writing-screen");
            analyzingScreen = root.Q("analyzing-screen");
            resultScreen = root.Q("result-screen");
            topBar = root.Q("top-bar");
            guideBox = root.Q("guide-box");
            bottomBar = root.Q("bottom-bar");
            drawMaskTop = root.Q("draw-mask-top");
            drawMaskBottom = root.Q("draw-mask-bottom");
            drawMaskLeft = root.Q("draw-mask-left");
            drawMaskRight = root.Q("draw-mask-right");
            ghostCharacterLabel = root.Q<Label>("ghost-character-label");
            strokeOrderLayer = root.Q("stroke-order-layer");
            toggleShowCharacter = root.Q<Toggle>("toggle-show-character");
            toggleShowStrokeOrder = root.Q<Toggle>("toggle-show-stroke-order");
            completeButton = root.Q<Button>("complete-button");
            continueButton = root.Q<Button>("continue-button");
            retryStageButton = root.Q<Button>("retry-stage-button");
            resultExitButton = root.Q<Button>("result-exit-button");
            resetButton = root.Q<Button>("reset-button");
            undoButton = root.Q<Button>("undo-button");
            topBarStopButton = root.Q<Button>("top-bar-stop-button");
            currentWordLabel = root.Q<Label>("current-word-label");
            wordProgressLabel = root.Q<Label>("word-progress-label");
            strokeFeedbackLabel = root.Q<Label>("stroke-feedback-label");
            // top-bar 아래에 겹쳐 뜨는 오버레이라 그림 그리기 입력을 가리면 안 된다.
            if (strokeFeedbackLabel != null) strokeFeedbackLabel.pickingMode = PickingMode.Ignore;
            guideCrossH = root.Q("guide-cross-h");
            guideCrossV = root.Q("guide-cross-v");
            resultStrokeOrderScoreLabel = root.Q<Label>("result-stroke-order-score");
            resultSimilarityScoreLabel = root.Q<Label>("result-similarity-score");
            resultPositionScoreLabel = root.Q<Label>("result-position-score");
            resultMessageLabel = root.Q<Label>("result-message");
            resultTitleLabel = root.Q<Label>("result-title-label");
            resultStrokeOrderCaptionLabel = root.Q<Label>("result-stroke-order-label");
            resultSimilarityCaptionLabel = root.Q<Label>("result-similarity-label");
            resultPositionCaptionLabel = root.Q<Label>("result-position-label");
            analyzingLabel = root.Q<Label>("analyzing-label");
            resultStarsRow = root.Q<VisualElement>("result-stars-row");
        }

        private void ApplyLocalization()
        {
            if (resetButton != null) resetButton.text = LocalizationManager.Get("precise_writing.reset_button");
            if (undoButton != null) undoButton.text = LocalizationManager.Get("precise_writing.undo_button");
            if (completeButton != null) completeButton.text = LocalizationManager.Get("precise_writing.complete_button");
            if (continueButton != null) continueButton.text = LocalizationManager.Get("precise_writing.continue_button");
            if (retryStageButton != null) retryStageButton.text = LocalizationManager.Get("precise_writing.retry_stage_button");
            if (resultExitButton != null) resultExitButton.text = LocalizationManager.Get("precise_writing.exit_button");
            if (topBarStopButton != null) topBarStopButton.text = LocalizationManager.Get("precise_writing.stop_button");

            if (analyzingLabel != null) analyzingLabel.text = LocalizationManager.Get("precise_writing.analyzing_label");

            if (resultTitleLabel != null) resultTitleLabel.text = LocalizationManager.Get("precise_writing.result_title");
            if (resultStrokeOrderCaptionLabel != null) resultStrokeOrderCaptionLabel.text = LocalizationManager.Get("precise_writing.result_stroke_order_label");
            if (resultSimilarityCaptionLabel != null) resultSimilarityCaptionLabel.text = LocalizationManager.Get("precise_writing.result_similarity_label");
            if (resultPositionCaptionLabel != null) resultPositionCaptionLabel.text = LocalizationManager.Get("precise_writing.result_position_label");

            if (toggleShowCharacterLabel != null) toggleShowCharacterLabel.text = LocalizationManager.Get("precise_writing.toggle_show_character");
            if (toggleShowStrokeOrderLabel != null) toggleShowStrokeOrderLabel.text = LocalizationManager.Get("precise_writing.toggle_show_stroke_order");
        }

        private void ApplyFont()
        {
            if (root == null || koreanFont == null) return;
            var def = new StyleFontDefinition(koreanFont);
            root.style.unityFontDefinition = def;
            root.Query<TextElement>().ForEach(el => el.style.unityFontDefinition = def);
        }

        private void Bind()
        {
            toggleShowCharacter?.RegisterValueChangedCallback(_ => ApplyToggles());
            toggleShowStrokeOrder?.RegisterValueChangedCallback(_ => ApplyToggles());
            completeButton?.RegisterCallback<ClickEvent>(_ => OnCompleteClicked());
            resetButton?.RegisterCallback<ClickEvent>(_ => ClearStrokes());
            // 되돌리기는 보통 방금 틀린 획을 지우려는 것이므로, 그 획이 사라진 뒤에는
            // 에러 표시(빨간색+교정 화살표)를 걷어낸다 - 다음 획순 판정은 그대로 이어짐
            // (planIndex는 애초에 틀렸을 때 진행시키지 않았으므로 되돌린 뒤에도 여전히 맞음).
            undoButton?.RegisterCallback<ClickEvent>(_ =>
            {
                UndoManager.Instance?.Undo();
                if (strokeOrderErrorActive)
                {
                    strokeOrderErrorActive = false;
                    drawLine?.SetInkColor(Color.black);
                    RefreshStrokeOrderOverlay();
                }
            });
            // 계속 하기: 점수창을 닫고 멈췄던 글자부터 바로 이어서 쓴다 (단어 인덱스/누적 점수 그대로).
            continueButton?.RegisterCallback<ClickEvent>(_ => Show(writingScreen));
            // 다시하기: 누적 점수와 진행도를 전부 초기화하고 첫 글자부터 새로 시작한다.
            retryStageButton?.RegisterCallback<ClickEvent>(_ => RestartSession());
            resultExitButton?.RegisterCallback<ClickEvent>(_ =>
                UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyScene"));
            // 그만하기: 라운드 수 제한 없이 계속 쓰다가, 사용자가 직접 멈출 때만 지금까지의
            // 누적 점수로 점수창을 띄운다 - 더 이상 5단계 등 정해진 라운드 끝에서 자동으로 뜨지 않는다.
            topBarStopButton?.RegisterCallback<ClickEvent>(_ => ShowResult(lastFeedbackMessage));
        }

        // 다시하기: 진행도와 누적 점수를 전부 초기화하고 첫 글자로 되돌아간다.
        private void RestartSession()
        {
            wordIndex = 0;
            stageLoopCount = 0;
            lastBannerStage = -1;
            stageSimilaritySum = stageOrderSum = stagePositionSum = stageScoredCount = 0;
            lastFeedbackMessage = "";
            UpdateStrokeFeedbackLabel("", true);
            UpdateWordLabel();
            ClearStrokes();
            Show(writingScreen);
        }

        // 획을 전부 지울 때(초기화/다음 단어/다시 하기)는 UndoManager 히스토리도 같이
        // 비워야 한다 - 안 그러면 되돌리기 버튼이 이미 풀로 반환된(사라진) 이전 단어의
        // 획을 되살리려다 어긋난 동작을 하게 된다.
        private void ClearStrokes()
        {
            drawLine?.ClearAll();
            UndoManager.Instance?.Clear();
            ResetStrokeOrderState();
        }

        // 완료 클릭: 현재 글자를 채점한다. 점수는 계속 누적되고, 다음 글자로 바로 넘어간다 -
        // 점수창은 라운드 수와 상관없이 그만하기를 눌렀을 때만 뜬다(무제한 라운드).
        private void OnCompleteClicked()
        {
            Show(analyzingScreen);

            if (feedbackController != null && writingCell != null)
            {
                writingCell.targetText = practiceWords != null && practiceWords.Length > 0
                    ? practiceWords[wordIndex]
                    : "";
                feedbackController.RequestFeedback();
                return;
            }

            // feedbackController가 연결 안 돼 있으면(테스트용) 데모 점수로 대체
            root.schedule.Execute(() =>
                HandleScored(demoScorePercent, demoScorePercent, demoScorePercent, "(데모 점수 — 채점 미연결)", demoScorePercent >= 50)
            ).StartingIn(600);
        }

        // WritingFeedbackController.onFeedback 콜백 — 채점 결과가 도착하면 호출된다.
        // 세 항목은 각각 다른 방식으로 계산된 독립적인 점수다 (-1 = 이번엔 계산 안 됨 → 종합 점수로 대체 표시).
        private void OnFeedbackReceived(HandwritingFeedback feedback)
        {
            if (feedback == null) return;

            int similarity = feedback.similarityScore >= 0 ? feedback.similarityScore : feedback.score;
            int strokeOrder = feedback.strokeOrderScore >= 0 ? feedback.strokeOrderScore : feedback.score;
            int position = feedback.positionScore >= 0 ? feedback.positionScore : feedback.score;

            // 교정 겹쳐보기가 연결돼 있으면: 카드가 표시됐다 사라진 뒤에 다음 글자로 진행
            float delayMs = compareOverlay != null ? compareOverlay.TotalDuration * 1000f : 0f;
            if (delayMs > 0f && root != null)
            {
                Show(writingScreen); // 분석 화면을 닫고 쓰기 화면으로 — 칸 위의 교정 카드가 보이게
                SetGuideVisible(false); // 안내선·본보기를 잠시 숨겨 교정 카드가 또렷하게 보이게
                root.schedule.Execute(() =>
                {
                    SetGuideVisible(true);
                    HandleScored(similarity, strokeOrder, position, feedback.message, feedback.passed);
                }).StartingIn((long)delayMs);
                return;
            }

            HandleScored(similarity, strokeOrder, position, feedback.message, feedback.passed);
        }

        // 교정 카드 표시 중 십자 점선/본보기 글자 숨김·복원 (복원 시 토글 상태 존중)
        private void SetGuideVisible(bool visible)
        {
            var d = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (guideCrossH != null) guideCrossH.style.display = d;
            if (guideCrossV != null) guideCrossV.style.display = d;

            if (visible)
            {
                ApplyToggles(); // 본보기 글자는 사용자의 토글 설정대로 복원
            }
            else if (ghostCharacterLabel != null)
            {
                ghostCharacterLabel.style.display = DisplayStyle.None;
            }
        }

        // 글자 하나 채점 완료 → 누적하고 다음 글자로. 첫 바퀴(stageLoopCount==0, 스테이지1~5를
        // 아직 다 안 돌았을 때)엔 스테이지가 바뀔 때마다 자동으로 점수창을 띄운다. 5단계까지
        // 다 돌고 나면(무한모드) 더 이상 자동으로 뜨지 않고, 그만하기를 눌렀을 때만 뜬다.
        private void HandleScored(int similarity, int strokeOrder, int position, string message, bool good)
        {
            stageSimilaritySum += similarity;
            stageOrderSum += strokeOrder;
            stagePositionSum += position;
            stageScoredCount++;
            lastFeedbackMessage = message;
            UpdateStrokeFeedbackLabel(message, good);

            bool hasStages = stages != null && stages.Length > 0;
            bool lastOverall = practiceWords == null || wordIndex >= practiceWords.Length - 1;
            bool stageEnd = hasStages && stageLoopCount == 0 &&
                (lastOverall ||
                 (wordStageNums != null && wordIndex + 1 < wordStageNums.Length &&
                  wordStageNums[wordIndex + 1] != wordStageNums[wordIndex]));
            // wordIndex를 진행시키기 전에 "방금 끝난" 스테이지 번호를 미리 구해 둔다 -
            // 진행 후에는 wordIndex가 다음 스테이지(또는 순환된 처음)를 가리키게 되기 때문.
            int completedStageNum = stageEnd ? GetDisplayStageNum(wordIndex) : 0;

            wordIndex++;
            if (practiceWords == null || wordIndex >= practiceWords.Length)
            {
                wordIndex = 0;
                if (hasStages) stageLoopCount++; // 5단계까지 다 돌았으면 여기서 무한모드 진입
            }

            if (stageEnd)
            {
                ShowResult(message, completedStageNum);
                return;
            }

            UpdateWordLabel();
            ClearStrokes();
            Show(writingScreen);
        }

        // 점수창: 스테이지 클리어로 자동으로 뜬 경우엔 "n단계 완료!" 제목을, 그만하기로
        // 수동으로 띄운 경우(completedStageNum==0, 보통 무한모드 중)엔 기본 "결과" 제목을 쓴다.
        private void ShowResult(string lastMessage, int completedStageNum = 0)
        {
            int n = Mathf.Max(1, stageScoredCount);
            int similarityAvg = stageSimilaritySum / n;
            int orderAvg = stageOrderSum / n;
            int positionAvg = stagePositionSum / n;
            if (resultSimilarityScoreLabel != null) resultSimilarityScoreLabel.text = $"{similarityAvg}%";
            if (resultStrokeOrderScoreLabel != null) resultStrokeOrderScoreLabel.text = $"{orderAvg}%";
            if (resultPositionScoreLabel != null) resultPositionScoreLabel.text = $"{positionAvg}%";
            if (resultMessageLabel != null) resultMessageLabel.text = lastMessage;

            if (resultTitleLabel != null)
            {
                resultTitleLabel.text = completedStageNum > 0
                    ? $"{completedStageNum}단계 완료!"
                    : LocalizationManager.Get("precise_writing.result_title");
            }

            // 세 점수의 평균으로 3개 만점 별점을 매긴다: 90%↑ 3개, 60%↑ 2개, 30%↑ 1개, 그 미만 0개.
            int overallAvg = (similarityAvg + orderAvg + positionAvg) / 3;
            int starCount = overallAvg >= 90 ? 3 : overallAvg >= 60 ? 2 : overallAvg >= 30 ? 1 : 0;
            RenderStars(starCount);

            // 다음 구간을 위해 누적 초기화 (점수창이 뜰 때마다 "여기까지의 점수"를 보여주는 개념)
            stageSimilaritySum = stageOrderSum = stagePositionSum = stageScoredCount = 0;

            Show(resultScreen);
        }

        // wordStageNums[idx](1~stages.Length, 루프마다 초기화됨)에 stageLoopCount * stages.Length를
        // 더해서, 무한모드로 계속 순환해도 화면에는 1,2,3...으로 끊임없이 커지는 번호로 보이게 한다.
        private int GetDisplayStageNum(int idx)
        {
            if (wordStageNums == null || idx < 0 || idx >= wordStageNums.Length) return 0;
            int stagesPerLoop = stages != null ? stages.Length : 0;
            return wordStageNums[idx] + stageLoopCount * stagesPerLoop;
        }

        // 별 3개를 그려서 획득한 개수만큼 채우고, 나머지는 빈 별로 표시한다.
        private void RenderStars(int filledCount)
        {
            if (resultStarsRow == null) return;

            resultStarsRow.Clear();
            for (int i = 0; i < 3; i++)
                resultStarsRow.Add(CreateStar(90f, i < filledCount));
        }

        private static VisualElement CreateStar(float size, bool filled)
        {
            var star = new VisualElement();
            star.style.width = size;
            star.style.height = size;
            star.style.marginLeft = 6;
            star.style.marginRight = 6;
            star.generateVisualContent += ctx => DrawStar(ctx, size, filled);
            return star;
        }

        // 폰트에 ★/☆ 글리프가 없을 수 있어 텍스트 대신 Painter2D로 5각별 폴리곤을 직접 그린다.
        // 채워진 별은 테마 오렌지로 꽉 채우고, 빈 별은 테두리만(반투명 브라운) 그려 구분한다.
        private static void DrawStar(MeshGenerationContext ctx, float size, bool filled)
        {
            var painter = ctx.painter2D;
            var fillColor = new Color(214f / 255f, 108f / 255f, 58f / 255f);
            var emptyOutline = new Color(150f / 255f, 111f / 255f, 71f / 255f, 0.45f);

            float cx = size * 0.5f;
            float cy = size * 0.5f;
            float outerR = size * 0.5f;
            float innerR = outerR * 0.42f;

            painter.BeginPath();
            for (int i = 0; i < 10; i++)
            {
                float angle = -Mathf.PI / 2f + i * Mathf.PI / 5f;
                float r = (i % 2 == 0) ? outerR : innerR;
                var p = new Vector2(cx + r * Mathf.Cos(angle), cy + r * Mathf.Sin(angle));
                if (i == 0) painter.MoveTo(p);
                else painter.LineTo(p);
            }
            painter.ClosePath();

            if (filled)
            {
                painter.fillColor = fillColor;
                painter.Fill();
            }
            else
            {
                painter.strokeColor = emptyOutline;
                painter.lineWidth = 4f;
                painter.Stroke();
            }
        }

        // 직전 글자 채점의 피드백 문구(주로 획순 오류 안내)를 상단바에 표시한다.
        // 다음 글자를 쓰는 동안에도 계속 보여서, 뭘 고쳐야 하는지 보면서 다시 쓸 수 있게 한다.
        private void UpdateStrokeFeedbackLabel(string message, bool good)
        {
            if (strokeFeedbackLabel == null) return;
            bool has = !string.IsNullOrEmpty(message);
            strokeFeedbackLabel.text = has ? message : "";
            strokeFeedbackLabel.EnableInClassList("hidden", !has);
            strokeFeedbackLabel.EnableInClassList("stroke-feedback-label--good", has && good);
            strokeFeedbackLabel.EnableInClassList("stroke-feedback-label--bad", has && !good);
        }

        private void UpdateWordLabel()
        {
            if (practiceWords == null || practiceWords.Length == 0) return;

            string word = practiceWords[wordIndex];
            if (currentWordLabel != null) currentWordLabel.text = word;
            if (ghostCharacterLabel != null) ghostCharacterLabel.text = word;
            RebuildStrokeOrderPlan(word);

            // 무제한 라운드라 "n / 총개수"처럼 정해진 끝이 있는 표시 대신, 루프 횟수까지 반영해
            // 계속 커지기만 하는 라운드 번호를 보여준다.
            if (wordProgressLabel != null)
                wordProgressLabel.text = $"{stageLoopCount * practiceWords.Length + wordIndex + 1}";

            // 스테이지가 바뀌는 첫 글자에서 전환 연출 - 첫 바퀴(stageLoopCount==0, 아직 5단계까지
            // 다 안 돌았을 때)에만 띄우고, 무한모드(stageLoopCount>=1)에서는 더 이상 띄우지 않는다.
            if (stageLoopCount == 0 && wordStageNums != null && wordIndex < wordStageNums.Length && wordStageNums[wordIndex] != lastBannerStage)
            {
                lastBannerStage = wordStageNums[wordIndex];
                ShowStageBanner(GetDisplayStageNum(wordIndex), wordStageNames[wordIndex]);
            }
        }

        // 스테이지 전환 배너: 살짝 확대되며 페이드 인 → 잠시 유지 → 페이드 아웃
        private void ShowStageBanner(int stageNum, string stageName)
        {
            if (root == null) return;

            var banner = new Label(string.IsNullOrEmpty(stageName) ? $"{stageNum}단계" : $"{stageNum}단계\n{stageName}");
            banner.pickingMode = PickingMode.Ignore;
            banner.style.position = Position.Absolute;
            banner.style.left = 0;
            banner.style.right = 0;
            banner.style.top = Length.Percent(38);
            banner.style.unityTextAlign = TextAnchor.MiddleCenter;
            banner.style.whiteSpace = WhiteSpace.Normal;
            banner.style.fontSize = 64;
            banner.style.color = new Color(0.45f, 0.32f, 0.24f); // 테마에 맞는 짙은 갈색
            banner.style.opacity = 0f;
            if (koreanFont != null)
                banner.style.unityFontDefinition = new StyleFontDefinition(koreanFont);
            root.Add(banner);

            const int fadeMs = 300;
            const int holdMs = 1000;

            banner.experimental.animation
                .Start(0f, 1f, fadeMs, (ve, t) =>
                {
                    ve.style.opacity = t;
                    ve.style.scale = new Scale(Vector2.one * (0.85f + 0.15f * t));
                })
                .OnCompleted(() =>
                {
                    banner.schedule.Execute(() =>
                    {
                        banner.experimental.animation
                            .Start(0f, 1f, fadeMs, (ve, t) => ve.style.opacity = 1f - t)
                            .OnCompleted(() => banner.RemoveFromHierarchy());
                    }).StartingIn(holdMs);
                });
        }

        // Toggle의 기본 체크박스 비주얼은 USS만으로는 완전히 숨기기 어려워(내부 구조가 테마에 따라 달라짐)
        // 체크박스 파트를 아예 계층에서 제거하고, 우리 라벨을 직접 붙여 버튼처럼 보이게 만든다.
        // 텍스트는 여기서 바로 채우지 않고 ApplyLocalization()에서 채운다 - 만들어진
        // Label을 돌려줘서 언어가 바뀔 때도 같은 요소의 text만 갱신하면 되게 한다.
        private static Label SetupToggleButton(Toggle toggle)
        {
            if (toggle == null) return null;

            toggle.Q(className: Toggle.inputUssClassName)?.RemoveFromHierarchy();
            toggle.text = null;

            var lbl = new Label();
            lbl.AddToClassList("toggle-btn-label");
            lbl.pickingMode = PickingMode.Ignore;
            toggle.Add(lbl);
            return lbl;
        }

        // guide-box 안의 가로/세로 점선 십자를 작은 점 세그먼트로 절차적으로 구성한다.
        // (UI Toolkit USS에는 border-style: dashed가 없어 세그먼트 방식으로 대체)
        private void BuildGuideCross()
        {
            BuildDashSegments(guideCrossH, 14);
            BuildDashSegments(guideCrossV, 14);
        }

        private static void BuildDashSegments(VisualElement container, int count)
        {
            if (container == null) return;

            container.Clear();
            for (int i = 0; i < count; i++)
            {
                var dot = new VisualElement();
                dot.AddToClassList("guide-cross-dot");
                container.Add(dot);
            }
        }

        private void OnLayoutGeo(GeometryChangedEvent evt) => ApplyLayout();

        private void ApplyLayout()
        {
            if (writingScreen == null) return;
            if (!TryGetLayoutSize(writingScreen, out var w, out var h)) return;

            float topBarTop = TopBarTopFrac * h;
            float topBarHeight = TopBarHeightFrac * h;
            PlaceRect(topBar, 0.06f * w, topBarTop, 0.88f * w, topBarHeight);

            float guideTop = topBarTop + topBarHeight + GuideGapFrac * h;
            float guideSize = w * GuideWidthFrac;
            float guideLeft = (w - guideSize) * 0.5f;
            PlaceRect(guideBox, guideLeft, guideTop, guideSize, guideSize);
            float guideBottom = guideTop + guideSize;
            float guideRight = guideLeft + guideSize;

            // guide-box 밖으로 삐져나온 손글씨를 가리는 4방향 마스크 (위/아래는 화면 전체 폭, 좌/우는 guide-box 높이만큼만)
            PlaceRect(drawMaskTop, 0, 0, w, guideTop);
            PlaceRect(drawMaskBottom, 0, guideBottom, w, h - guideBottom);
            PlaceRect(drawMaskLeft, 0, guideTop, guideLeft, guideSize);
            PlaceRect(drawMaskRight, guideRight, guideTop, w - guideRight, guideSize);

            float completeTop = guideBottom + ButtonGapFrac * h;
            float completeHeight = CompleteHeightFrac * h;
            PlaceRect(completeButton, 0.06f * w, completeTop, 0.88f * w, completeHeight);
            float completeBottom = completeTop + completeHeight;

            float toggleTop = completeBottom + ToggleGapFrac * h;
            float toggleHeight = ToggleHeightFrac * h;
            PlaceRect(bottomBar, 0.06f * w, toggleTop, 0.88f * w, toggleHeight);

            if (ghostCharacterLabel != null)
                ghostCharacterLabel.style.fontSize = guideSize * 0.52f;
        }

        /// <summary>
        /// absolute-only 부모는 contentRect 가 0 일 수 있어 resolvedStyle → layout 순으로 읽음.
        /// </summary>
        private static bool TryGetLayoutSize(VisualElement el, out float w, out float h)
        {
            w = el.resolvedStyle.width;
            h = el.resolvedStyle.height;
            if (w > 1f && h > 1f) return true;

            var layout = el.layout;
            w = layout.width;
            h = layout.height;
            if (w > 1f && h > 1f) return true;

            var content = el.contentRect;
            w = content.width;
            h = content.height;
            return w > 1f && h > 1f;
        }

        private static void PlaceRect(VisualElement el, float x, float y, float width, float height)
        {
            if (el == null) return;

            el.style.position = Position.Absolute;
            el.style.left = x;
            el.style.top = y;
            el.style.width = width;
            el.style.height = height;
        }

        private void ApplyToggles()
        {
            var showChar = toggleShowCharacter == null || toggleShowCharacter.value;
            var showStroke = toggleShowStrokeOrder != null && toggleShowStrokeOrder.value;

            if (ghostCharacterLabel != null)
                ghostCharacterLabel.style.display = showChar ? DisplayStyle.Flex : DisplayStyle.None;

            strokeOrderLayer?.EnableInClassList("hidden", !showStroke);

            toggleShowCharacter?.EnableInClassList("toggle-btn--on", showChar);
            toggleShowStrokeOrder?.EnableInClassList("toggle-btn--on", showStroke);
        }

        // 글자가 바뀔 때마다 그 글자의 표준 필순 계획을 새로 만든다. 여러 글자(단어 모드)는
        // 지원하지 않는다 - 배치 채점(WritingFeedbackController)도 한 글자짜리만 획순을 검사한다.
        private void RebuildStrokeOrderPlan(string word)
        {
            strokeOrderPlan = (!string.IsNullOrEmpty(word) && word.Length == 1)
                ? StrokeOrderValidator.BuildStandardPlan(word[0])
                : null;
            ResetStrokeOrderState();
        }

        // 획을 전부 지웠을 때(초기화/다음 글자/다시 하기) 진행 상태를 처음으로 되돌린다.
        private void ResetStrokeOrderState()
        {
            strokeOrderPlanIndex = 0;
            strokeOrderErrorActive = false;
            RefreshStrokeOrderOverlay();
        }

        // DrowLine이 획 하나를 다 그렸을 때(펜을 뗀 순간) 호출된다.
        // 다음에 나와야 할 표준 획(strokeOrderPlan[strokeOrderPlanIndex])과 위치·방향을 대조한다.
        private void HandleStrokeCompleted(List<Vector2> worldPoints)
        {
            if (strokeOrderPlan == null || writingCell == null) return;
            if (worldPoints == null || worldPoints.Count < 2) return;
            if (strokeOrderPlanIndex >= strokeOrderPlan.Count) return; // 이미 다 맞음 - 여분의 획은 검사하지 않음

            Rect rect = writingCell.WorldRect;
            if (rect.width <= 0f || rect.height <= 0f) return;

            // 칸(WritingCell) 기준 0~1 정규화 (y: 위→아래) — StrokeCapture.GetCellNormalizedStrokes와 동일한 변환.
            // (전체 잉크의 바운딩 박스가 아니라 "칸" 기준인 이유: 계획의 자모 배치 좌표가
            //  글자 전체가 칸을 채운다는 가정으로 만들어져 있어, 한 획만으로도 즉시 비교 가능)
            Vector2 nStart = NormalizeToCell(worldPoints[0], rect);
            Vector2 nEnd = NormalizeToCell(worldPoints[worldPoints.Count - 1], rect);
            Vector2 nCenter = Vector2.zero;
            foreach (var p in worldPoints) nCenter += NormalizeToCell(p, rect);
            nCenter /= worldPoints.Count;

            var expected = strokeOrderPlan[strokeOrderPlanIndex];

            float dx = Mathf.Max(expected.box.xMin - nCenter.x, 0f, nCenter.x - expected.box.xMax);
            float dy = Mathf.Max(expected.box.yMin - nCenter.y, 0f, nCenter.y - expected.box.yMax);
            float gate = Mathf.Sqrt(dx * dx + dy * dy);

            Vector2 strokeDir = nEnd - nStart;
            Vector2 expectedDir = expected.end - expected.start;

            bool matched = false;
            if (gate <= 0.25f && strokeDir.magnitude > 0.001f && expectedDir.magnitude > 0.001f)
            {
                float dot = Vector2.Dot(strokeDir.normalized, expectedDir.normalized);
                matched = dot > 0.25f; // 위치가 맞고, 방향도 크게 반대가 아님
            }

            if (matched)
            {
                strokeOrderPlanIndex++;
                if (strokeOrderErrorActive)
                {
                    strokeOrderErrorActive = false;
                    drawLine?.SetInkColor(Color.black);
                }
            }
            else
            {
                strokeOrderErrorActive = true;
                drawLine?.SetInkColor(CorrectionArrowColor);
            }

            RefreshStrokeOrderOverlay();
        }

        private static Vector2 NormalizeToCell(Vector2 world, Rect cellRect) => new Vector2(
            (world.x - cellRect.xMin) / cellRect.width,
            1f - (world.y - cellRect.yMin) / cellRect.height);

        // stroke-order-layer 내용을 현재 상태에 맞게 다시 그린다. 표시 여부(hidden 클래스)는
        // ApplyToggles가 "획 순서 표시" 토글로 이미 제어하므로, 여기서는 내용만 갱신한다.
        //  - 획순 오류 상태: 틀린(다음에 나와야 할) 획 하나만 빨간 교정 화살표로 표시
        //  - 아직 한 획도 안 그림: 전체 필순을 번호+화살표로 미리보기
        //  - 정상 진행 중(일부만 쓰고 오류 없음): 방해되지 않도록 아무것도 표시하지 않음
        private void RefreshStrokeOrderOverlay()
        {
            if (strokeOrderLayer == null) return;
            strokeOrderLayer.Clear();
            if (strokeOrderPlan == null) return;

            if (strokeOrderErrorActive)
            {
                if (strokeOrderPlanIndex < strokeOrderPlan.Count)
                    AddStrokeArrow(strokeOrderPlan[strokeOrderPlanIndex], 0, CorrectionArrowColor);
            }
            else if (strokeOrderPlanIndex == 0)
            {
                for (int i = 0; i < strokeOrderPlan.Count; i++)
                    AddStrokeArrow(strokeOrderPlan[i], i + 1, PreviewArrowColor);
            }
        }

        // 화살표(선+화살촉) 하나와, number>0이면 순서 번호 배지를 stroke-order-layer에 추가한다.
        private void AddStrokeArrow(StrokeOrderValidator.PlanStep step, int number, Color color)
        {
            Vector2 start = step.start;
            Vector2 end = step.end;

            var arrow = new VisualElement();
            arrow.pickingMode = PickingMode.Ignore;
            arrow.style.position = Position.Absolute;
            arrow.style.left = 0; arrow.style.right = 0; arrow.style.top = 0; arrow.style.bottom = 0;
            // left/right/top/bottom만으로는 width/height가 0으로 잡혀 generateVisualContent의
            // contentRect가 0이 되는 경우가 있어(그러면 화살표가 전혀 그려지지 않음), 확실하게
            // 100%로 명시한다.
            arrow.style.width = Length.Percent(100f);
            arrow.style.height = Length.Percent(100f);
            arrow.generateVisualContent += ctx => DrawStrokeArrow(ctx, start, end, color);
            strokeOrderLayer.Add(arrow);

            if (number > 0)
            {
                var badge = new Label(number.ToString());
                badge.pickingMode = PickingMode.Ignore;
                badge.style.position = Position.Absolute;
                badge.style.left = Length.Percent(start.x * 100f);
                badge.style.top = Length.Percent(start.y * 100f);
                badge.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-50f));
                badge.style.width = 44; badge.style.height = 44;
                badge.style.borderTopLeftRadius = 22; badge.style.borderTopRightRadius = 22;
                badge.style.borderBottomLeftRadius = 22; badge.style.borderBottomRightRadius = 22;
                badge.style.backgroundColor = color;
                badge.style.color = Color.white;
                badge.style.unityTextAlign = TextAnchor.MiddleCenter;
                badge.style.fontSize = 24;
                if (koreanFont != null) badge.style.unityFontDefinition = new StyleFontDefinition(koreanFont);
                strokeOrderLayer.Add(badge);
            }
        }

        // Painter2D로 시작→끝 화살표를 그린다 (RenderStars/DrawStar와 같은 방식).
        private static void DrawStrokeArrow(MeshGenerationContext ctx, Vector2 start, Vector2 end, Color color)
        {
            var painter = ctx.painter2D;
            float w = ctx.visualElement.contentRect.width;
            float h = ctx.visualElement.contentRect.height;
            if (w <= 0f || h <= 0f) return;

            Vector2 p0 = new Vector2(start.x * w, start.y * h);
            Vector2 p1 = new Vector2(end.x * w, end.y * h);
            Vector2 diff = p1 - p0;
            float len = diff.magnitude;
            if (len < 1f) return;
            Vector2 dir = diff / len;
            Vector2 normal = new Vector2(-dir.y, dir.x);

            painter.strokeColor = color;
            painter.lineWidth = Mathf.Max(4f, w * 0.018f);
            painter.BeginPath();
            painter.MoveTo(p0);
            painter.LineTo(p1);
            painter.Stroke();

            float headLen = Mathf.Min(len * 0.45f, w * 0.09f);
            float headWidth = headLen * 0.8f;
            Vector2 basePt = p1 - dir * headLen;
            Vector2 left = basePt + normal * headWidth * 0.5f;
            Vector2 right = basePt - normal * headWidth * 0.5f;

            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(p1);
            painter.LineTo(left);
            painter.LineTo(right);
            painter.ClosePath();
            painter.Fill();
        }

        private void Show(VisualElement target)
        {
            writingScreen?.EnableInClassList("hidden", target != writingScreen);
            analyzingScreen?.EnableInClassList("hidden", target != analyzingScreen);
            resultScreen?.EnableInClassList("hidden", target != resultScreen);
        }
    }
}

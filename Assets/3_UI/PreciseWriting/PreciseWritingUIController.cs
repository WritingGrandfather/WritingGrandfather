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
        private const float TopBarHeightFrac = 0.11f;
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

        [Tooltip("연습할 스테이지 데이터 — 연결하면 아래 데모 단어 대신 스테이지 글자들을 순서대로 출제")]
        [SerializeField] private StageData[] stages;

        [Tooltip("스테이지에서 낱말(Letter)/단어(Word) 중 어느 목록을 쓸지")]
        [SerializeField] private GameMode stageMode = GameMode.Letter;

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
        private Button retryButton;
        private Button exitButton;
        private Button resetButton;
        private Button undoButton;
        private Label currentWordLabel;
        private Label wordProgressLabel;
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

        private int wordIndex;

        // 스테이지 전환 연출용 — practiceWords[i]가 속한 스테이지 번호(1부터)와 이름
        private int[] wordStageNums;
        private string[] wordStageNames;
        private int lastBannerStage = -1;

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
                    foreach (var t in texts)
                    {
                        list.Add(t);
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
            root.RegisterCallback<PointerMoveEvent>(OnPointerMoveOverRoot, TrickleDown.TrickleDown);
            feedbackController?.onFeedback?.AddListener(OnFeedbackReceived);
            root.schedule.Execute(ApplyLayout).StartingIn(0);
        }

        private void OnDisable()
        {
            safeArea?.UnregisterCallback<GeometryChangedEvent>(OnLayoutGeo);
            writingScreen?.UnregisterCallback<GeometryChangedEvent>(OnLayoutGeo);
            guideBox?.UnregisterCallback<GeometryChangedEvent>(OnGuideBoxGeo);
            root?.UnregisterCallback<PointerMoveEvent>(OnPointerMoveOverRoot, TrickleDown.TrickleDown);
            feedbackController?.onFeedback?.RemoveListener(OnFeedbackReceived);
            LocalizationManager.OnLanguageChanged -= ApplyLocalization;
            if (drawLine != null) drawLine.drawingEnabled = false;
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

        // guide-box와 safe-area 밖에서는 손글씨가 그려지지 않도록, 포인터가 둘 다 안에 있을 때만
        // DrowLine.drawingEnabled를 켠다 (카드/safe-area 밖으로 그려지던 버그 수정).
        private void OnPointerMoveOverRoot(PointerMoveEvent evt)
        {
            if (drawLine == null || guideBox == null) return;

            bool onWritingScreen = writingScreen != null && !writingScreen.ClassListContains("hidden");
            bool insideSafeArea = safeArea == null || safeArea.worldBound.Contains(evt.position);
            drawLine.drawingEnabled = onWritingScreen && insideSafeArea && guideBox.worldBound.Contains(evt.position);
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
            retryButton = root.Q<Button>("retry-button");
            exitButton = root.Q<Button>("exit-button");
            resetButton = root.Q<Button>("reset-button");
            undoButton = root.Q<Button>("undo-button");
            currentWordLabel = root.Q<Label>("current-word-label");
            wordProgressLabel = root.Q<Label>("word-progress-label");
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
        }

        private void ApplyLocalization()
        {
            if (resetButton != null) resetButton.text = LocalizationManager.Get("precise_writing.reset_button");
            if (undoButton != null) undoButton.text = LocalizationManager.Get("precise_writing.undo_button");
            if (completeButton != null) completeButton.text = LocalizationManager.Get("precise_writing.complete_button");
            if (retryButton != null) retryButton.text = LocalizationManager.Get("precise_writing.retry_button");
            if (exitButton != null) exitButton.text = LocalizationManager.Get("precise_writing.exit_button");

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
            undoButton?.RegisterCallback<ClickEvent>(_ => UndoManager.Instance?.Undo());
            retryButton?.RegisterCallback<ClickEvent>(_ =>
            {
                wordIndex = 0;
                UpdateWordLabel();
                ClearStrokes();
                Show(writingScreen);
            });
            exitButton?.RegisterCallback<ClickEvent>(_ =>
                UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyScene"));
        }

        // 그려진 획을 모두 지운다 (단어 전환·다시 시작·완료 후). drawLine 미연결이어도 안전.
        private void ClearStrokes()
        {
            drawLine?.ClearAll();
        }

        // 완료 클릭: 다음 단어가 있으면 넘어가고, 마지막 단어면 분석 후 결과를 보여준다.
        private void OnCompleteClicked()
        {
            if (practiceWords != null && wordIndex < practiceWords.Length - 1)
            {
                wordIndex++;
                UpdateWordLabel();
                ClearStrokes();
                return;
            }

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
            {
                string scoreText = $"{demoScorePercent}%";
                if (resultStrokeOrderScoreLabel != null) resultStrokeOrderScoreLabel.text = scoreText;
                if (resultSimilarityScoreLabel != null) resultSimilarityScoreLabel.text = scoreText;
                if (resultPositionScoreLabel != null) resultPositionScoreLabel.text = scoreText;
                if (resultMessageLabel != null) resultMessageLabel.text = "(데모 점수 — AI 채점 미연결)";
                Show(resultScreen);
            }).StartingIn(1200);
        }

        // WritingFeedbackController.onFeedback 콜백 — 실제 AI 채점 결과가 도착하면 호출된다.
        // 세 항목은 각각 다른 방식으로 계산된 독립적인 점수다 (-1 = 이번엔 계산 안 됨 → 종합 점수로 대체 표시).
        private void OnFeedbackReceived(HandwritingFeedback feedback)
        {
            if (feedback == null) return;

            int similarity = feedback.similarityScore >= 0 ? feedback.similarityScore : feedback.score;
            int strokeOrder = feedback.strokeOrderScore >= 0 ? feedback.strokeOrderScore : feedback.score;
            int position = feedback.positionScore >= 0 ? feedback.positionScore : feedback.score;

            if (resultStrokeOrderScoreLabel != null) resultStrokeOrderScoreLabel.text = $"{strokeOrder}%";
            if (resultSimilarityScoreLabel != null) resultSimilarityScoreLabel.text = $"{similarity}%";
            if (resultPositionScoreLabel != null) resultPositionScoreLabel.text = $"{position}%";
            if (resultMessageLabel != null) resultMessageLabel.text = feedback.message;

            Show(resultScreen);
        }

        private void UpdateWordLabel()
        {
            if (practiceWords == null || practiceWords.Length == 0) return;

            string word = practiceWords[wordIndex];
            if (currentWordLabel != null) currentWordLabel.text = word;
            if (ghostCharacterLabel != null) ghostCharacterLabel.text = word;
            if (wordProgressLabel != null) wordProgressLabel.text = $"{wordIndex + 1} / {practiceWords.Length}";

            // 스테이지가 바뀌는 첫 글자에서 전환 연출
            if (wordStageNums != null && wordIndex < wordStageNums.Length && wordStageNums[wordIndex] != lastBannerStage)
            {
                lastBannerStage = wordStageNums[wordIndex];
                ShowStageBanner(lastBannerStage, wordStageNames[wordIndex]);
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

        private void Show(VisualElement target)
        {
            writingScreen?.EnableInClassList("hidden", target != writingScreen);
            analyzingScreen?.EnableInClassList("hidden", target != analyzingScreen);
            resultScreen?.EnableInClassList("hidden", target != resultScreen);
        }
    }
}

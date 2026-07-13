using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class LobbyController : MonoBehaviour
{
    [SerializeField] float baseMargin = 32f;

    // Text elements that shrink together with the content box when SafeArea
    // insets eat into it, so text never gets clipped instead of just resized.
    // Matches the font-size values in Lobby.uss - keep both in sync.
    [SerializeField] string[] titleLabelNames = { "title-label-line1", "title-label-line2" };
    [SerializeField] float titleBaseFontSize = 52f;
    [SerializeField] string[] menuButtonNames = { "btn-start", "btn-settings", "btn-exit" };
    [SerializeField] float menuButtonBaseFontSize = 34f;
    // title-area (190) + its margin-bottom (40) + button-area (3 * (120 + 14)) in Lobby.uss.
    [SerializeField] float designContentHeight = 632f;
    // .title-area's own "padding: 0 40px" (both sides) in Lobby.uss.
    [SerializeField] float titleAreaHorizontalPadding = 80f;
    // .button-area's "width: 80%" in Lobby.uss.
    [SerializeField] float buttonAreaWidthFraction = 0.8f;
    [SerializeField] float minFontScale = 0.5f;

    // Settings panel text also needs to shrink on narrow screens, same reason
    // as the lobby title/menu buttons above. Matches Settings.uss values.
    [SerializeField] float settingsTitleBaseFontSize = 44f;
    [SerializeField] float settingsTabBaseFontSize = 28f;
    // .settings-header's "padding: 24px 32px" (both sides) in Settings.uss.
    [SerializeField] float settingsHeaderHorizontalPadding = 64f;
    // .settings-tab-bar's "padding: 0 24px" (both sides) in Settings.uss.
    [SerializeField] float settingsTabBarHorizontalPadding = 48f;
    // each .settings-tab's "margin: 0 6px" (both sides) in Settings.uss.
    [SerializeField] float settingsTabHorizontalMargin = 12f;
    [SerializeField] float settingsRowLabelBaseFontSize = 30f;
    // .settings-content's "padding: 8px 40px 32px" (both sides) in Settings.uss.
    [SerializeField] float settingsContentHorizontalPadding = 80f;
    // .settings-row-label's "width: 32%" in Settings.uss.
    [SerializeField] float settingsRowLabelWidthFraction = 0.32f;

    VisualElement _lobbyRoot;
    VisualElement _exitModal;
    TextElement[] _titleLabels;
    TextElement[] _menuButtons;
    Rect _appliedSafeArea;
    Vector2Int _appliedScreenSize;
    Vector2 _appliedPanelSize;

    VisualElement _settingsPanel;
    TextElement _settingsTitle;
    Button _settingsCloseButton;
    Button[] _settingsTabButtons;
    VisualElement[] _settingsTabPanels;
    TextElement[] _settingsRowLabels;

    void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        root.Q<Button>("btn-start").clicked += OnStartClicked;
        root.Q<Button>("btn-settings").clicked += OnSettingsClicked;
        root.Q<Button>("btn-exit").clicked += OnExitClicked;

        _exitModal = root.Q<VisualElement>("exit-modal");
        root.Q<Button>("btn-exit-confirm").clicked += OnExitConfirmClicked;
        root.Q<Button>("btn-exit-cancel").clicked += OnExitCancelClicked;

        _lobbyRoot = root.Q<VisualElement>("lobby-root");
        _titleLabels = System.Array.ConvertAll(titleLabelNames, name => _lobbyRoot.Q<TextElement>(name));
        _menuButtons = System.Array.ConvertAll(menuButtonNames, name => _lobbyRoot.Q<TextElement>(name));
        _lobbyRoot.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

        _settingsPanel = root.Q<VisualElement>("settings-panel");
        _settingsTitle = root.Q<TextElement>("settings-title");
        _settingsCloseButton = root.Q<Button>("btn-settings-close");
        _settingsCloseButton.clicked += OnSettingsCloseClicked;

        // 탭 버튼과 탭 콘텐츠 패널을 같은 순서(비디오/오디오/언어)로 짝지어
        // 인덱스만으로 서로 매칭할 수 있게 함 - Lobby.uxml의 탭 순서와 일치해야 함.
        _settingsTabButtons = new[]
        {
            root.Q<Button>("tab-video"),
            root.Q<Button>("tab-audio"),
            root.Q<Button>("tab-language"),
        };
        _settingsTabPanels = new[]
        {
            root.Q<VisualElement>("video-panel"),
            root.Q<VisualElement>("audio-panel"),
            root.Q<VisualElement>("language-panel"),
        };
        // 화질/밝기/전체 볼륨 등 모든 탭 패널에 걸쳐 있는 행 라벨을 한 번에 모음 -
        // 탭이 숨겨져 있어도(display:none) 텍스트 측정 자체는 문제없이 동작한다.
        _settingsRowLabels = root.Query<Label>().Class("settings-row-label").Build().ToList().ToArray();
        for (int i = 0; i < _settingsTabButtons.Length; i++)
        {
            // 클로저가 루프 변수 i를 그대로 캡처하면 모든 버튼이 마지막 인덱스를
            // 참조하게 되므로, 로컬 변수 tabIndex에 복사해서 캡처한다.
            int tabIndex = i;
            _settingsTabButtons[i].clicked += () => ShowSettingsTab(tabIndex);
        }

        ApplySafeArea();
    }

    void OnDisable()
    {
        _lobbyRoot?.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
    }

    void OnGeometryChanged(GeometryChangedEvent evt)
    {
        ApplySafeArea();
    }

    void Update()
    {
        // Device Simulator switching devices doesn't always raise a
        // GeometryChangedEvent, so poll Screen.safeArea directly as well.
        ApplySafeArea();
    }

    void OnStartClicked()
    {
        // TODO: 모드 선택 화면(정밀 쓰기 / 도전 모드)으로 전환
        Debug.Log("글쓰기 시작 클릭");
    }

    // 설정 패널은 항상 트리에 존재하고 .hidden 클래스(Lobby.uss)로만 표시 여부를
    // 제어한다. Enable/Disable 대신 이 방식을 쓰는 이유는 SafeArea 갱신(ApplySafeArea)
    // 등 패널 상태와 무관하게 계속 이벤트를 받아야 하기 때문.
    void OnSettingsClicked()
    {
        _settingsPanel.RemoveFromClassList("hidden");

        // While hidden (display:none) the panel's children have no resolved
        // size, so the safe-area/font-scale values computed during that time
        // are stale. Clear the cache so the next ApplySafeArea() (from the
        // very next Update()) recomputes them against real, laid-out sizes.
        _appliedPanelSize = default;
    }

    void OnSettingsCloseClicked()
    {
        _settingsPanel.AddToClassList("hidden");
    }

    // 선택된 탭만 활성 스타일 + 해당 탭 패널을 보여주고, 나머지는 비활성 처리.
    void ShowSettingsTab(int tabIndex)
    {
        for (int i = 0; i < _settingsTabButtons.Length; i++)
        {
            _settingsTabButtons[i].EnableInClassList("settings-tab--active", i == tabIndex);
            _settingsTabPanels[i].EnableInClassList("hidden", i != tabIndex);
        }
    }

    void OnExitClicked()
    {
        _exitModal.RemoveFromClassList("hidden");
    }

    void OnExitConfirmClicked()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    void OnExitCancelClicked()
    {
        _exitModal.AddToClassList("hidden");
    }

    void ApplySafeArea()
    {
        if (_lobbyRoot == null || _lobbyRoot.panel == null)
            return;

        var safeArea = Screen.safeArea;
        var screenSize = new Vector2Int(Screen.width, Screen.height);

        var panel = _lobbyRoot.panel;
        float panelWidth = panel.visualTree.resolvedStyle.width;
        float panelHeight = panel.visualTree.resolvedStyle.height;
        if (panelWidth <= 0f || panelHeight <= 0f)
            return;

        // panelWidth/panelHeight can take a frame or two to catch up after
        // Screen.safeArea/Screen.width/height change (e.g. Device Simulator
        // switching profiles), since PanelSettings needs a layout pass to
        // re-derive its scale factor. Keep re-checking every frame - not just
        // when safeArea/screenSize change - until the panel size itself has
        // also settled, otherwise this can lock in padding computed against a
        // stale (previous device's) panel size.
        var panelSize = new Vector2(panelWidth, panelHeight);
        if (safeArea == _appliedSafeArea && screenSize == _appliedScreenSize && panelSize == _appliedPanelSize)
            return;

        // Let Unity's own screen-to-panel transform do the conversion instead of
        // re-deriving PanelSettings' scale factor by hand - it already accounts
        // for whichever ScreenMatchMode (Shrink/Expand/MatchWidthOrHeight) is
        // configured, so this stays correct no matter how that setting changes.
        // Screen.safeArea uses a bottom-left origin (like Input.mousePosition);
        // ScreenToPanel expects a top-left origin, so flip Y.
        Vector2 topLeft = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(safeArea.xMin, screenSize.y - safeArea.yMax));
        Vector2 bottomRight = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(safeArea.xMax, screenSize.y - safeArea.yMin));

        // Even with no notch/home-indicator inset, keep a minimum breathing
        // room from the screen edge so content doesn't hug it directly.
        float paddingLeft = Mathf.Max(0f, topLeft.x) + baseMargin;
        float paddingTop = Mathf.Max(0f, topLeft.y) + baseMargin;
        float paddingRight = Mathf.Max(0f, panelWidth - bottomRight.x) + baseMargin;
        float paddingBottom = Mathf.Max(0f, panelHeight - bottomRight.y) + baseMargin;
        _lobbyRoot.style.paddingLeft = paddingLeft;
        _lobbyRoot.style.paddingRight = paddingRight;
        _lobbyRoot.style.paddingTop = paddingTop;
        _lobbyRoot.style.paddingBottom = paddingBottom;

        if (_settingsPanel != null)
        {
            _settingsPanel.style.paddingLeft = paddingLeft;
            _settingsPanel.style.paddingRight = paddingRight;
            _settingsPanel.style.paddingTop = paddingTop;
            _settingsPanel.style.paddingBottom = paddingBottom;
        }

        // Only SafeArea-driven shrinkage should scale the text down - measured
        // against the panel's actual current size, not an assumed constant.
        float availableContentHeight = panelHeight - paddingTop - paddingBottom;
        float heightFontScale = Mathf.Clamp(availableContentHeight / designContentHeight, minFontScale, 1f);

        float contentWidth = panelWidth - paddingLeft - paddingRight;
        float titleAvailableWidth = Mathf.Max(0f, contentWidth - titleAreaHorizontalPadding);
        float buttonAvailableWidth = Mathf.Max(0f, contentWidth * buttonAreaWidthFraction);

        ApplyTextScale(_titleLabels, titleBaseFontSize, titleAvailableWidth, heightFontScale);
        ApplyTextScale(_menuButtons, menuButtonBaseFontSize, buttonAvailableWidth, heightFontScale);

        // Settings panel isn't height-constrained the way the lobby's title/
        // buttons are (no equivalent designContentHeight for it), so only
        // shrink its text by width - pass 1f instead of heightFontScale.
        if (_settingsTitle != null)
        {
            // Reserve the close button's actual laid-out width instead of a
            // hardcoded estimate, so this stays correct if its text/padding
            // ever changes independently of this script.
            float closeButtonWidth = _settingsCloseButton.resolvedStyle.width;
            if (float.IsNaN(closeButtonWidth))
                closeButtonWidth = 0f;
            float settingsTitleAvailableWidth = Mathf.Max(0f, contentWidth - settingsHeaderHorizontalPadding - closeButtonWidth);
            ApplyTextScale(new[] { _settingsTitle }, settingsTitleBaseFontSize, settingsTitleAvailableWidth, 1f);
        }

        if (_settingsTabButtons != null)
        {
            float tabsMargin = settingsTabHorizontalMargin * _settingsTabButtons.Length;
            float settingsTabAvailableWidth = Mathf.Max(0f, (contentWidth - settingsTabBarHorizontalPadding - tabsMargin) / _settingsTabButtons.Length);
            ApplyTextScale(_settingsTabButtons, settingsTabBaseFontSize, settingsTabAvailableWidth, 1f);
        }

        if (_settingsRowLabels != null)
        {
            float settingsRowWidth = Mathf.Max(0f, contentWidth - settingsContentHorizontalPadding);
            float settingsRowLabelAvailableWidth = settingsRowWidth * settingsRowLabelWidthFraction;
            ApplyTextScale(_settingsRowLabels, settingsRowLabelBaseFontSize, settingsRowLabelAvailableWidth, 1f);
        }

        _appliedSafeArea = safeArea;
        _appliedScreenSize = screenSize;
        _appliedPanelSize = panelSize;
    }

    void ApplyTextScale(TextElement[] elements, float baseFontSize, float availableWidth, float heightFontScale)
    {
        foreach (var element in elements)
        {
            if (element == null)
                continue;

            // Reset to the base size before measuring, otherwise a previous
            // frame's shrunk size would throw off this frame's measurement.
            element.style.fontSize = baseFontSize;
            var naturalSize = element.MeasureTextSize(element.text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
            float widthFontScale = naturalSize.x > 0f ? Mathf.Clamp01(availableWidth / naturalSize.x) : 1f;

            float fontScale = Mathf.Clamp(Mathf.Min(heightFontScale, widthFontScale), minFontScale, 1f);
            element.style.fontSize = baseFontSize * fontScale;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class LobbyController : MonoBehaviour
{
    [SerializeField] float baseMargin = 32f;

    // Text elements that shrink together with the content box when SafeArea
    // insets eat into it, so text never gets clipped instead of just resized.
    // Matches the font-size values in Lobby.uss - keep both in sync.
    [SerializeField] string[] titleLabelNames = { "title-label-line1", "title-label-line2" };
    // titleLabelNames와 같은 순서(line1, line2)로 대응되는 localization.csv 키.
    static readonly string[] TitleLabelKeys = { "lobby.title.line1", "lobby.title.line2" };
    [SerializeField] float titleBaseFontSize = 52f;
    [SerializeField] string[] menuButtonNames = { "btn-start", "btn-settings", "btn-exit" };
    // menuButtonNames와 같은 순서(시작, 설정, 나가기)로 대응되는 키.
    static readonly string[] MenuButtonKeys = { "lobby.btn.start", "lobby.btn.settings", "lobby.btn.exit" };
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

    TextElement _modalTextLine1;
    TextElement _modalTextLine2;
    Button _btnExitConfirm;
    Button _btnExitCancel;

    VisualElement _settingsPanel;
    TextElement _settingsTitle;
    Button _settingsCloseButton;
    Button[] _settingsTabButtons;
    VisualElement[] _settingsTabPanels;
    TextElement[] _settingsRowLabels;
    // _settingsRowLabels는 이름이 아니라 UXML 문서 순서(화질/프레임/전체 볼륨/배경음/
    // 효과음/언어 선택)로 모은 것이라, 이 배열도 그 순서와 반드시 일치해야 한다 -
    // Lobby.uxml에서 행 라벨 순서가 바뀌면 여기도 같이 바꿔야 한다.
    static readonly string[] SettingsRowLabelKeys =
    {
        "lobby.settings.quality_label",
        "lobby.settings.framerate_label",
        "lobby.settings.master_volume_label",
        "lobby.settings.bgm_label",
        "lobby.settings.sfx_label",
        "lobby.settings.language_label",
    };

    DropdownField _dropdownQuality;
    DropdownField _dropdownFramerate;
    RadioButtonGroup _radioLanguage;

    void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        // root.panel은 최소 한 프레임의 레이아웃/어태치 과정을 거쳐야 값이
        // 채워지므로, 아직 비어 있으면 패널에 붙는 시점(AttachToPanelEvent)에
        // 다시 시도한다.
        if (root.panel != null)
            HoistStyleSheetsToPanelRoot(root);
        else
            root.RegisterCallback<AttachToPanelEvent>(_ => HoistStyleSheetsToPanelRoot(root));

        root.Q<Button>("btn-start").clicked += OnStartClicked;
        root.Q<Button>("btn-settings").clicked += OnSettingsClicked;
        root.Q<Button>("btn-exit").clicked += OnExitClicked;

        _exitModal = root.Q<VisualElement>("exit-modal");
        _modalTextLine1 = root.Q<TextElement>("modal-text-line1");
        _modalTextLine2 = root.Q<TextElement>("modal-text-line2");
        _btnExitConfirm = root.Q<Button>("btn-exit-confirm");
        _btnExitCancel = root.Q<Button>("btn-exit-cancel");
        _btnExitConfirm.clicked += OnExitConfirmClicked;
        _btnExitCancel.clicked += OnExitCancelClicked;

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

        _dropdownQuality = root.Q<DropdownField>("dropdown-quality");
        _dropdownFramerate = root.Q<DropdownField>("dropdown-framerate");
        _radioLanguage = root.Q<RadioButtonGroup>("radio-language");
        // UXML의 value="0"(한국어) 대신, 이전에 저장해 둔 언어 설정을 그대로
        // 반영한다. SetValueWithoutNotify를 써서 OnLanguageRadioChanged가 다시
        // 호출되며 SetLanguage를 한 번 더 트리거하는 걸 막는다.
        _radioLanguage.SetValueWithoutNotify(LocalizationManager.CurrentLanguage == Language.Korean ? 0 : 1);
        _radioLanguage.RegisterValueChangedCallback(OnLanguageRadioChanged);

        LocalizationManager.OnLanguageChanged += OnLanguageChanged;
        ApplyLocalization();

        ApplySafeArea();
    }

    void OnLanguageRadioChanged(ChangeEvent<int> evt)
    {
        LocalizationManager.SetLanguage(evt.newValue == 0 ? Language.Korean : Language.English);
    }

    // 언어가 바뀌면 모든 텍스트를 다시 채우고, 언어별로 글자 폭이 달라질 수
    // 있으므로 폰트 스케일 캐시도 무효화해서 다음 Update()에서 다시 계산되게 한다.
    void OnLanguageChanged()
    {
        ApplyLocalization();
        _appliedPanelSize = default;
    }

    void ApplyLocalization()
    {
        for (int i = 0; i < _titleLabels.Length; i++)
            _titleLabels[i].text = LocalizationManager.Get(TitleLabelKeys[i]);

        for (int i = 0; i < _menuButtons.Length; i++)
            _menuButtons[i].text = LocalizationManager.Get(MenuButtonKeys[i]);

        _modalTextLine1.text = LocalizationManager.Get("lobby.exit.confirm_line1");
        _modalTextLine2.text = LocalizationManager.Get("lobby.exit.confirm_line2");
        _btnExitConfirm.text = LocalizationManager.Get("lobby.exit.confirm_yes");
        _btnExitCancel.text = LocalizationManager.Get("lobby.exit.confirm_no");

        _settingsTitle.text = LocalizationManager.Get("lobby.settings.title");
        _settingsCloseButton.text = LocalizationManager.Get("lobby.settings.close");

        _settingsTabButtons[0].text = LocalizationManager.Get("lobby.settings.tab_video");
        _settingsTabButtons[1].text = LocalizationManager.Get("lobby.settings.tab_audio");
        _settingsTabButtons[2].text = LocalizationManager.Get("lobby.settings.tab_language");

        for (int i = 0; i < _settingsRowLabels.Length; i++)
            _settingsRowLabels[i].text = LocalizationManager.Get(SettingsRowLabelKeys[i]);

        // DropdownField.value는 (인덱스가 아니라) 선택된 문자열 그 자체라서,
        // choices를 통째로 새 언어 문자열로 갈아끼우면 이전 value("보통" 등)가
        // 새 choices 목록 어디에도 없어 선택이 깨진다. 인덱스를 먼저 기억해 뒀다가
        // choices를 바꾼 뒤 그 인덱스로 다시 선택해서 같은 항목이 유지되게 한다.
        int qualityIndex = _dropdownQuality.index;
        _dropdownQuality.choices = new List<string>
        {
            LocalizationManager.Get("lobby.settings.quality_low"),
            LocalizationManager.Get("lobby.settings.quality_medium"),
            LocalizationManager.Get("lobby.settings.quality_high"),
        };
        _dropdownQuality.index = qualityIndex;

        int framerateIndex = _dropdownFramerate.index;
        _dropdownFramerate.choices = new List<string>
        {
            LocalizationManager.Get("lobby.settings.fps_30"),
            LocalizationManager.Get("lobby.settings.fps_60"),
        };
        _dropdownFramerate.index = framerateIndex;
        // 언어 이름 자체(한국어/English)는 항상 그 언어 고유 표기로 보여준다 -
        // 라디오 그룹의 choices는 UI 언어가 바뀌어도 안 바뀐다.
        _radioLanguage.choices = new List<string>
        {
            LocalizationManager.Get("lobby.settings.lang_korean"),
            LocalizationManager.Get("lobby.settings.lang_english"),
        };

        // RadioButtonGroup.choices를 새로 대입하면 내부적으로 RadioButton들을
        // 통째로 다시 만든다 - 그러면 이전에 만들어 둔 체크마크(들)는 새로
        // 생성된 것으로 교체돼서, 위치/크기를 맞춰주는 FixRadioButtonCheckmarks
        // 보정도 매번(언어가 바뀔 때마다) 다시 걸어줘야 한다.
        FixRadioButtonCheckmarks(_radioLanguage);
    }

    // DropdownField를 눌렀을 때 뜨는 선택지 목록(GenericDropdownMenu)은 UXML로
    // 불러온 콘텐츠 트리 안이 아니라, 패널 자체의 최상위 루트(panel.visualTree)에
    // rootVisualElement와 형제로 별도로 붙는다 (Unity의 GetRootVisualContainer()가
    // 그렇게 만듦). Lobby.uxml의 <Style src>로 등록한 스타일시트는 그 UXML
    // 콘텐츠를 감싸는 자식 요소에만 붙어있어서, 형제로 붙는 드롭다운 목록에는
    // 상속되지 않아 Settings.uss의 .unity-base-dropdown__label 등이 전혀
    // 적용되지 않았다. 실제로 스타일시트를 갖고 있는 자식을 찾아 panel.visualTree
    // 자신에게도 그대로 추가해서 rootVisualElement든 그 형제든 전부 같은
    // 스타일을 받게 한다.
    void HoistStyleSheetsToPanelRoot(VisualElement root)
    {
        var panelRoot = root.panel.visualTree;
        var carrier = FindStyleSheetCarrier(root);
        if (carrier == null)
            return;

        for (int i = 0; i < carrier.styleSheets.count; i++)
        {
            var sheet = carrier.styleSheets[i];
            if (!panelRoot.styleSheets.Contains(sheet))
                panelRoot.styleSheets.Add(sheet);
        }
    }

    VisualElement FindStyleSheetCarrier(VisualElement element)
    {
        if (element.styleSheets.count > 0)
            return element;

        foreach (var child in element.Children())
        {
            var found = FindStyleSheetCarrier(child);
            if (found != null)
                return found;
        }

        return null;
    }

    // Settings.uss가 링을 꽉 채우는 원으로 바꿔보려 했지만, Unity 기본 테마가
    // ":checked" 상태에서 checkmark의 position/top을 더 높은 우선순위로 다시
    // 지정해서 계속 아래로 치우쳐 보였다. 인라인 스타일은 어떤 USS 규칙보다도
    // 항상 우선 적용되므로, 여기서 직접 지정해 확실하게 중앙에 꽉 채운다.
    //
    // position:Relative + top/left:0은 "원래 flex 배치 위치에서 0만큼 이동"일
    // 뿐이라, checkmark-background의 flex 정렬이 (0,0)이 아닌 곳에 놓으면
    // 그 어긋난 위치가 그대로 남아 안쪽 원이 한쪽으로 치우쳐 보이는 버그가
    // 있었다. position:Absolute로 4면을 전부 0에 고정해야 부모의 실제 컨텐츠
    // 박스 크기와 무관하게 정확히 채워진다.
    // width/height도 명시적으로 100%를 줘야 한다 - Unity 기본 테마가 checkmark에
    // 작은 고정 px 크기를 지정해 두는데, absolute에서 left/right(top/bottom)만
    // 0으로 줘도 width/height가 명시돼 있으면 그 값이 우선되어 결국 좌상단에
    // 작은 점만 남는 문제가 있었다. border-radius도 36/3px라는 고정값 가정
    // 대신, 레이아웃이 실제로 계산된 크기(GeometryChangedEvent)를 기준으로
    // 매번 폭의 절반으로 다시 계산해 항상 완전한 원이 되도록 한다.
    void FixRadioButtonCheckmarks(VisualElement root)
    {
        var checkmarks = root.Query<VisualElement>().Class("unity-radio-button__checkmark").Build().ToList();
        foreach (var checkmark in checkmarks)
        {
            checkmark.style.position = Position.Absolute;
            checkmark.style.top = 0f;
            checkmark.style.left = 0f;
            checkmark.style.right = 0f;
            checkmark.style.bottom = 0f;
            checkmark.style.width = new Length(100f, LengthUnit.Percent);
            checkmark.style.height = new Length(100f, LengthUnit.Percent);
            checkmark.style.marginTop = 0f;
            checkmark.style.marginLeft = 0f;
            checkmark.style.marginRight = 0f;
            checkmark.style.marginBottom = 0f;
            checkmark.style.backgroundImage = StyleKeyword.None;
            checkmark.style.backgroundColor = new Color(214f / 255f, 108f / 255f, 58f / 255f);
            checkmark.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                float radius = evt.newRect.width / 2f;
                checkmark.style.borderTopLeftRadius = radius;
                checkmark.style.borderTopRightRadius = radius;
                checkmark.style.borderBottomLeftRadius = radius;
                checkmark.style.borderBottomRightRadius = radius;
            });
        }
    }

    void OnDisable()
    {
        _lobbyRoot?.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        _radioLanguage?.UnregisterValueChangedCallback(OnLanguageRadioChanged);
        LocalizationManager.OnLanguageChanged -= OnLanguageChanged;
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
        SceneManager.LoadScene("ModSelectScene");
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

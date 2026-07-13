using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class LobbyController : MonoBehaviour
{
    [SerializeField] float baseMargin = 32f;
    [Tooltip("로그아웃 시 이동할 씬 이름 (Build Settings에 등록되어 있어야 함)")]
    [SerializeField] string loginScene = "LoginScene";

    // Text elements that shrink together with the content box when SafeArea
    // insets eat into it, so text never gets clipped instead of just resized.
    // Matches the font-size values in Lobby.uss - keep both in sync.
    [SerializeField] string[] titleLabelNames = { "title-label-line1", "title-label-line2" };
    // titleLabelNames와 같은 순서(line1, line2)로 대응되는 localization.csv 키.
    static readonly string[] TitleLabelKeys = { "lobby.title.line1", "lobby.title.line2" };
    [SerializeField] float titleBaseFontSize = 52f;
    [SerializeField] string[] menuButtonNames = { "btn-start", "btn-ranking", "btn-settings", "btn-exit" };
    // menuButtonNames와 같은 순서(시작, 랭킹, 설정, 나가기)로 대응되는 키.
    static readonly string[] MenuButtonKeys = { "lobby.btn.start", "lobby.btn.ranking", "lobby.btn.settings", "lobby.btn.exit" };
    [SerializeField] float menuButtonBaseFontSize = 34f;
    // title-area (190) + its margin-bottom (40) + button-area (4 * (120 + 14)) in Lobby.uss.
    [SerializeField] float designContentHeight = 766f;
    // .title-area's own "padding: 0 40px" (both sides) in Lobby.uss.
    [SerializeField] float titleAreaHorizontalPadding = 80f;
    // .button-area's "width: 80%" in Lobby.uss.
    [SerializeField] float buttonAreaWidthFraction = 0.8f;
    [SerializeField] float minFontScale = 0.5f;

    // Settings panel text also needs to shrink on narrow screens, same reason
    // as the lobby title/menu buttons above. Matches Settings.uss values.
    [SerializeField] float settingsTitleBaseFontSize = 44f;
    // .settings-section-title's font-size in Settings.uss (비디오/오디오/언어 등 분류 제목).
    [SerializeField] float settingsSectionTitleBaseFontSize = 56f;
    // .settings-header's "padding: 24px 32px" (both sides) in Settings.uss.
    [SerializeField] float settingsHeaderHorizontalPadding = 64f;
    // .settings-row-label's font-size in Settings.uss.
    [SerializeField] float settingsRowLabelBaseFontSize = 48f;
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
    TextElement[] _settingsSectionTitles;
    TextElement[] _settingsRowLabels;
    // _settingsRowLabels는 이름이 아니라 UXML 문서 순서(닉네임/이메일/비밀번호/화질/
    // 프레임/전체 볼륨/배경음/효과음/언어 선택/연령)로 모은 것이라, 이 배열도 그 순서와
    // 반드시 일치해야 한다 - Lobby.uxml에서 행 라벨 순서가 바뀌면 여기도 같이 바꿔야 한다.
    static readonly string[] SettingsRowLabelKeys =
    {
        "lobby.settings.nickname_label",
        "login.field.email_label",
        "login.field.password_label",
        "lobby.settings.quality_label",
        "lobby.settings.framerate_label",
        "lobby.settings.master_volume_label",
        "lobby.settings.bgm_label",
        "lobby.settings.sfx_label",
        "lobby.settings.language_label",
        "lobby.settings.adult_child_label",
    };

    DropdownField _dropdownQuality;
    DropdownField _dropdownFramerate;
    RadioButtonGroup _radioLanguage;
    RadioButtonGroup _radioAdultChild;

    Label _mypageEmailValue;
    VisualElement _mypagePasswordRow;
    Button _btnLogout;

    // 닉네임은 계정 인증 정보가 아니라 이 화면(랭킹 표시용)만의 로컬 설정이라
    // AuthManager를 건드리지 않고 PlayerPrefs에 직접 저장한다. 랭킹에 실제로
    // 반영하는 연동은 나중에 별도로 처리한다.
    const string NicknamePrefsKey = "user_nickname";
    TextField _nicknameField;

    void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        // 이전 씬(로그인 등)에서 누른 클릭의 포인터-업이 씬 전환 도중/직후에
        // 도착하면, 그 자리에 새로 생긴 이 씬의 버튼(예: 로그인의 "게스트로
        // 시작"과 같은 화면 위치에 오는 "설정")이 즉시 눌려버리는 문제가
        // 있었다 - 로비로 들어오자마자 설정 화면이 바로 열려 보이는 원인.
        // 씬이 뜨자마자 아주 짧은 시간 동안 화면 전체를 덮는 투명 오버레이로
        // 포인터 입력을 흡수해서, 그 시점의 클릭이 실제 버튼에 닿지 않게 막는다.
        var inputGuard = new VisualElement();
        inputGuard.style.position = Position.Absolute;
        inputGuard.style.left = 0;
        inputGuard.style.top = 0;
        inputGuard.style.right = 0;
        inputGuard.style.bottom = 0;
        root.Add(inputGuard);
        root.schedule.Execute(() => inputGuard.RemoveFromHierarchy()).StartingIn(1000);

        // root.panel은 최소 한 프레임의 레이아웃/어태치 과정을 거쳐야 값이
        // 채워지므로, 아직 비어 있으면 패널에 붙는 시점(AttachToPanelEvent)에
        // 다시 시도한다.
        if (root.panel != null)
            HoistStyleSheetsToPanelRoot(root);
        else
            root.RegisterCallback<AttachToPanelEvent>(_ => HoistStyleSheetsToPanelRoot(root));

        root.Q<Button>("btn-start").clicked += OnStartClicked;
        root.Q<Button>("btn-ranking").clicked += OnRankingClicked;
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

        // 마이페이지/비디오/오디오/언어/기타 섹션 제목을 같은 순서로 모아, 인덱스만으로
        // 로컬라이제이션 키와 매칭할 수 있게 함 - Lobby.uxml의 섹션 순서와 일치해야 함.
        _settingsSectionTitles = new TextElement[]
        {
            root.Q<Label>("section-mypage-title"),
            root.Q<Label>("section-video-title"),
            root.Q<Label>("section-audio-title"),
            root.Q<Label>("section-language-title"),
            root.Q<Label>("section-misc-title"),
        };
        // 화질/프레임/전체 볼륨 등 모든 섹션에 걸쳐 있는 행 라벨을 한 번에 모음 -
        // 이제 모든 섹션이 항상 같이 표시되므로 순서만 UXML 문서 순서와 맞으면 된다.
        _settingsRowLabels = root.Query<Label>().Class("settings-row-label").Build().ToList().ToArray();

        _dropdownQuality = root.Q<DropdownField>("dropdown-quality");
        _dropdownFramerate = root.Q<DropdownField>("dropdown-framerate");
        _radioLanguage = root.Q<RadioButtonGroup>("radio-language");
        // UXML의 value="0"(한국어) 대신, 이전에 저장해 둔 언어 설정을 그대로
        // 반영한다. SetValueWithoutNotify를 써서 OnLanguageRadioChanged가 다시
        // 호출되며 SetLanguage를 한 번 더 트리거하는 걸 막는다.
        _radioLanguage.SetValueWithoutNotify(LocalizationManager.CurrentLanguage == Language.Korean ? 0 : 1);
        _radioLanguage.RegisterValueChangedCallback(OnLanguageRadioChanged);

        _radioAdultChild = root.Q<RadioButtonGroup>("radio-adult-child");

        _mypageEmailValue = root.Q<Label>("mypage-email-value");
        _mypagePasswordRow = root.Q<VisualElement>("mypage-password-row");
        _btnLogout = root.Q<Button>("btn-logout");

        _nicknameField = root.Q<TextField>("mypage-nickname-field");
        _nicknameField.SetValueWithoutNotify(PlayerPrefs.GetString(NicknamePrefsKey, ""));
        _nicknameField.RegisterValueChangedCallback(OnNicknameChanged);
        _btnLogout.clicked += OnLogoutClicked;

        LocalizationManager.OnLanguageChanged += OnLanguageChanged;
        ApplyLocalization();
        RefreshMyPage();

        ApplySafeArea();
    }

    void OnLanguageRadioChanged(ChangeEvent<int> evt)
    {
        LocalizationManager.SetLanguage(evt.newValue == 0 ? Language.Korean : Language.English);
    }

    void OnNicknameChanged(ChangeEvent<string> evt)
    {
        PlayerPrefs.SetString(NicknamePrefsKey, evt.newValue);
        PlayerPrefs.Save();
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

        _settingsSectionTitles[0].text = LocalizationManager.Get("lobby.settings.tab_mypage");
        _settingsSectionTitles[1].text = LocalizationManager.Get("lobby.settings.tab_video");
        _settingsSectionTitles[2].text = LocalizationManager.Get("lobby.settings.tab_audio");
        _settingsSectionTitles[3].text = LocalizationManager.Get("lobby.settings.tab_language");
        _settingsSectionTitles[4].text = LocalizationManager.Get("lobby.settings.tab_misc");

        _btnLogout.text = LocalizationManager.Get("lobby.settings.logout_button");

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

        // 언어 라디오와 달리 성인/어린이는 고유명사가 아니라 UI 언어에 맞춰
        // 번역돼야 하므로, dropdown들과 같은 방식으로 선택된 인덱스를
        // 기억했다가 choices를 바꾼 뒤 그대로 복원한다.
        int adultChildIndex = _radioAdultChild.value;
        _radioAdultChild.choices = new List<string>
        {
            LocalizationManager.Get("lobby.settings.adult_label"),
            LocalizationManager.Get("lobby.settings.child_label"),
        };
        _radioAdultChild.SetValueWithoutNotify(adultChildIndex);
        FixRadioButtonCheckmarks(_radioAdultChild);
    }

    // AuthManager(로그인 씬과 공유하는 DontDestroyOnLoad 싱글톤)의 DisplayName을
    // 그대로 가져와 보여준다 - 이메일 계정은 AuthManager.ApplyUser()에서 표시
    // 이름이 없으면 이메일 자체를 DisplayName으로 채워두므로 이것으로 충분하다.
    void RefreshMyPage()
    {
        if (_mypageEmailValue == null)
            return;

        var auth = AuthManager.Instance;
        _mypageEmailValue.text = auth != null ? auth.DisplayName : "";

        // 게스트 로그인은 계정/비밀번호 자체가 없으므로 비밀번호 행을 통째로 숨긴다.
        bool isGuest = auth != null && auth.IsGuest;
        if (_mypagePasswordRow != null)
        {
            if (isGuest) _mypagePasswordRow.AddToClassList("hidden");
            else _mypagePasswordRow.RemoveFromClassList("hidden");
        }
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
        _nicknameField?.UnregisterValueChangedCallback(OnNicknameChanged);
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

    void OnRankingClicked()
    {
        RankingScreenController.CallerScene = SceneManager.GetActiveScene().name;
        SceneManager.LoadScene("Ranking");
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

        // 설정을 열 때마다 다시 채워서, 로그인 상태가 바뀐 뒤에도 이전 값이
        // 남아있지 않게 한다.
        RefreshMyPage();
    }

    void OnSettingsCloseClicked()
    {
        _settingsPanel.AddToClassList("hidden");
    }

    // AuthManager.SignOut()은 LoginController.cs가 아니라 씬 전환에도 유지되는
    // AuthManager 싱글톤(Assets/2_Scripts/Auth/AuthManager.cs)에 이미 있는 공개
    // 메서드라, 그 두 스크립트는 건드리지 않고 여기서 그대로 호출만 한다.
    void OnLogoutClicked()
    {
        AuthManager.Instance?.SignOut();
        SceneManager.LoadScene(loginScene);
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

        if (_settingsSectionTitles != null)
        {
            // 섹션 제목은 좌우 구분선과 한 줄에 나란히 있지만, 그 선들은
            // flex-grow로 남는 폭을 채울 뿐이라 제목 자체의 사용 가능 폭은
            // 콘텐츠 폭 전체(줄어들 필요가 있을 만큼 좁아지는 경우는 드묾)로 잡는다.
            float settingsSectionTitleAvailableWidth = Mathf.Max(0f, contentWidth - settingsContentHorizontalPadding);
            ApplyTextScale(_settingsSectionTitles, settingsSectionTitleBaseFontSize, settingsSectionTitleAvailableWidth, 1f);
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

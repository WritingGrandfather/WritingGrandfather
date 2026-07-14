using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class ModSelectController : MonoBehaviour
{
    const string SelectedClass = "mode-icon-button--selected";

    TextElement _titleLabel;
    TextElement _hintLabel;
    Button _btnPrecise;
    Button _btnChallenge;
    Button _btnBack;

    // 버튼 자체는 이제 아이콘만 있고 텍스트가 없어서(Lobby의 원형 버튼과 같은
    // 방식), 설명은 항상 떠 있는 라벨이 아니라 카드 하나로 대체했다. 첫 번째
    // 탭에서는 그 모드를 "선택"만 해서 설명을 보여주고, 이미 선택된 버튼을
    // 한 번 더 누르면 그때 실제로 그 모드로 진입한다.
    VisualElement _modeDescCard;
    TextElement _modeDescLabel;
    string _preciseDescText;
    string _challengeDescText;
    Button _selectedButton;

    void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;
        UIClickSound.Attach(root);

        _titleLabel = root.Q<TextElement>("title-label");
        _hintLabel = root.Q<TextElement>("hint-label");
        _btnPrecise = root.Q<Button>("btn-precise");
        _btnChallenge = root.Q<Button>("btn-challenge");
        _btnBack = root.Q<Button>("btn-back");
        _modeDescCard = root.Q<VisualElement>("mode-desc-card");
        _modeDescLabel = root.Q<TextElement>("mode-desc-label");

        _btnPrecise.clicked += () => OnModeClicked(_btnPrecise, _preciseDescText, "WritingPracticeScene");
        _btnChallenge.clicked += () => OnModeClicked(_btnChallenge, _challengeDescText, "ChallengeScene");
        _btnBack.clicked += () => SceneManager.LoadScene("LobbyScene");

        ApplyLocalization();
        LocalizationManager.OnLanguageChanged += ApplyLocalization;
    }

    void OnDisable()
    {
        LocalizationManager.OnLanguageChanged -= ApplyLocalization;
    }

    // 처음 누르면 그 모드를 선택해서 설명만 보여주고, 이미 선택돼 있는
    // 버튼을 다시 누르면 그제서야 해당 씬으로 넘어간다.
    void OnModeClicked(Button button, string descText, string sceneName)
    {
        if (_selectedButton == button)
        {
            // clicked는 포인터를 뗄 때(버튼이 눌렸다 돌아오는 :active scale
            // 애니메이션이 채 끝나기도 전) 바로 불려서, SceneManager.LoadScene을
            // 여기서 즉시 호출하면 씬이 바로 넘어가버려 눌리는 게 안 보였다.
            // Lobby의 inputGuard처럼 UI Toolkit 스케줄러로 살짝 늦춰서
            // 눌리는 애니메이션이 실제로 한 프레임 이상 보인 뒤에 넘어가게 한다.
            button.schedule.Execute(() => SceneManager.LoadScene(sceneName)).StartingIn(120);
            return;
        }

        _selectedButton?.RemoveFromClassList(SelectedClass);
        _selectedButton = button;
        button.AddToClassList(SelectedClass);

        _modeDescLabel.text = descText;
        _modeDescCard.RemoveFromClassList("hidden");
    }

    void ApplyLocalization()
    {
        _titleLabel.text = LocalizationManager.Get("mod_select.title");
        _hintLabel.text = LocalizationManager.Get("mod_select.hint");
        _preciseDescText = LocalizationManager.Get("mod_select.precise_desc");
        _challengeDescText = LocalizationManager.Get("mod_select.challenge_desc");

        // 언어가 바뀌었을 때 이미 선택돼서 보이고 있던 설명도 새 언어로 갱신.
        if (_selectedButton == _btnPrecise) _modeDescLabel.text = _preciseDescText;
        else if (_selectedButton == _btnChallenge) _modeDescLabel.text = _challengeDescText;
    }
}

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class ModSelectController : MonoBehaviour
{
    TextElement _titleLabel;
    Button _btnPrecise;
    Button _btnChallenge;
    Button _btnBack;
    TextElement _preciseDesc;
    TextElement _challengeDesc;

    void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        _titleLabel = root.Q<TextElement>("title-label");
        _btnPrecise = root.Q<Button>("btn-precise");
        _btnChallenge = root.Q<Button>("btn-challenge");
        _btnBack = root.Q<Button>("btn-back");
        _preciseDesc = root.Q<TextElement>("precise-desc");
        _challengeDesc = root.Q<TextElement>("challenge-desc");

        _btnPrecise.clicked += () => SceneManager.LoadScene("WritingPracticeScene");
        _btnChallenge.clicked += () => SceneManager.LoadScene("ChallengeScene");
        _btnBack.clicked += () => SceneManager.LoadScene("LobbyScene");

        ApplyLocalization();
        LocalizationManager.OnLanguageChanged += ApplyLocalization;
    }

    void OnDisable()
    {
        LocalizationManager.OnLanguageChanged -= ApplyLocalization;
    }

    void ApplyLocalization()
    {
        _titleLabel.text = LocalizationManager.Get("mod_select.title");
        _btnPrecise.text = LocalizationManager.Get("mod_select.precise_button");
        _preciseDesc.text = LocalizationManager.Get("mod_select.precise_desc");
        _btnChallenge.text = LocalizationManager.Get("mod_select.challenge_button");
        _challengeDesc.text = LocalizationManager.Get("mod_select.challenge_desc");
        _btnBack.text = LocalizationManager.Get("mod_select.back_button");
    }
}

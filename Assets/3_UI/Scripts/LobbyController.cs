using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class LobbyController : MonoBehaviour
{
    VisualElement _exitModal;
    ResponsiveTextScaler _textScaler;

    void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;
        _textScaler = GetComponent<ResponsiveTextScaler>();

        root.Q<Button>("btn-start").clicked += OnStartClicked;
        root.Q<Button>("btn-settings").clicked += OnSettingsClicked;
        root.Q<Button>("btn-exit").clicked += OnExitClicked;

        _exitModal = root.Q<VisualElement>("exit-modal");
        root.Q<Button>("btn-exit-confirm").clicked += OnExitConfirmClicked;
        root.Q<Button>("btn-exit-cancel").clicked += OnExitCancelClicked;
    }


    void OnStartClicked()
    {
        // TODO: 모드 선택 화면(정밀 쓰기 / 도전 모드)으로 전환
        Debug.Log("글쓰기 시작 클릭");
    }

    void OnSettingsClicked()
    {
        // TODO: 설정 화면으로 전환
        Debug.Log("설정 클릭");
    }

    void OnExitClicked()
    {
        _exitModal.RemoveFromClassList("hidden");
        _exitModal.schedule.Execute(() => _textScaler?.Refresh());
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
}

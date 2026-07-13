using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class ModSelectController : MonoBehaviour
{
    void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        root.Q<Button>("btn-precise").clicked += () => SceneManager.LoadScene("WritingPracticeScene");
        root.Q<Button>("btn-challenge").clicked += () => SceneManager.LoadScene("JihwanScnene");
        root.Q<Button>("btn-back").clicked += () => SceneManager.LoadScene("LobbyScene");
    }
}

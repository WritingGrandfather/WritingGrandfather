using UnityEngine;
using UnityEngine.UIElements;

// 나중에 UI Toolkit 화면에 로컬라이제이션을 적용할 때 쓸 보조 컴포넌트.
// UIDocument가 붙은 오브젝트에 이 컴포넌트를 추가하고 인스펙터에서
// elementName(UXML의 name)과 key(localization.csv의 key)를 짝지어 두면,
// 언어가 바뀔 때마다 해당 VisualElement의 텍스트를 자동으로 갱신한다.
// 아직 어떤 씬에도 붙어있지 않다 - 실제 적용은 나중에 진행한다.
[RequireComponent(typeof(UIDocument))]
public class LocalizedTextBinder : MonoBehaviour
{
    [System.Serializable]
    public struct Binding
    {
        public string elementName;
        public string key;
    }

    [SerializeField] Binding[] bindings;

    TextElement[] _targets;
    string[] _keys;

    void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        _targets = new TextElement[bindings.Length];
        _keys = new string[bindings.Length];
        for (int i = 0; i < bindings.Length; i++)
        {
            _targets[i] = root.Q<TextElement>(bindings[i].elementName);
            _keys[i] = bindings[i].key;
        }

        LocalizationManager.OnLanguageChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        LocalizationManager.OnLanguageChanged -= Refresh;
    }

    void Refresh()
    {
        for (int i = 0; i < _targets.Length; i++)
        {
            if (_targets[i] != null)
                _targets[i].text = LocalizationManager.Get(_keys[i]);
        }
    }
}

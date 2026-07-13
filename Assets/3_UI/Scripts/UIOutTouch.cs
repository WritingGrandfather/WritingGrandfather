using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 패널/모달 바깥쪽(오버레이 배경)을 터치하면 자동으로 닫아주는 범용 컴포넌트.
///
/// 각 씬의 컨트롤러(LoginController 등)는 건드리지 않고, 이미 있는
/// ".modal-overlay"(전체 화면을 덮는 배경) + "hidden"(닫힘) 클래스 규칙만
/// 그대로 재사용한다. UIDocument가 붙어있는 오브젝트에 이 컴포넌트를 같이
/// 추가하고, Overlay Names에 닫고 싶은 오버레이 VisualElement 이름(예:
/// login-panel, signup-panel)을 넣으면 된다.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class UIOutTouch : MonoBehaviour
{
    [Tooltip("바깥을 터치하면 닫을 오버레이 VisualElement 이름들 (예: login-panel, signup-panel)")]
    [SerializeField] string[] overlayNames;

    VisualElement[] _overlays;

    void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;
        _overlays = System.Array.ConvertAll(overlayNames, name => root.Q<VisualElement>(name));

        for (int i = 0; i < _overlays.Length; i++)
        {
            if (_overlays[i] == null)
            {
                // Overlay Names의 오타/빈 값 등으로 못 찾은 경우 조용히 아무 일도
                // 안 일어나면 원인을 알기 어려우므로 바로 콘솔에 남긴다.
                Debug.LogWarning($"[UIOutTouch] \"{overlayNames[i]}\" 이름의 VisualElement를 못 찾았습니다.");
                continue;
            }
            _overlays[i].RegisterCallback<PointerDownEvent>(OnOverlayPointerDown);
        }
    }

    void OnDisable()
    {
        if (_overlays == null)
            return;

        foreach (var overlay in _overlays)
            overlay?.UnregisterCallback<PointerDownEvent>(OnOverlayPointerDown);
    }

    // 이벤트는 오버레이 안쪽(modal-box 등)을 눌러도 버블링되어 오버레이까지
    // 올라온다. target이 오버레이 자신일 때만 닫아야 안쪽 내용을 눌렀을 때
    // 같이 닫히는 걸 막을 수 있다.
    void OnOverlayPointerDown(PointerDownEvent evt)
    {
        // 원인 파악을 위한 임시 로그 - 정상 동작 확인되면 지워도 된다.
        Debug.Log($"[UIOutTouch] pointer down: target={(evt.target as VisualElement)?.name}, overlay={(evt.currentTarget as VisualElement)?.name}");

        if (evt.target == evt.currentTarget)
            ((VisualElement)evt.currentTarget).AddToClassList("hidden");
    }
}

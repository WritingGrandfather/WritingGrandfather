using UnityEngine.UIElements;

// UI Toolkit 버튼 클릭 사운드. 버튼마다 콜백을 다는 대신 UIDocument 루트 하나에
// ClickEvent(버블 단계)를 걸어, 그 아래 어떤 버튼을 눌러도 clickSound가 나게 한다.
// 각 화면 컨트롤러의 OnEnable에서 UIClickSound.Attach(root)를 한 번 호출하면 된다.
public static class UIClickSound
{
    public static void Attach(VisualElement root)
    {
        if (root == null) return;
        // OnEnable이 여러 번 불려도 중복 등록되지 않도록, 정적 메서드를 먼저 해제 후 등록한다.
        root.UnregisterCallback<ClickEvent>(OnClick);
        root.RegisterCallback<ClickEvent>(OnClick);
    }

    static void OnClick(ClickEvent evt)
    {
        // 클릭 지점이 버튼이거나 버튼 내부 요소(라벨 등)면 클릭음 재생.
        if (evt.target is VisualElement ve &&
            (ve is Button || ve.GetFirstAncestorOfType<Button>() != null))
        {
            SoundManager.Instance?.PlaySfx("clickSound");
        }
    }
}

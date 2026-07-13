using UnityEngine.UIElements;

/// <summary>
/// 떨어지는 글자 적. UI Toolkit Label 하나가 곧 적이다. (GameObject 아님)
/// </summary>
public class Enemy
{
    /// <summary>이 적에게 배정된 한글 외자</summary>
    public char Letter { get; }

    /// <summary>UI Toolkit 요소 (스타일은 .enemy-letter 클래스로 USS에서도 제어 가능)</summary>
    public Label Element { get; }

    private readonly float speed;     // px/sec
    private readonly float deadlineY; // 패널 좌표(px), 이 y에 닿으면 플레이어 라인 도달
    private float y;

    public System.Action<Enemy> OnReachedDeadline;
    public System.Action<Enemy> OnDied;

    public Enemy(char letter, float x, float startY, float speed, float deadlineY)
    {
        Letter = letter;
        this.speed = speed;
        this.deadlineY = deadlineY;
        y = startY;

        Element = new Label(letter.ToString());
        Element.AddToClassList("enemy-letter");
        Element.style.position = Position.Absolute;
        Element.style.left = x;
        Element.style.top = y;
    }

    /// <summary>스포너의 Update에서 매 프레임 호출</summary>
    public void Tick(float deltaTime)
    {
        y += speed * deltaTime;
        Element.style.top = y;

        if (y >= deadlineY)
            OnReachedDeadline?.Invoke(this);
    }

    /// <summary>글자를 맞게 썼을 때 호출 (이후 필기 인식 쪽에서 사용)</summary>
    public void Die()
    {
        OnDied?.Invoke(this);
    }

    public void Remove()
    {
        Element.RemoveFromHierarchy();
    }
}

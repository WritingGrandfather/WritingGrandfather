using UnityEngine;

/// <summary>
/// 유저가 글씨를 쓰는 "칸" 하나를 정의한다.
/// 이 오브젝트의 위치를 중심으로 size 만큼의 사각 영역이 칸이 되며,
/// StrokeCapture가 이 영역 기준으로 획 좌표를 정규화한다.
/// targetText는 이 칸에서 써야 할 목표 글자(선택).
/// </summary>
public class WritingCell : MonoBehaviour
{
    [Tooltip("칸의 가로/세로 크기 (월드 단위)")]
    public Vector2 size = new Vector2(2f, 2f);

    [Tooltip("이 칸에서 써야 할 목표 글자 (없으면 비워둠)")]
    public string targetText = "";

    /// <summary>칸의 월드 공간 사각 영역. 중심은 transform.position.</summary>
    public Rect WorldRect
    {
        get
        {
            Vector3 c = transform.position;
            return new Rect(c.x - size.x * 0.5f, c.y - size.y * 0.5f, size.x, size.y);
        }
    }

    public Vector3 Center => transform.position;

    // 씬 뷰에서 칸 영역을 초록 사각형으로 표시
    void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(transform.position, new Vector3(size.x, size.y, 0f));
    }
}

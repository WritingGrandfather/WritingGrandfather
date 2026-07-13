using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// DrowLine이 그린 획(LineRenderer)들을 읽어 "획 좌표 데이터(JSON)"로 만든다. (PNG 대신 라인렌더러 기반)
///
/// 각 획은 그린 순서대로, 점 좌표는 칸(WritingCell) 기준 0~1로 정규화한다.
///  - x: 좌→우, y: 아래→위
///  - 획 순서 = 그린 순서 (자식 순서), 획 개수 = 배열 길이
///
/// 출력 예: {"strokes":[[[0.1,0.9],[0.5,0.5]],[[0.2,0.3],...]]}
/// </summary>
public class StrokeCapture : MonoBehaviour
{
    [Tooltip("획을 읽어올 DrowLine (획들은 이 오브젝트의 자식 LineRenderer로 존재)")]
    [SerializeField] DrowLine drawLine;

    [Tooltip("좌표 소수 자릿수 (작을수록 토큰 절약)")]
    public int decimals = 3;

    public void SetDrawLine(DrowLine dl) => drawLine = dl;

    /// <summary>칸 기준으로 정규화한 획 좌표 JSON을 반환한다.</summary>
    public string CaptureJson(WritingCell cell)
    {
        if (drawLine == null || cell == null) return "{\"strokes\":[]}";

        Rect rect = cell.WorldRect;
        string fmt = "F" + Mathf.Clamp(decimals, 0, 6);
        var inv = CultureInfo.InvariantCulture; // 지역설정(콤마 소수점) 때문에 JSON 깨지지 않게

        var sb = new StringBuilder();
        sb.Append("{\"strokes\":[");

        Transform parent = drawLine.transform;
        bool firstStroke = true;

        for (int c = 0; c < parent.childCount; c++)
        {
            var lr = parent.GetChild(c).GetComponent<LineRenderer>();
            if (lr == null || lr.positionCount < 1) continue;
            if (!lr.gameObject.activeSelf) continue; // 풀로 반환된(지워진) 획 제외

            if (!firstStroke) sb.Append(",");
            firstStroke = false;

            sb.Append("[");
            for (int i = 0; i < lr.positionCount; i++)
            {
                Vector3 w = lr.GetPosition(i);
                float nx = Mathf.Clamp01((w.x - rect.xMin) / rect.width);
                float ny = Mathf.Clamp01((w.y - rect.yMin) / rect.height);
                if (i > 0) sb.Append(",");
                sb.Append("[").Append(nx.ToString(fmt, inv)).Append(",").Append(ny.ToString(fmt, inv)).Append("]");
            }
            sb.Append("]");
        }

        sb.Append("]}");
        return sb.ToString();
    }
}

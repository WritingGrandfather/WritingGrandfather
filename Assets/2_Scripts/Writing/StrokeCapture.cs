using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// DrawLine이 그린 획(LineRenderer)들을 읽어 "획 좌표 데이터(JSON)"로 만든다.
///
/// AI가 판독하기 쉽도록 원시 좌표 외에 획별 특징을 미리 계산해서 담는다:
///  - strokeCount: 총 획수
///  - 각 획의 start/end (시작·끝점), path (방향 경로. 예: "down,right" = ㄴ 모양)
///  - points: 다운샘플링한 좌표 (0~1 정규화, x: 좌→우, y: 위→아래 = 이미지와 동일)
///
/// 출력 예:
/// {"strokeCount":2,"strokes":[{"start":[0.2,0.2],"end":[0.4,0.5],"path":"down,right","points":[...]},...]}
/// </summary>
public class StrokeCapture : MonoBehaviour
{
    [Tooltip("획을 읽어올 DrawLine (획들은 이 오브젝트의 자식 LineRenderer로 존재)")]
    [SerializeField] DrawLine drawLine;

    [Tooltip("좌표 소수 자릿수 (작을수록 토큰 절약)")]
    public int decimals = 3;

    [Tooltip("다운샘플링 최소 간격 (정규화 단위). 이보다 가까운 점은 생략")]
    public float minPointGap = 0.02f;

    [Tooltip("방향 전환으로 인정할 최소 각도 변화 (도)")]
    public float turnAngle = 50f;

    public void SetDrawLine(DrawLine dl) => drawLine = dl;

    /// <summary>
    /// 잉크 영역 기준으로 0~1 정규화(다운샘플링 포함)한 획 좌표들을 반환한다. (y: 위→아래)
    ///
    /// ★ 셀(WorldRect) 기준이 아니라 실제 그린 잉크의 바운딩 박스 기준이다.
    ///   셀 위치가 어긋나 있어도 좌표가 깨지지 않고, 비율이 유지된다(긴 변 = 1).
    ///   클릭으로 생긴 길이 0짜리 점 획은 자동으로 걸러낸다.
    /// </summary>
    public List<List<Vector2>> GetNormalizedStrokes(WritingCell cell)
    {
        var result = new List<List<Vector2>>();
        if (drawLine == null) return result;

        // 1) 월드 좌표 수집 + 길이 0(클릭 점) 획 제거
        var raw = new List<List<Vector2>>();
        Transform parent = drawLine.transform;
        for (int c = 0; c < parent.childCount; c++)
        {
            var lr = parent.GetChild(c).GetComponent<LineRenderer>();
            if (lr == null || lr.positionCount < 1) continue;
            if (!lr.gameObject.activeSelf) continue; // 풀로 반환된(지워진) 획 제외

            var pts = new List<Vector2>();
            float length = 0f;
            for (int i = 0; i < lr.positionCount; i++)
            {
                Vector3 w = lr.GetPosition(i);
                var p = new Vector2(w.x, w.y);
                if (pts.Count > 0) length += Vector2.Distance(pts[pts.Count - 1], p);
                pts.Add(p);
            }
            if (pts.Count < 2 || length < 1e-4f) continue; // 스치듯 찍힌 점은 글자가 아님
            raw.Add(pts);
        }
        if (raw.Count == 0) return result;

        // 2) 잉크 바운딩 박스
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var stroke in raw)
            foreach (var p in stroke)
            {
                if (p.x < minX) minX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.x > maxX) maxX = p.x;
                if (p.y > maxY) maxY = p.y;
            }
        float size = Mathf.Max(maxX - minX, maxY - minY, 1e-4f); // 긴 변 기준 = 비율 유지

        // 3) 정규화 (+다운샘플링). y는 이미지 좌표계처럼 위→아래
        foreach (var stroke in raw)
        {
            var pts = new List<Vector2>();
            for (int i = 0; i < stroke.Count; i++)
            {
                var p = new Vector2((stroke[i].x - minX) / size, (maxY - stroke[i].y) / size);
                if (pts.Count == 0 || Vector2.Distance(pts[pts.Count - 1], p) >= minPointGap || i == stroke.Count - 1)
                    pts.Add(p);
            }
            result.Add(pts);
        }
        return result;
    }

    /// <summary>칸 기준으로 정규화한 획 데이터 JSON을 반환한다.</summary>
    public string CaptureJson(WritingCell cell)
    {
        return BuildJson(GetNormalizedStrokes(cell));
    }

    /// <summary>
    /// 칸(WorldRect) 기준 좌표로 정규화한 획들. (y: 위→아래, 클램프 없음 — 칸 밖은 0~1 범위를 벗어남)
    /// 따라쓰기 채점처럼 "어디에 썼는지"가 중요할 때 사용.
    /// </summary>
    public List<List<Vector2>> GetCellNormalizedStrokes(WritingCell cell)
    {
        var result = new List<List<Vector2>>();
        if (drawLine == null || cell == null) return result;

        Rect rect = cell.WorldRect;
        Transform parent = drawLine.transform;
        for (int c = 0; c < parent.childCount; c++)
        {
            var lr = parent.GetChild(c).GetComponent<LineRenderer>();
            if (lr == null || lr.positionCount < 2) continue;
            if (!lr.gameObject.activeSelf) continue;

            var pts = new List<Vector2>();
            float length = 0f;
            Vector2 prevW = Vector2.zero;
            for (int i = 0; i < lr.positionCount; i++)
            {
                Vector3 w = lr.GetPosition(i);
                if (i > 0) length += Vector2.Distance(prevW, new Vector2(w.x, w.y));
                prevW = new Vector2(w.x, w.y);

                float nx = (w.x - rect.xMin) / rect.width;
                float ny = 1f - (w.y - rect.yMin) / rect.height;
                var p = new Vector2(nx, ny);
                if (pts.Count == 0 || Vector2.Distance(pts[pts.Count - 1], p) >= minPointGap || i == lr.positionCount - 1)
                    pts.Add(p);
            }
            if (pts.Count < 2 || length < 1e-4f) continue; // 클릭 점 제거
            result.Add(pts);
        }
        return result;
    }

    string BuildJson(List<List<Vector2>> strokes)
    {
        string fmt = "F" + Mathf.Clamp(decimals, 0, 6);
        var inv = CultureInfo.InvariantCulture;

        var sb = new StringBuilder();
        sb.Append("{\"strokeCount\":").Append(strokes.Count).Append(",\"strokes\":[");

        for (int s = 0; s < strokes.Count; s++)
        {
            var pts = strokes[s];
            if (s > 0) sb.Append(",");

            Vector2 start = pts[0];
            Vector2 end = pts[pts.Count - 1];

            sb.Append("{\"start\":").Append(Pt(start, fmt, inv));
            sb.Append(",\"end\":").Append(Pt(end, fmt, inv));
            sb.Append(",\"path\":\"").Append(BuildPath(pts)).Append("\"");
            // 전체 좌표 대신 핵심점(시작·꺾임·끝)만 전송 → 토큰 대폭 절약 = 응답 속도↑
            var keys = GetKeyPoints(pts);
            sb.Append(",\"keyPoints\":[");
            for (int i = 0; i < keys.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(Pt(keys[i], fmt, inv));
            }
            sb.Append("]}");
        }

        sb.Append("]}");
        return sb.ToString();
    }

    static string Pt(Vector2 p, string fmt, CultureInfo inv) =>
        "[" + p.x.ToString(fmt, inv) + "," + p.y.ToString(fmt, inv) + "]";

    /// <summary>
    /// 획의 방향 경로를 만든다. 예: ㄴ 모양 획 → "down,right", ㄱ 모양 획 → "right,down"
    /// 진행 방향이 turnAngle 이상 꺾이면 새 구간으로 나눈다.
    /// </summary>
    string BuildPath(List<Vector2> pts)
    {
        if (pts.Count < 2) return "dot";

        var dirs = new List<string>();
        Vector2 segDir = Vector2.zero;

        for (int i = 1; i < pts.Count; i++)
        {
            Vector2 d = pts[i] - pts[i - 1];
            if (d.magnitude < 0.005f) continue;

            if (segDir == Vector2.zero)
            {
                segDir = d.normalized;
                dirs.Add(DirName(segDir));
                continue;
            }

            // 누적 방향과 비교해서 크게 꺾이면 새 구간
            if (Vector2.Angle(segDir, d) >= turnAngle)
            {
                segDir = d.normalized;
                string name = DirName(segDir);
                if (dirs.Count == 0 || dirs[dirs.Count - 1] != name)
                    dirs.Add(name);
            }
            else
            {
                // 완만한 변화는 방향 누적 (곡선 대응)
                segDir = Vector2.Lerp(segDir, d.normalized, 0.3f).normalized;
            }

            if (dirs.Count >= 6) break; // 너무 길면 잘라냄 (낙서 방지)
        }

        return dirs.Count == 0 ? "dot" : string.Join(",", dirs);
    }

    /// <summary>획의 핵심점: 시작점 + 방향이 꺾이는 지점들 + 끝점. (모양 정보는 유지, 토큰은 최소화)</summary>
    List<Vector2> GetKeyPoints(List<Vector2> pts)
    {
        var keys = new List<Vector2> { pts[0] };
        if (pts.Count < 2) return keys;

        Vector2 segDir = Vector2.zero;
        for (int i = 1; i < pts.Count; i++)
        {
            Vector2 d = pts[i] - pts[i - 1];
            if (d.magnitude < 0.005f) continue;

            if (segDir == Vector2.zero)
            {
                segDir = d.normalized;
                continue;
            }

            if (Vector2.Angle(segDir, d) >= turnAngle)
            {
                segDir = d.normalized;
                if (keys.Count < 10) keys.Add(pts[i - 1]); // 꺾인 지점
            }
            else
            {
                segDir = Vector2.Lerp(segDir, d.normalized, 0.3f).normalized;
            }
        }

        keys.Add(pts[pts.Count - 1]);
        return keys;
    }

    /// <summary>방향 벡터 → 8방위 이름 (y는 아래가 +)</summary>
    static string DirName(Vector2 d)
    {
        float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y);
        string h = d.x > 0 ? "right" : "left";
        string v = d.y > 0 ? "down" : "up";
        if (ax > ay * 2f) return h;
        if (ay > ax * 2f) return v;
        return v + "-" + h;
    }
}

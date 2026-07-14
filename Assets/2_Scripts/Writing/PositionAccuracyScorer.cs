using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "글자 크기·위치 정확도" 채점기.
/// StrokeCapture.GetCellNormalizedStrokes(cell) 결과(칸 기준 0~1 좌표, 클램프 없음)를 받아
///  - 칸을 벗어나 그렸는가 (이탈)
///  - 칸 대비 적당한 크기로 썼는가 (너무 작거나 크지 않은지)
///  - 칸 중앙에 맞춰 썼는가 (치우침)
/// 세 가지를 종합해 0~100점으로 낸다. AI 유사도/획순 정확도와 독립적인 지표.
/// </summary>
public static class PositionAccuracyScorer
{
    const float IdealFill = 0.7f;   // 칸 한 변 대비 이상적인 잉크 크기 비율
    const float MaxCenterOffset = 0.35f; // 이보다 중심이 벗어나면 위치 점수 0
    const float OutsideWeight = 2f; // 이탈 비율에 곱하는 가중치 (엄하게)

    public static int Score(List<List<Vector2>> cellStrokes)
    {
        if (cellStrokes == null || cellStrokes.Count == 0) return 0;

        int total = 0, outside = 0;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;

        foreach (var stroke in cellStrokes)
        {
            foreach (var p in stroke)
            {
                total++;
                if (p.x < 0f || p.x > 1f || p.y < 0f || p.y > 1f) outside++;

                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }
        }
        if (total == 0) return 0;

        // 크기 점수: 잉크 바운딩박스가 칸 한 변 대비 이상적 비율(70%)에서 얼마나 벗어났는지
        float boxW = Mathf.Clamp01(maxX - minX);
        float boxH = Mathf.Clamp01(maxY - minY);
        float fill = (boxW + boxH) * 0.5f;
        float sizeDeviation = Mathf.Abs(fill - IdealFill) / IdealFill;
        float sizeScore = 100f * (1f - Mathf.Clamp01(sizeDeviation));

        // 위치(중앙 정렬) 점수: 잉크 중심이 칸 중앙(0.5, 0.5)에서 얼마나 벗어났는지
        float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f;
        float centerOffset = Vector2.Distance(new Vector2(cx, cy), new Vector2(0.5f, 0.5f));
        float centerScore = 100f * (1f - Mathf.Clamp01(centerOffset / MaxCenterOffset));

        // 이탈 점수: 칸 밖으로 나간 점의 비율 (엄하게 — 절반만 벗어나도 0점에 가까움)
        float outsideFrac = (float)outside / total;
        float outsideScore = 100f * (1f - Mathf.Clamp01(outsideFrac * OutsideWeight));

        float combined = sizeScore * 0.4f + centerScore * 0.35f + outsideScore * 0.25f;
        return Mathf.RoundToInt(Mathf.Clamp(combined, 0f, 100f));
    }
}

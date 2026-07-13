using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 표준 한글 필순을 코드에 정의해두고 유저 획과 기하학적으로 대조하는 로컬 획순 검사기.
/// (AI 없이 결정적으로 판정 — 자모 안 순서, 자모 간 순서, 획 방향까지 전부 검사)
///
/// 원리:
///  1) 목표 글자를 자모 분해 → 자모별 표준 획(순서·방향 포함)을 글자 레이아웃에 배치한 "필순 계획" 생성
///  2) 유저 획을 순서대로 계획의 가장 비슷한 획에 매칭
///  3) 매칭된 계획 획의 순서가 뒤집혀 있으면 순서 오류, 반대로 그었으면 방향 오류
/// </summary>
public static class StrokeOrderValidator
{
    public class Result
    {
        public bool supported; // false면 판정 보류 (지원 안 되는 자모, 획수 차이 과다 등)
        public bool ok = true;
        public string message;
        public int score = 100; // 0~100. 위반 하나당 감점 (방향 오류·순서 역전 각각 카운트)
    }

    class PlanStroke
    {
        public char jamo;       // 이 획이 속한 자모 (메시지용)
        public Vector2[] pts;   // 시작→끝 (글자 좌표, y: 위→아래)
        public Rect box;        // 자모 배치 영역 (위치 게이트용)
    }

    // ────────────────────────────────────────────────────────────────

    public static Result Validate(char syllable, List<List<Vector2>> strokes)
    {
        var res = new Result();
        if (strokes == null || strokes.Count == 0) return res;
        if (!HangulComposer.Decompose(syllable, out char cho, out char jung, out char jong)) return res;

        List<PlanStroke> plan = BuildPlan(cho, jung, jong);
        if (plan == null) return res; // 지원 안 되는 자모 → 보류

        // 획수 차이가 크면 매칭 신뢰 불가 → 보류 (이어쓰기 감점은 정자 검사가 담당)
        if (strokes.Count < plan.Count - 1 || strokes.Count > plan.Count + 2) return res;

        // 유저 잉크를 유닛 정사각형으로 (계획 좌표계와 맞춤)
        List<Vector2[]> user = NormalizeToUnitSquare(strokes);
        int U = user.Count, N = plan.Count;
        if (N > 14) return res; // DP 마스크 한계

        // 비용 행렬: 위치(시작/중간/끝) + 방향(가로/세로 불일치 페널티)
        float[,] cost = new float[U, N];
        bool[,] revFlag = new bool[U, N];
        var lengths = new float[U];

        for (int u = 0; u < U; u++)
        {
            Vector2[] s = user[u];
            Vector2 uS = s[0], uM = s[s.Length / 2], uE = s[s.Length - 1];
            Vector2 uDir = uE - uS;
            float uLen = 0f;
            for (int k = 1; k < s.Length; k++) uLen += Vector2.Distance(s[k - 1], s[k]);
            lengths[u] = uLen;
            uDir = uDir.magnitude > 0.001f ? uDir.normalized : Vector2.zero;

            // 획 중심점 (자모 영역 게이트용)
            Vector2 uCenter = Vector2.zero;
            foreach (var pt in s) uCenter += pt;
            uCenter /= s.Length;

            for (int p = 0; p < N; p++)
            {
                Vector2[] q = plan[p].pts;
                Vector2 pS = q[0], pM = q[q.Length / 2], pE = q[q.Length - 1];
                Vector2 pDir = pE - pS;
                pDir = pDir.magnitude > 0.001f ? pDir.normalized : Vector2.zero;

                float fwd = Vector2.Distance(uS, pS) + Vector2.Distance(uM, pM) + Vector2.Distance(uE, pE);
                float rv = Vector2.Distance(uS, pE) + Vector2.Distance(uM, pM) + Vector2.Distance(uE, pS);

                // 방향 불일치 페널티: 세로획이 가로획에 매칭되는 것을 강하게 차단
                float orient = (uDir == Vector2.zero || pDir == Vector2.zero)
                    ? 0f
                    : 1f - Mathf.Abs(Vector2.Dot(uDir, pDir));

                // 자모 영역 게이트: 획의 중심이 이 계획 획이 속한 자모 영역에서 멀수록 페널티,
                // 아예 멀면(0.15 초과) 사실상 매칭 금지 — 위치 간격으로 자모를 구분
                float gate = DistanceToRect(uCenter, plan[p].box);
                float region = gate > 0.15f ? 5f : gate * 2f;

                cost[u, p] = Mathf.Min(fwd, rv) + orient * 1.2f + region;
                revFlag[u, p] = rv + 0.15f < fwd;
            }
        }

        // 전체 최적 매칭 (DP 비트마스크) — 그리디의 연쇄 오매칭 방지
        const float SkipCost = 0.9f;       // 유저 잡획 하나 건너뛰는 비용
        const float UnusedPlanCost = 0.4f; // 계획 획이 매칭 안 되고 남는 비용
        int full = 1 << N;
        const float INF = 1e9f;

        var dp = new float[U + 1, full];
        var parent = new int[U + 1, full]; // -1: 건너뜀, 그 외: 매칭한 계획 획
        for (int i = 0; i <= U; i++)
            for (int m = 0; m < full; m++)
                dp[i, m] = INF;
        dp[0, 0] = 0f;

        for (int i = 0; i < U; i++)
        {
            for (int m = 0; m < full; m++)
            {
                if (dp[i, m] >= INF) continue;

                // 이 유저 획을 잡획으로 건너뜀
                if (dp[i, m] + SkipCost < dp[i + 1, m])
                {
                    dp[i + 1, m] = dp[i, m] + SkipCost;
                    parent[i + 1, m] = -1;
                }
                // 남은 계획 획에 매칭
                for (int p = 0; p < N; p++)
                {
                    if ((m & (1 << p)) != 0) continue;
                    int nm = m | (1 << p);

                    // 순서 우대: 이미 매칭된 획 중 계획 순서가 p보다 뒤인 것(=역전)마다 페널티.
                    // 위치가 애매한 비슷한 획(짧은 가로 2개 등)은 표준 순서대로 매칭되고,
                    // 진짜 순서를 바꿔 쓴 경우는 위치 차이가 커서 여전히 역전 매칭이 이긴다.
                    int inversions = CountBits(m >> (p + 1));
                    float nc = dp[i, m] + cost[i, p] + inversions * 0.25f;

                    if (nc < dp[i + 1, nm])
                    {
                        dp[i + 1, nm] = nc;
                        parent[i + 1, nm] = p;
                    }
                }
            }
        }

        // 최적 마스크 선택 (남은 계획 획은 개당 페널티)
        int bestMask = 0;
        float bestTotal = INF;
        for (int m = 0; m < full; m++)
        {
            if (dp[U, m] >= INF) continue;
            int unused = N - CountBits(m);
            float total = dp[U, m] + unused * UnusedPlanCost;
            if (total < bestTotal)
            {
                bestTotal = total;
                bestMask = m;
            }
        }

        // 역추적으로 유저 획별 매칭 복원
        var assigned = new int[U]; // -1 = 건너뜀
        {
            int m = bestMask;
            for (int i = U; i >= 1; i--)
            {
                int p = parent[i, m];
                assigned[i - 1] = p;
                if (p >= 0) m &= ~(1 << p);
            }
        }

        var seq = new List<int>();
        var reversed = new List<bool>();
        var seqLens = new List<float>();
        for (int u = 0; u < U; u++)
        {
            if (assigned[u] < 0) continue;
            seq.Add(assigned[u]);
            reversed.Add(revFlag[u, assigned[u]]);
            seqLens.Add(lengths[u]);
        }

        res.supported = true;

        // 디버그: 매칭 결과 출력 (유저 획 번호 → 표준 획)
        var dbg = new System.Text.StringBuilder("[StrokeOrder/로컬] 매칭: ");
        for (int i = 0; i < seq.Count; i++)
            dbg.Append($"{i + 1}번획→{plan[seq[i]].jamo}(순서{seq[i]}){(reversed[i] ? "[역방향]" : "")}  ");
        Debug.Log(dbg.ToString());

        // 위반을 전부 세어 점수화한다 (첫 건에서 멈추지 않음 — "정확도 %"를 위해 필요).
        // message는 사용자에게 보여줄 한 문장만 필요하므로 처음 발견한 위반의 설명을 그대로 쓴다.
        int violations = 0;

        // 1) 방향 검사 — 긴 획이 반대 방향이면 오류
        for (int i = 0; i < seq.Count; i++)
        {
            if (reversed[i] && seqLens[i] > 0.4f)
            {
                violations++;
                if (res.message == null)
                {
                    char jamo = plan[seq[i]].jamo;
                    res.message = $"'{jamo}'의 획 방향이 반대예요. 가로는 왼쪽→오른쪽, 세로는 위→아래로 그어볼까요?";
                }
            }
        }

        // 2) 순서 검사 — 매칭된 계획 인덱스에 역전이 있으면 오류
        for (int i = 0; i < seq.Count; i++)
        {
            for (int j = i + 1; j < seq.Count; j++)
            {
                if (seq[i] > seq[j])
                {
                    violations++;
                    if (res.message == null)
                    {
                        char earlier = plan[seq[j]].jamo; // 먼저 썼어야 하는 획의 자모
                        char later = plan[seq[i]].jamo;   // 실제로 먼저 쓴 획의 자모
                        res.message = earlier == later
                            ? $"'{earlier}'의 획 순서가 표준과 달라요. 위에서 아래, 왼쪽에서 오른쪽 순서로 써볼까요?"
                            : $"'{earlier}'를 먼저 쓰고 '{later}'를 써볼까요?";
                    }
                }
            }
        }

        res.ok = violations == 0;
        res.score = Mathf.Clamp(100 - violations * 25, 0, 100);
        return res;
    }

    static int CountBits(int v)
    {
        int c = 0;
        while (v != 0) { c += v & 1; v >>= 1; }
        return c;
    }

    /// <summary>점과 사각형의 거리 (안이면 0)</summary>
    static float DistanceToRect(Vector2 p, Rect r)
    {
        float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax);
        float dy = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    // ── 필순 계획 생성 ───────────────────────────────────────────────

    // 복합모음 = 가로 부분 + 세로 부분 결합
    static bool TrySplitMixedVowel(char jung, out char hPart, out char vPart)
    {
        switch (jung)
        {
            case 'ㅘ': hPart = 'ㅗ'; vPart = 'ㅏ'; return true;
            case 'ㅙ': hPart = 'ㅗ'; vPart = 'ㅐ'; return true;
            case 'ㅚ': hPart = 'ㅗ'; vPart = 'ㅣ'; return true;
            case 'ㅝ': hPart = 'ㅜ'; vPart = 'ㅓ'; return true;
            case 'ㅞ': hPart = 'ㅜ'; vPart = 'ㅔ'; return true;
            case 'ㅟ': hPart = 'ㅜ'; vPart = 'ㅣ'; return true;
            case 'ㅢ': hPart = 'ㅡ'; vPart = 'ㅣ'; return true;
            default: hPart = vPart = '\0'; return false;
        }
    }

    static List<PlanStroke> BuildPlan(char cho, char jung, char jong)
    {
        bool vertical = "ㅏㅐㅑㅒㅓㅔㅕㅖㅣ".IndexOf(jung) >= 0;
        bool horizontal = "ㅗㅛㅜㅠㅡ".IndexOf(jung) >= 0;
        bool mixed = TrySplitMixedVowel(jung, out char hPart, out char vPart);
        if (!vertical && !horizontal && !mixed) return null;

        bool hasJong = jong != '\0';
        var plan = new List<PlanStroke>();
        Rect jongBox = new Rect(0.15f, 0.66f, 0.7f, 0.31f);

        if (vertical)
        {
            Rect choBox = hasJong ? new Rect(0.03f, 0.03f, 0.45f, 0.47f) : new Rect(0.03f, 0.08f, 0.47f, 0.62f);
            Rect jungBox = hasJong ? new Rect(0.52f, 0.02f, 0.45f, 0.58f) : new Rect(0.55f, 0.02f, 0.42f, 0.9f);
            if (!AppendJamo(plan, cho, choBox)) return null;
            if (!AppendJamo(plan, jung, jungBox)) return null;
        }
        else if (horizontal)
        {
            Rect choBox = hasJong ? new Rect(0.2f, 0.02f, 0.6f, 0.32f) : new Rect(0.18f, 0.05f, 0.64f, 0.42f);
            Rect jungBox = hasJong ? new Rect(0.03f, 0.36f, 0.94f, 0.26f) : new Rect(0.03f, 0.5f, 0.94f, 0.38f);
            if (!AppendJamo(plan, cho, choBox)) return null;
            if (!AppendJamo(plan, jung, jungBox)) return null;
        }
        else // mixed (ㅘ류): 초성 좌상, 가로모음 그 아래 왼쪽, 세로모음 오른쪽
        {
            Rect choBox = hasJong ? new Rect(0.03f, 0.02f, 0.45f, 0.32f) : new Rect(0.05f, 0.03f, 0.45f, 0.4f);
            Rect hBox = hasJong ? new Rect(0.02f, 0.36f, 0.55f, 0.24f) : new Rect(0.02f, 0.48f, 0.58f, 0.34f);
            Rect vBox = hasJong ? new Rect(0.62f, 0.02f, 0.35f, 0.58f) : new Rect(0.64f, 0.02f, 0.33f, 0.9f);
            if (!AppendJamo(plan, cho, choBox)) return null;
            if (!AppendJamo(plan, hPart, hBox)) return null;
            if (!AppendJamo(plan, vPart, vBox)) return null;
        }

        if (hasJong && !AppendJamo(plan, jong, jongBox)) return null;
        return plan;
    }

    static bool AppendJamo(List<PlanStroke> plan, char jamo, Rect box)
    {
        Vector2[][] strokes = JamoStrokes(jamo);
        if (strokes == null) return false;

        foreach (var s in strokes)
        {
            var pts = new Vector2[s.Length];
            for (int i = 0; i < s.Length; i++)
                pts[i] = new Vector2(box.xMin + s[i].x * box.width, box.yMin + s[i].y * box.height);
            plan.Add(new PlanStroke { jamo = jamo, pts = pts, box = box });
        }
        return true;
    }

    static Vector2 P(float x, float y) => new Vector2(x, y);

    /// <summary>자모별 표준 필순 (유닛 박스, y: 위→아래, 배열 순서 = 필순, 점 순서 = 획 방향)</summary>
    static Vector2[][] JamoStrokes(char j)
    {
        switch (j)
        {
            case 'ㄱ': return new[] { new[] { P(0, 0), P(1, 0), P(1, 1) } };
            case 'ㄴ': return new[] { new[] { P(0, 0), P(0, 1), P(1, 1) } };
            case 'ㄷ': return new[] {
                new[] { P(0, 0), P(1, 0) },
                new[] { P(0, 0), P(0, 1), P(1, 1) } };
            case 'ㄹ': return new[] {
                new[] { P(0, 0), P(1, 0), P(1, 0.5f) },
                new[] { P(0, 0.5f), P(1, 0.5f) },
                new[] { P(0, 0.5f), P(0, 1), P(1, 1) } };
            case 'ㅁ': return new[] {
                new[] { P(0, 0), P(0, 1) },
                new[] { P(0, 0), P(1, 0), P(1, 1) },
                new[] { P(0, 1), P(1, 1) } };
            case 'ㅂ': return new[] {
                new[] { P(0, 0), P(0, 1) },
                new[] { P(1, 0), P(1, 1) },
                new[] { P(0, 0.45f), P(1, 0.45f) },
                new[] { P(0, 1), P(1, 1) } };
            case 'ㅅ': return new[] {
                new[] { P(0.55f, 0), P(0.3f, 0.5f), P(0, 1) },
                new[] { P(0.5f, 0.35f), P(1, 1) } };
            case 'ㅇ': return new[] {
                new[] { P(0.5f, 0), P(0, 0.5f), P(0.5f, 1), P(1, 0.5f), P(0.5f, 0.05f) } };
            case 'ㅈ': return new[] {
                new[] { P(0, 0), P(1, 0) },
                new[] { P(0.5f, 0), P(0.25f, 0.5f), P(0, 1) },
                new[] { P(0.45f, 0.4f), P(1, 1) } };
            case 'ㅊ': return new[] {
                new[] { P(0.3f, 0), P(0.7f, 0.05f) },
                new[] { P(0, 0.25f), P(1, 0.25f) },
                new[] { P(0.5f, 0.25f), P(0.25f, 0.6f), P(0, 1) },
                new[] { P(0.45f, 0.55f), P(1, 1) } };
            case 'ㅋ': return new[] {
                new[] { P(0, 0), P(1, 0), P(1, 1) },
                new[] { P(0, 0.5f), P(0.85f, 0.5f) } };
            case 'ㅌ': return new[] {
                new[] { P(0, 0), P(1, 0) },
                new[] { P(0, 0.5f), P(0.9f, 0.5f) },
                new[] { P(0, 0), P(0, 1), P(1, 1) } };
            case 'ㅍ': return new[] {
                new[] { P(0, 0), P(1, 0) },
                new[] { P(0.28f, 0.05f), P(0.22f, 0.95f) },
                new[] { P(0.72f, 0.05f), P(0.78f, 0.95f) },
                new[] { P(0, 1), P(1, 1) } };
            case 'ㅎ': return new[] {
                new[] { P(0.3f, 0), P(0.7f, 0.05f) },
                new[] { P(0.05f, 0.28f), P(0.95f, 0.28f) },
                new[] { P(0.5f, 0.4f), P(0.1f, 0.7f), P(0.5f, 1), P(0.9f, 0.7f), P(0.5f, 0.42f) } };

            // 모음 (세로형)
            case 'ㅏ': return new[] {
                new[] { P(0.25f, 0), P(0.25f, 1) },
                new[] { P(0.25f, 0.5f), P(1, 0.5f) } };
            case 'ㅑ': return new[] {
                new[] { P(0.25f, 0), P(0.25f, 1) },
                new[] { P(0.25f, 0.33f), P(1, 0.33f) },
                new[] { P(0.25f, 0.66f), P(1, 0.66f) } };
            case 'ㅓ': return new[] {
                new[] { P(0, 0.5f), P(0.7f, 0.5f) },
                new[] { P(0.72f, 0), P(0.72f, 1) } };
            case 'ㅕ': return new[] {
                new[] { P(0, 0.33f), P(0.68f, 0.33f) },
                new[] { P(0, 0.66f), P(0.68f, 0.66f) },
                new[] { P(0.72f, 0), P(0.72f, 1) } };
            case 'ㅣ': return new[] { new[] { P(0.5f, 0), P(0.5f, 1) } };
            case 'ㅒ': return new[] {
                new[] { P(0.15f, 0), P(0.15f, 1) },
                new[] { P(0.15f, 0.33f), P(0.6f, 0.33f) },
                new[] { P(0.15f, 0.66f), P(0.6f, 0.66f) },
                new[] { P(0.62f, 0), P(0.62f, 1) } };
            case 'ㅖ': return new[] {
                new[] { P(0, 0.33f), P(0.5f, 0.33f) },
                new[] { P(0, 0.66f), P(0.5f, 0.66f) },
                new[] { P(0.52f, 0), P(0.52f, 1) },
                new[] { P(0.85f, 0), P(0.85f, 1) } };
            case 'ㅐ': return new[] {
                new[] { P(0.15f, 0), P(0.15f, 1) },
                new[] { P(0.15f, 0.5f), P(0.6f, 0.5f) },
                new[] { P(0.62f, 0), P(0.62f, 1) } };
            case 'ㅔ': return new[] {
                new[] { P(0, 0.5f), P(0.5f, 0.5f) },
                new[] { P(0.52f, 0), P(0.52f, 1) },
                new[] { P(0.85f, 0), P(0.85f, 1) } };

            // 모음 (가로형)
            case 'ㅗ': return new[] {
                new[] { P(0.5f, 0), P(0.5f, 0.6f) },
                new[] { P(0, 0.62f), P(1, 0.62f) } };
            case 'ㅛ': return new[] {
                new[] { P(0.33f, 0), P(0.33f, 0.6f) },
                new[] { P(0.66f, 0), P(0.66f, 0.6f) },
                new[] { P(0, 0.62f), P(1, 0.62f) } };
            case 'ㅜ': return new[] {
                new[] { P(0, 0.3f), P(1, 0.3f) },
                new[] { P(0.5f, 0.32f), P(0.5f, 1) } };
            case 'ㅠ': return new[] {
                new[] { P(0, 0.3f), P(1, 0.3f) },
                new[] { P(0.35f, 0.32f), P(0.35f, 1) },
                new[] { P(0.65f, 0.32f), P(0.65f, 1) } };
            case 'ㅡ': return new[] { new[] { P(0, 0.5f), P(1, 0.5f) } };

            // 쌍자음 (같은 자음 좌우 결합)
            case 'ㄲ': return Composite('ㄱ', 'ㄱ');
            case 'ㄸ': return Composite('ㄷ', 'ㄷ');
            case 'ㅃ': return Composite('ㅂ', 'ㅂ');
            case 'ㅆ': return Composite('ㅅ', 'ㅅ');
            case 'ㅉ': return Composite('ㅈ', 'ㅈ');

            // 겹받침 (서로 다른 자음 좌우 결합)
            case 'ㄳ': return Composite('ㄱ', 'ㅅ');
            case 'ㄵ': return Composite('ㄴ', 'ㅈ');
            case 'ㄶ': return Composite('ㄴ', 'ㅎ');
            case 'ㄺ': return Composite('ㄹ', 'ㄱ');
            case 'ㄻ': return Composite('ㄹ', 'ㅁ');
            case 'ㄼ': return Composite('ㄹ', 'ㅂ');
            case 'ㄽ': return Composite('ㄹ', 'ㅅ');
            case 'ㄾ': return Composite('ㄹ', 'ㅌ');
            case 'ㄿ': return Composite('ㄹ', 'ㅍ');
            case 'ㅀ': return Composite('ㄹ', 'ㅎ');
            case 'ㅄ': return Composite('ㅂ', 'ㅅ');

            default: return null;
        }
    }

    /// <summary>두 자음을 좌우로 결합 (쌍자음/겹받침). 필순: 왼쪽 자음 전부 → 오른쪽 자음 전부.</summary>
    static Vector2[][] Composite(char left, char right)
    {
        Vector2[][] a = JamoStrokes(left);
        Vector2[][] b = JamoStrokes(right);
        if (a == null || b == null) return null;

        var result = new Vector2[a.Length + b.Length][];
        for (int i = 0; i < a.Length; i++)
        {
            result[i] = new Vector2[a[i].Length];
            for (int k = 0; k < a[i].Length; k++)
                result[i][k] = new Vector2(a[i][k].x * 0.45f, a[i][k].y);           // 왼쪽 45%
        }
        for (int i = 0; i < b.Length; i++)
        {
            result[a.Length + i] = new Vector2[b[i].Length];
            for (int k = 0; k < b[i].Length; k++)
                result[a.Length + i][k] = new Vector2(0.55f + b[i][k].x * 0.45f, b[i][k].y); // 오른쪽 45%
        }
        return result;
    }

    // ── 유저 잉크를 유닛 정사각형으로 늘리기 ─────────────────────────
    static List<Vector2[]> NormalizeToUnitSquare(List<List<Vector2>> strokes)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var s in strokes)
            foreach (var p in s)
            {
                if (p.x < minX) minX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.x > maxX) maxX = p.x;
                if (p.y > maxY) maxY = p.y;
            }
        float w = Mathf.Max(maxX - minX, 0.001f);
        float h = Mathf.Max(maxY - minY, 0.001f);

        var result = new List<Vector2[]>();
        foreach (var s in strokes)
        {
            var arr = new Vector2[s.Count];
            for (int i = 0; i < s.Count; i++)
                arr[i] = new Vector2((s[i].x - minX) / w, (s[i].y - minY) / h);
            result.Add(arr);
        }
        return result;
    }
}

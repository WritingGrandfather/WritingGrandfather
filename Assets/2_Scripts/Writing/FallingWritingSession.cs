using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// 낙하 모드 세션: 떨어지는 글자들(FallingWordSpawner) 중 유저가 쓴 글자를 찾아 제거한다.
///
/// 흐름: [평가] → 쓴 글씨를 화면의 모든 낙하 글자와 유사도 비교 → 가장 닮은 글자가
///       기준 점수 이상이면 (+획순 검사 통과 시) 그 글자를 격추.
///
/// ※ 여기 연결하는 TemplateSimilarityEvaluator는 Stroke Capture/Cell 참조를 "비운" 것이어야 한다.
///   (낙하 모드는 위치 비교가 아니라 모양 비교 — 본보기 따라쓰기가 아니므로)
/// </summary>
public class FallingWritingSession : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] FallingWordSpawner spawner;
    [SerializeField] StrokeCapture strokeCapture;
    [Tooltip("좌표 기준용 칸 (targetText는 사용 안 함)")]
    [SerializeField] WritingCell cell;
    [Tooltip("모양 채점기 — Stroke Capture/Cell 참조를 비운 별도 인스턴스 권장")]
    [SerializeField] TemplateSimilarityEvaluator evaluator;
    [SerializeField] DrowLine drawLine;

    [Tooltip("판정 직후 맞음/빠뜨림/벗어남을 색으로 보여줄 교정 겹쳐보기 (선택)")]
    [SerializeField] CompareOverlay compareOverlay;

    [Header("판정")]
    [Tooltip("이 점수 이상으로 닮은 낙하 글자가 있으면 격추")]
    [SerializeField] int passScore = 60;

    [Tooltip("격추 전에 획순도 검사 (로컬)")]
    [SerializeField] bool checkStrokeOrder = true;

    [Tooltip("정자 검사 — 흘림체(획 이어 쓰기, 구불거림)를 감점")]
    [SerializeField] bool requireNeatWriting = true;

    [Header("자동 판정")]
    [Tooltip("본보기 글자가 일정 비율 이상 채워지면 버튼 없이 자동으로 판정 (그리는 도중에도)")]
    [SerializeField] bool autoEvaluate = true;

    [Tooltip("자동 판정이 걸리는 채움 비율 (본보기를 이만큼 덮으면 판정. 벗어난 잉크는 무시)")]
    [Range(0.3f, 1f)]
    [SerializeField] float autoFillThreshold = 0.95f;

    [Tooltip("채움 비율 검사 주기(초) — 너무 짧으면 연산 낭비")]
    [SerializeField] float autoCheckInterval = 0.15f;

    [Tooltip("조건 충족 후 펜을 뗀 뒤 판정까지 대기 시간(초) — 쓰는 도중 성급한 판정 방지")]
    [SerializeField] float autoEvaluateDelay = 0.4f;

    [Tooltip("획수를 초과한 경우: 펜을 뗀 뒤 이 시간(초)이 지나면 바로 판정하고 무조건 불통과")]
    [SerializeField] float overStrokeDelay = 0.5f;

    [Tooltip("예외 처리: 두 조건 다 못 채운 채 이 시간(초) 동안 아무것도 안 쓰면 그냥 판정 (대부분 불통과 → 다시 쓰기)")]
    [SerializeField] float idleEvaluateTimeout = 2.5f;

    [Header("UI가 구독할 이벤트")]
    public FeedbackEvent onResult;
    public StatusEvent onStatus;

    [Tooltip("글자를 격추했을 때")]
    public UnityEvent onWordCleared;

    bool isEvaluating;

    void OnEnable()
    {
        if (spawner != null) spawner.OnWordReachedBottom += HandleWordDropped;
    }

    void OnDisable()
    {
        if (spawner != null) spawner.OnWordReachedBottom -= HandleWordDropped;
    }

    // 글자가 바닥에 떨어져 사라지면 쓰고 있던 글씨도 함께 지운다
    void HandleWordDropped(FallingWordSpawner.FallingWord word)
    {
        drawLine?.ClearAll();
        drawLine?.CancelCurrentStroke();
    }

    /// <summary>현재 타겟 = 가장 먼저 소환된 외자 글자 (선입선출)</summary>
    FallingWordSpawner.FallingWord CurrentTarget()
    {
        if (spawner == null) return null;
        foreach (var word in spawner.ActiveWords)
            if (!string.IsNullOrEmpty(word.Text) && word.Text.Length == 1)
                return word;
        return null;
    }

    float autoCheckTimer;
    float strokeStableTime;
    float idleTime;
    int lastStrokeCount;

    void Update()
    {
        // 현재 타겟을 칸에 기록 → TraceGuide(본보기)가 자동으로 그 글자를 띄운다
        if (cell == null) return;
        var target = CurrentTarget();
        cell.targetText = target != null ? target.Text : "";

        if (autoEvaluate) AutoEvaluateCheck(target);
    }

    // ── 자동 판정: 본보기가 일정 비율 이상 채워지면 즉시 Evaluate() ──────
    //    그리는 도중에도 검사한다. 판정되면 그리던 획을 끊고, 손가락을 뗄 때까지 펜 차단.
    void AutoEvaluateCheck(FallingWordSpawner.FallingWord target)
    {
        if (isEvaluating || target == null || strokeCapture == null || evaluator == null) return;

        autoCheckTimer += Time.deltaTime;
        if (autoCheckTimer < autoCheckInterval) return;
        autoCheckTimer = 0f;

        var norm = strokeCapture.GetNormalizedStrokes(cell);
        if (norm.Count == 0)
        {
            strokeStableTime = 0f;
            idleTime = 0f;
            lastStrokeCount = 0;
            return;
        }

        // 획이 늘거나 지워지면 방치 타이머 리셋 (아직 쓰는 중)
        if (norm.Count != lastStrokeCount)
        {
            lastStrokeCount = norm.Count;
            idleTime = 0f;
        }

        // 조건: 1차 채움 비율 (벗어난 잉크 무시) 또는 2차 획수 (표준 획수 완료)
        float coverage = evaluator.CoverageRatio(norm, target.Text[0]);
        int expected = ExpectedStrokes(target.Text[0]);
        bool overStroke = expected > 0 && norm.Count > expected; // 획수 초과 → 빠르게 판정(불통과)
        bool conditionMet = overStroke
                         || coverage >= autoFillThreshold
                         || (expected > 0 && norm.Count >= expected);

        bool drawing = Pointer.current != null && Pointer.current.press.isPressed;
        if (drawing)
        {
            strokeStableTime = 0f;
            idleTime = 0f;
            return;
        }

        if (conditionMet)
        {
            // 펜을 뗀 상태로 대기 시간이 지나야 판정 (쓰는 도중 성급한 판정 방지)
            idleTime = 0f;
            strokeStableTime += autoCheckInterval;
            if (strokeStableTime < (overStroke ? overStrokeDelay : autoEvaluateDelay)) return;
        }
        else
        {
            // 예외 처리: 두 조건 다 못 채운 채 한참 방치하면 그냥 판정 (불통과 피드백 → 다시 쓰기)
            strokeStableTime = 0f;
            idleTime += autoCheckInterval;
            if (idleTime < idleEvaluateTimeout) return;
        }

        strokeStableTime = 0f;
        idleTime = 0f;
        lastStrokeCount = 0;

        // 판정! (Evaluate가 그리는 중인 획까지 포함해 캡처한 뒤 획을 정리한다)
        Evaluate();

        // 아직 손가락/펜을 누르고 있으면 뗄 때까지 새 획이 안 나오게 차단
        drawLine?.CancelCurrentStroke();
    }

    /// <summary>완성형 한글의 표준 획수 (자모 분해 후 합산). 모르면 0.</summary>
    static int ExpectedStrokes(char ch)
    {
        if (!HangulComposer.Decompose(ch, out char cho, out char jung, out char jong)) return 0;
        return HangulComposer.JamoStrokeCount(cho) + HangulComposer.JamoStrokeCount(jung)
             + (jong != '\0' ? HangulComposer.JamoStrokeCount(jong) : 0);
    }

    /// <summary>UI의 [평가] 버튼에서 호출</summary>
    public void Evaluate()
    {
        if (isEvaluating) return;
        if (spawner == null || strokeCapture == null || cell == null || evaluator == null)
        {
            Debug.LogError("[FallingSession] 참조가 비어 있습니다. 인스펙터를 확인하세요.");
            return;
        }

        isEvaluating = true;
        onStatus?.Invoke("확인 중...");

        var norm = strokeCapture.GetNormalizedStrokes(cell);
        if (norm.Count == 0)
        {
            Finish(new HandwritingFeedback { recognizedText = "", score = 0, passed = false, message = "글씨를 먼저 써볼까요?" });
            return;
        }

        byte[] png = StrokeRasterizer.ToPng(norm);
        string strokesJson = strokeCapture.CaptureJson(cell);

        // 타겟 = 가장 먼저 소환된 글자 (선입선출 — 바닥에 제일 가까운 글자부터 처리)
        FallingWordSpawner.FallingWord target = CurrentTarget();

        if (target == null)
        {
            drawLine?.ClearAll();
            Finish(new HandwritingFeedback { recognizedText = "", score = 0, passed = false, message = "떨어지는 글자가 없어요." });
            return;
        }

        // 획수 초과 = 무조건 불통과 (모양이 아무리 닮았어도)
        int expectedStrokes = ExpectedStrokes(target.Text[0]);
        if (expectedStrokes > 0 && norm.Count > expectedStrokes)
        {
            drawLine?.ClearAll();
            Finish(new HandwritingFeedback
            {
                recognizedText = "",
                score = 0,
                passed = false,
                message = $"획이 너무 많아요 ({norm.Count}획 — '{target.Text}'는 {expectedStrokes}획). 또박또박 다시 써볼까요?",
            });
            return;
        }

        // 타겟 글자와 모양 비교
        var request = new HandwritingEvaluationRequest
        {
            imagePng = png,
            strokesJson = strokesJson,
            targetText = target.Text,
        };
        HandwritingFeedback fb = null;
        evaluator.Evaluate(request, r => fb = r); // TemplateSimilarityEvaluator는 즉시 콜백
        if (fb == null)
        {
            Finish(HandwritingFeedback.Error("채점 결과를 받지 못했습니다."));
            return;
        }

        // 교정 겹쳐보기 — 통과/불통과와 무관하게 어디가 맞고 틀렸는지 색으로 표시
        if (compareOverlay != null)
            compareOverlay.Show(evaluator.BuildCompareTexture());

        // 정자 검사 — 흘림체(획 이어 쓰기, 구불거림) 감점
        if (requireNeatWriting)
        {
            fb.score = TemplateSimilarityEvaluator.ApplyNeatnessChecks(norm, target.Text[0], fb.score, out string neatWarn);
            if (!string.IsNullOrEmpty(neatWarn)) fb.message = neatWarn;
        }

        if (fb.score < passScore)
        {
            drawLine?.ClearAll();
            Finish(new HandwritingFeedback
            {
                recognizedText = "",
                score = fb.score,
                passed = false,
                message = string.IsNullOrEmpty(fb.message) || fb.message.Contains("닮았")
                    ? $"'{target.Text}'와 달라 보여요. 또박또박 다시 써볼까요?"
                    : fb.message,
            });
            return;
        }

        // 획순 검사 (로컬, 지원 자모만)
        if (checkStrokeOrder)
        {
            var order = StrokeOrderValidator.Validate(target.Text[0], norm);
            if (order.supported && !order.ok)
            {
                drawLine?.ClearAll();
                Finish(new HandwritingFeedback
                {
                    recognizedText = target.Text,
                    score = Mathf.Min(fb.score, 60),
                    passed = false,
                    message = order.message,
                });
                return;
            }
        }

        // 격추!
        spawner.TryClearWord(target.Text);
        drawLine?.ClearAll();
        onWordCleared?.Invoke();
        Finish(new HandwritingFeedback
        {
            recognizedText = target.Text,
            score = fb.score,
            passed = true,
            message = $"'{target.Text}' 명중! ({fb.score}점)",
        });
    }

    void Finish(HandwritingFeedback fb)
    {
        isEvaluating = false;
        onStatus?.Invoke("");
        onResult?.Invoke(fb);
        Debug.Log($"[FallingSession] 인식='{fb.recognizedText}' 점수={fb.score} 통과={fb.passed}");
    }
}

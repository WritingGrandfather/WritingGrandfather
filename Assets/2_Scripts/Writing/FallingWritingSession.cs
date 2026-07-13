using UnityEngine;
using UnityEngine.Events;

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

    [Header("판정")]
    [Tooltip("이 점수 이상으로 닮은 낙하 글자가 있으면 격추")]
    [SerializeField] int passScore = 60;

    [Tooltip("격추 전에 획순도 검사 (로컬)")]
    [SerializeField] bool checkStrokeOrder = true;

    [Tooltip("정자 검사 — 흘림체(획 이어 쓰기, 구불거림)를 감점")]
    [SerializeField] bool requireNeatWriting = true;

    [Header("UI가 구독할 이벤트")]
    public FeedbackEvent onResult;
    public StatusEvent onStatus;

    [Tooltip("글자를 격추했을 때")]
    public UnityEvent onWordCleared;

    bool isEvaluating;

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
        FallingWordSpawner.FallingWord target = null;
        foreach (var word in spawner.ActiveWords)
        {
            if (!string.IsNullOrEmpty(word.Text) && word.Text.Length == 1) // 외자만 지원
            {
                target = word;
                break;
            }
        }

        if (target == null)
        {
            drawLine?.ClearAll();
            Finish(new HandwritingFeedback { recognizedText = "", score = 0, passed = false, message = "떨어지는 글자가 없어요." });
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

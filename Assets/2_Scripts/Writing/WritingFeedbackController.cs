using System;
using UnityEngine;
using UnityEngine.Events;

// UnityEvent<T> 제네릭은 인스펙터에 노출되지 않으므로 구체 서브클래스로 선언한다.
[Serializable] public class FeedbackEvent : UnityEvent<HandwritingFeedback> { }
[Serializable] public class StatusEvent : UnityEvent<string> { }

/// <summary>
/// 손글씨 피드백 전체 흐름을 조율하는 컨트롤러.
///
/// 흐름: [평가 버튼] → 칸 캡처(CellCapture) → 평가 요청(HandwritingEvaluator)
///       → 결과(HandwritingFeedback)를 UnityEvent로 UI에 전달
///
/// 씬 설정:
///  1) 이 컴포넌트를 빈 오브젝트에 추가
///  2) targetCell   : 평가할 WritingCell 지정
///  3) capture       : CellCapture 컴포넌트 지정
///  4) evaluator     : HandwritingEvaluator (지금은 MockHandwritingEvaluator) 지정
///  5) 버튼 OnClick → WritingFeedbackController.RequestFeedback
///  6) onFeedback / onStatus 이벤트를 TMP 텍스트 등에 연결
/// </summary>
public class WritingFeedbackController : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] WritingCell targetCell;
    [SerializeField] StrokeCapture strokeCapture; // 획 좌표(획순/획수)
    [SerializeField] CellCapture imageCapture;     // PNG(글자 인식/모양)
    [SerializeField] HandwritingEvaluator evaluator;

    [Header("통과 기준")]
    [Tooltip("인식 글자가 목표와 일치하고 이 점수 이상이면 passed=true")]
    [SerializeField] int passScore = 70;

    [Header("기획 판정 기준 (AI 프롬프트로 전달됨)")]
    [Tooltip("기획서에 정의된 채점/피드백 조건을 여기에 적는다. AI가 이 기준으로 판단.")]
    [TextArea(4, 12)]
    [SerializeField]
    string criteria = "";   // ← 기획 판정 기준을 나중에 여기 채우기 (비워둠)

    [Header("이벤트")]
    [Tooltip("평가 결과가 나오면 호출 (피드백 메시지 등 표시용)")]
    public FeedbackEvent onFeedback;

    [Tooltip("진행 상태 메시지 (예: '평가 중...') 표시용")]
    public StatusEvent onStatus;

    bool isEvaluating;

    [Header("디버그")]
    [Tooltip("켜면 AI에 전송되는 이미지/획 데이터를 프로젝트 루트의 DebugCapture 폴더에 저장")]
    [SerializeField] bool debugDump = true;

    /// <summary>후보 글자 목록 (선택). WritingSessionController가 현재 스테이지 글자들로 채워준다.</summary>
    [HideInInspector] public string[] candidates;

    [Header("획순 검사 (선택 — API 사용)")]
    [Tooltip("연결하면 모양 통과 후 AI가 획순·획 방향을 추가 검증한다")]
    [SerializeField] StrokeOrderChecker strokeOrderChecker;

    [Tooltip("획순이 틀리면 불통과 처리 (끄면 피드백 문장만 보여줌)")]
    [SerializeField] bool failOnWrongOrder = true;

    string lastStrokesJson; // 획순 검사에 전달할 최근 획 데이터

    /// <summary>평가 버튼에서 호출. 칸을 캡처해 AI 평가를 요청한다.</summary>
    public void RequestFeedback()
    {
        if (isEvaluating)
        {
            Debug.Log("[WritingFeedback] 이미 평가 중입니다.");
            return;
        }

        if (targetCell == null || strokeCapture == null || evaluator == null)
        {
            Debug.LogError("[WritingFeedback] targetCell / strokeCapture / evaluator 참조가 비어 있습니다. 인스펙터를 확인하세요.");
            return;
        }

        isEvaluating = true;
        onStatus?.Invoke("Evaluating...");

        // 1) 하이브리드 캡처 — 획 좌표(획순) + 획 데이터를 직접 그린 PNG(모양/인식)
        //    카메라 캡처 대신 래스터라이저 사용: 배경 없이 순수 획만, 글자 영역만 크게 렌더링
        var normStrokes = strokeCapture.GetNormalizedStrokes(targetCell);
        string strokes = strokeCapture.CaptureJson(targetCell);
        byte[] png = StrokeRasterizer.ToPng(normStrokes);
        lastStrokesJson = strokes;

        // 디버그: AI에 실제로 전송되는 데이터를 프로젝트 루트/DebugCapture에 저장
        if (debugDump)
        {
            try
            {
                string dir = System.IO.Path.Combine(Application.dataPath, "../DebugCapture");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "last_capture.png"), png);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "last_strokes.json"), strokes);
                Debug.Log($"[WritingFeedback] 전송 데이터 저장됨: DebugCapture/ (획 {normStrokes.Count}개)");
            }
            catch (System.Exception e) { Debug.LogWarning("[WritingFeedback] 디버그 저장 실패: " + e.Message); }
        }

        // 2) 요청 구성 (후보군은 오답 끼워맞춤을 유발해서 사용하지 않음 — AI는 순수 인식만)
        var request = new HandwritingEvaluationRequest
        {
            strokesJson = strokes,
            imagePng = png,
            targetText = targetCell.targetText,
            criteria = criteria
        };

        // 3) 평가 요청 → 콜백으로 결과 수신
        evaluator.Evaluate(request, OnEvaluated);
    }

    void OnEvaluated(HandwritingFeedback feedback)
    {
        if (feedback == null)
            feedback = HandwritingFeedback.Error("No evaluation result received.");

        // 글자 유사도 = AI가 판단한 원점수. 아래에서 통과/불통과 판정에 따라 feedback.score가
        // 재조정(클램프)되므로, "닮은 정도" 표시용으로 원래 값을 먼저 따로 보관해둔다.
        feedback.similarityScore = feedback.score;

        // 글자 크기·위치 정확도 — AI/획순과 독립적으로 칸 대비 잉크 좌표만으로 계산
        if (strokeCapture != null && targetCell != null)
        {
            var cellStrokes = strokeCapture.GetCellNormalizedStrokes(targetCell);
            feedback.positionScore = PositionAccuracyScorer.Score(cellStrokes);
        }

        // ★ 목표 비교는 AI가 아니라 여기서 한다.
        //   (AI에게 목표를 알려주면 선입견으로 오답도 목표로 인식하므로, AI는 순수 인식만 수행)
        if (targetCell != null && !string.IsNullOrEmpty(targetCell.targetText))
        {
            string target = targetCell.targetText.Trim();
            string recognized = (feedback.recognizedText ?? "").Trim();

            if (recognized == target)
            {
                // 글자가 맞으면 웬만하면 통과 — 점수는 가독성 참고치일 뿐 (상한 55로 클램프)
                feedback.passed = feedback.score >= Mathf.Min(passScore, 55);
            }
            else
            {
                // 다른 글자로 인식됨 → 무조건 불통과
                if (feedback.score > 20) feedback.score = 20;
                feedback.passed = false;
                feedback.message = string.IsNullOrEmpty(recognized)
                    ? string.Format(LocalizationManager.Get("writing_feedback.unrecognized"), target)
                    : string.Format(LocalizationManager.Get("writing_feedback.wrong_character"), target, recognized);
            }
        }

        Debug.Log($"[WritingFeedback] 목표='{targetCell?.targetText}' 인식='{feedback.recognizedText}' 점수={feedback.score} 통과={feedback.passed}");

        // 모양 통과 시 획순 검사 — 1순위: 로컬 필순 대조 (결정적·즉시), 2순위: AI (미지원 자모만)
        string target2 = (targetCell?.targetText ?? "").Trim();
        if (feedback.passed && target2.Length == 1)
        {
            var local = StrokeOrderValidator.Validate(target2[0], strokeCapture.GetNormalizedStrokes(targetCell));
            if (local.supported)
            {
                feedback.strokeOrderScore = local.score;
                Debug.Log($"[StrokeOrder/로컬] {(local.ok ? "정상" : "오류")} {local.message} (점수 {local.score})");
                if (!local.ok && failOnWrongOrder)
                {
                    feedback.passed = false;
                    if (feedback.score > 60) feedback.score = 60;
                    feedback.message = local.message;
                }
                Finish(feedback);
                return;
            }

            // 로컬에서 판정 보류(미지원 자모 등) → AI가 있으면 AI로
            if (strokeOrderChecker != null && !string.IsNullOrEmpty(lastStrokesJson))
            {
                onStatus?.Invoke("획순 확인 중...");
                HandwritingFeedback fb = feedback;
                strokeOrderChecker.Check(target2, lastStrokesJson, (orderOk, orderMsg) =>
                {
                    // AI 획순 검사는 참/거짓만 주므로 점수는 근사치로 환산
                    fb.strokeOrderScore = orderOk ? 90 : 55;
                    if (!orderOk && failOnWrongOrder)
                    {
                        fb.passed = false;
                        if (fb.score > 60) fb.score = 60;
                    }
                    if (!string.IsNullOrEmpty(orderMsg))
                        fb.message = orderMsg;

                    Finish(fb);
                });
                return;
            }
        }

        Finish(feedback);
    }

    void Finish(HandwritingFeedback feedback)
    {
        isEvaluating = false;
        onStatus?.Invoke("");
        onFeedback?.Invoke(feedback);
    }
}

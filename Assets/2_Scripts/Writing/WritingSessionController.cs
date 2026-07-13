using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 출제(EnemySpawner) ↔ 필기 인식(WritingFeedbackController)을 잇는 "로직 연결자".
///
/// ★ UI에 의존하지 않는다 ★
/// UI 담당자는 이 컴포넌트만 알면 된다:
///   - 호출:  Evaluate()          → 현재 출제 글자를 목표로 잡고 필기 평가 시작
///   - 구독:  onResult(피드백)     → 결과를 UI에 표시 (UI Toolkit이든 uGUI든 자유)
///   - 구독:  onAdvanced()         → 통과해서 다음 글자로 넘어감
///
/// 즉 UI 쪽은 "버튼 → Evaluate() 연결" + "onResult 구독해서 표시"만 하면 된다.
/// 그리기/캡처/AI 호출/출제 진행은 전부 이 아래 로직이 처리한다.
/// </summary>
public class WritingSessionController : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("목표 글자를 출제하는 EnemySpawner")]
    [SerializeField] EnemySpawner spawner;

    [Tooltip("목표 글자를 기록할 WritingCell")]
    [SerializeField] WritingCell cell;

    [Tooltip("캡처+AI평가 컨트롤러")]
    [SerializeField] WritingFeedbackController feedback;

    [Tooltip("통과/다음으로 넘어갈 때 획을 지울 DrowLine (선택)")]
    [SerializeField] DrowLine drawLine;

    [Header("통과 기준")]
    [Tooltip("이 점수 이상이면 통과로 보고 다음 글자로 넘어간다")]
    [SerializeField] int passScore = 70;

    [Header("UI가 구독할 이벤트")]
    [Tooltip("평가 결과 — UI 표시용 (인식 글자/점수/피드백)")]
    public FeedbackEvent onResult;

    [Tooltip("진행 상태 메시지 (예: 'Evaluating...')")]
    public StatusEvent onStatus;

    [Tooltip("통과해서 다음 글자로 넘어갔을 때")]
    public UnityEvent onAdvanced;

    void Awake()
    {
        if (feedback != null)
        {
            if (feedback.onFeedback == null) feedback.onFeedback = new FeedbackEvent();
            if (feedback.onStatus == null) feedback.onStatus = new StatusEvent();
            feedback.onFeedback.AddListener(HandleResult);
            feedback.onStatus.AddListener(s => onStatus?.Invoke(s));
        }
    }

    /// <summary>UI의 [평가] 버튼 등에서 호출. 현재 출제 글자를 목표로 잡고 평가한다.</summary>
    public void Evaluate()
    {
        if (feedback == null)
        {
            Debug.LogError("[WritingSession] feedback 참조가 비어 있습니다.");
            return;
        }

        // 현재 출제 중인 글자를 이번 평가의 목표로 설정
        if (spawner != null && cell != null)
            cell.targetText = spawner.CurrentText;

        feedback.RequestFeedback();
    }

    void HandleResult(HandwritingFeedback fb)
    {
        onResult?.Invoke(fb);

        // 통과 판정: AI가 passed=true 이거나, 점수가 기준 이상이면 통과
        bool pass = fb != null && (fb.passed || fb.score >= passScore);
        if (!pass) return;

        // 다음 글자로: 화면 정리 + 출제기 진행
        drawLine?.ClearAll();
        if (spawner != null) spawner.Advance();
        onAdvanced?.Invoke();
    }
}

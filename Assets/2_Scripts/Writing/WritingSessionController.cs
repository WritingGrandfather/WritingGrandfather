using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 출제(EnemySpawner) ↔ 필기 채점(WritingFeedbackController)을 잇는 "로직 연결자".
///
/// 진행 방식:
///  - 낱말 모드: 출제된 외자 한 글자를 쓰고 통과하면 다음 출제로
///  - 단어 모드: 단어를 한 글자씩 차례로 쓴다 ("나무" → '나' 통과 → '무' 통과 → 다음 단어)
///
/// 현재 써야 할 글자는 WritingCell.targetText에 항상 유지되므로
/// TraceGuide(본보기)와 채점기는 자동으로 그 글자를 따라간다.
///
/// UI 담당자는 이 컴포넌트만 알면 된다:
///   - 호출:  Evaluate()      → 현재 글자 채점
///   - 구독:  onResult        → 결과 표시 (점수/피드백)
///   - 구독:  onCharAdvanced  → 단어 안에서 다음 글자로 넘어감
///   - 구독:  onWordAdvanced  → 단어/낱말 전체 완료, 다음 출제로 넘어감
/// </summary>
public class WritingSessionController : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("낱말/단어를 출제하는 EnemySpawner (스테이지 진행 포함)")]
    [SerializeField] EnemySpawner spawner;

    [Tooltip("목표 글자를 기록할 WritingCell")]
    [SerializeField] WritingCell cell;

    [Tooltip("캡처+채점 컨트롤러")]
    [SerializeField] WritingFeedbackController feedback;

    [Tooltip("통과/다음으로 넘어갈 때 획을 지울 DrowLine (선택)")]
    [SerializeField] DrowLine drawLine;

    [Header("통과 기준")]
    [Tooltip("이 점수 이상이면 통과로 보고 다음 글자로 넘어간다")]
    [SerializeField] int passScore = 70;

    [Tooltip("불통과일 때도 쓴 획을 모두 지운다 (깨끗한 상태로 다시 쓰기)")]
    [SerializeField] bool clearOnFail = true;

    [Header("UI가 구독할 이벤트")]
    [Tooltip("평가 결과 — UI 표시용 (점수/피드백)")]
    public FeedbackEvent onResult;

    [Tooltip("진행 상태 메시지 (예: 'Evaluating...')")]
    public StatusEvent onStatus;

    [Tooltip("단어 안에서 다음 글자로 넘어갔을 때 (단어 모드)")]
    public UnityEvent onCharAdvanced;

    [Tooltip("단어/낱말 전체를 완료하고 다음 출제로 넘어갔을 때")]
    public UnityEvent onWordAdvanced;

    string currentWord = "";
    int charIndex;

    /// <summary>지금 써야 할 글자 (예: "나무"의 두 번째면 '무')</summary>
    public string CurrentChar =>
        charIndex < currentWord.Length ? currentWord[charIndex].ToString() : "";

    /// <summary>단어 내 진행 상황 (UI 표시용, 예: 2/2)</summary>
    public int CharIndex => charIndex;
    public string CurrentWord => currentWord;

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

    void Update()
    {
        if (spawner == null || cell == null) return;

        // 출제가 바뀌면 (다음 단어/스테이지 전환 포함) 첫 글자부터 다시
        string word = spawner.CurrentText ?? "";
        if (word != currentWord)
        {
            currentWord = word;
            charIndex = 0;
            ClearDrawing();
        }

        // 본보기(TraceGuide)와 채점기가 참조하는 목표 글자를 항상 최신으로
        cell.targetText = CurrentChar;
    }

    void ClearDrawing()
    {
        drawLine?.ClearAll();
        UndoManager.Instance?.Clear();
    }

    /// <summary>UI의 [평가] 버튼 등에서 호출. 현재 글자를 채점한다.</summary>
    public void Evaluate()
    {
        if (feedback == null)
        {
            Debug.LogError("[WritingSession] feedback 참조가 비어 있습니다.");
            return;
        }
        if (string.IsNullOrEmpty(CurrentChar))
        {
            Debug.LogWarning("[WritingSession] 출제된 글자가 없습니다.");
            return;
        }

        feedback.RequestFeedback();
    }

    void HandleResult(HandwritingFeedback fb)
    {
        onResult?.Invoke(fb);

        bool pass = fb != null && (fb.passed || fb.score >= passScore);
        if (!pass)
        {
            if (clearOnFail) ClearDrawing();
            return;
        }

        ClearDrawing();
        charIndex++;

        if (charIndex >= currentWord.Length)
        {
            // 단어/낱말 완료 → 다음 출제로 (currentWord 갱신은 Update에서 감지)
            if (spawner != null) spawner.Advance();
            onWordAdvanced?.Invoke();
        }
        else
        {
            // 단어의 다음 글자로 (cell.targetText는 Update에서 갱신 → 본보기 자동 교체)
            onCharAdvanced?.Invoke();
        }
    }
}

using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

// UnityEvent<int> 제네릭은 인스펙터에 노출되지 않으므로 구체 서브클래스로 선언한다.
[System.Serializable] public class ScoreEvent : UnityEvent<int> { }
// 결과창용 - (이번 판 점수, 최고 점수)를 함께 전달한다.
[System.Serializable] public class ScoreResultEvent : UnityEvent<int, int> { }

/// <summary>
/// 도전 모드의 점수를 누적하고, 게임이 끝나면 랭킹(Firestore)에 업로드하는 연결자.
///
/// 흐름:
///  - 단어(낱말)를 하나 완성할 때마다 (WritingSessionController.onWordAdvanced) 고정 점수를 더한다.
///  - 게임이 끝나면 (모든 스테이지 클리어 or 외부에서 SubmitScore 호출) 최종 점수를
///    "로그인된 계정 id"로 RankingManager에 올리고, 로컬/클라우드 최고점수도 갱신한다.
///
/// 씬 설정:
///  1) 이 컴포넌트를 도전 씬의 빈 오브젝트에 추가
///  2) session / spawner / rankingManager 참조 연결
///  3) (선택) onScoreChanged → HUD 점수 텍스트, onGameFinished → 결과 화면에 연결
///
/// ※ 목숨/제한시간에 의한 게임오버는 아직 미구현. 그 로직이 생기면 게임오버 시점에
///   SubmitScore()만 호출하면 된다 (중복 업로드는 내부에서 방지).
/// </summary>
public class ChallengeScoreTracker : MonoBehaviour
{
    // 도전 씬 이름 — 이 씬이 로드되면 트래커를 자동 생성한다.
    const string ChallengeSceneName = "ChallengeScene";

    // 게임 시작 시 1회 등록. 도전 씬이 열릴 때마다 트래커 오브젝트를 자동으로 만들어 준다.
    // (씬에 수동으로 오브젝트를 넣지 않아도 동작하도록)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void AutoBootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded; // 중복 등록 방지
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != ChallengeSceneName) return;
        if (FindObjectOfType<ChallengeScoreTracker>() != null) return; // 이미 있으면 그대로 사용
        var go = new GameObject("ChallengeScoreTracker (auto)");
        go.AddComponent<ChallengeScoreTracker>();
    }

    [Header("참조")]
    [Tooltip("단어 완성(onWordAdvanced)을 받아 점수를 누적할 세션 컨트롤러 - 구버전(넘기기 방식) 씬용")]
    [SerializeField] WritingSessionController session;

    [Tooltip("모든 스테이지 클리어(OnAllStagesCleared)를 게임 종료로 감지할 출제기 - 구버전(넘기기 방식) 씬용")]
    [SerializeField] EnemySpawner spawner;

    [Tooltip("글자를 맞혀서 없앨 때마다(OnWordCleared) 점수를 더할 낙하식 출제기 - 실제 도전 씬(ChallengeScene)이 쓰는 시스템")]
    [SerializeField] FallingWordSpawner fallingSpawner;

    [Tooltip("체력이 0이 되거나 시간이 다 됐을 때(OnDied) 게임 종료로 감지할 플레이어 체력 - 비워두면 PlayerHp.Instance 사용")]
    [SerializeField] PlayerHp playerHp;

    [Tooltip("점수를 업로드할 랭킹 매니저")]
    [SerializeField] RankingManager rankingManager;

    [Header("점수")]
    [Tooltip("단어(낱말)를 하나 완성할 때마다 더해줄 점수 (n)")]
    [SerializeField] int pointsPerWord = 100;

    [Header("저장")]
    [Tooltip("게임 종료 시 랭킹에 업로드")]
    [SerializeField] bool uploadToRanking = true;

    [Tooltip("게임 종료 시 세이브의 최고점수를 갱신")]
    [SerializeField] bool saveBestScore = true;

    [Header("이벤트 (UI 연결용)")]
    [Tooltip("점수가 바뀔 때마다 현재 점수를 전달 (HUD 갱신용)")]
    public ScoreEvent onScoreChanged;

    [Tooltip("게임이 끝났을 때 최종 점수를 전달 (결과 화면 표시용)")]
    public ScoreEvent onGameFinished;

    [Tooltip("최고 점수 저장까지 끝난 뒤(이번 점수, 최고 점수) - 결과 화면이 이걸 구독해서 뜬다")]
    public ScoreResultEvent onResultReady;

    int score;
    int wordsCleared;
    bool submitted;

    /// <summary>현재까지 누적된 점수</summary>
    public int CurrentScore => score;

    /// <summary>이번 게임에서 완성한 단어 수</summary>
    public int WordsCleared => wordsCleared;

    PlayerHp Hp => playerHp != null ? playerHp : PlayerHp.Instance;

    void OnEnable()
    {
        // 인스펙터에서 연결 안 했으면 씬에서 자동으로 찾아 붙인다.
        if (session == null) session = FindObjectOfType<WritingSessionController>();
        if (spawner == null) spawner = FindObjectOfType<EnemySpawner>();
        if (fallingSpawner == null) fallingSpawner = FindObjectOfType<FallingWordSpawner>();
        if (rankingManager == null) rankingManager = FindObjectOfType<RankingManager>();

        if (session != null)
        {
            if (session.onWordAdvanced == null) session.onWordAdvanced = new UnityEvent();
            session.onWordAdvanced.AddListener(OnWordCleared);
        }
        if (spawner != null)
            spawner.OnAllStagesCleared += OnAllStagesCleared;
        if (fallingSpawner != null)
            fallingSpawner.OnWordCleared += OnFallingWordCleared;

        // 목숨이 0이 되거나(놓친 글자 누적) 시간이 다 되면(ChallengeSurvivalController가
        // PlayerHp.HP를 0으로 만듦) PlayerHp.OnDied가 한 번 울린다 - 그 시점에 점수를 제출한다.
        if (Hp != null)
            Hp.OnDied += SubmitScore;

        ResetGame();
    }

    void OnDisable()
    {
        if (session != null && session.onWordAdvanced != null)
            session.onWordAdvanced.RemoveListener(OnWordCleared);
        if (spawner != null)
            spawner.OnAllStagesCleared -= OnAllStagesCleared;
        if (fallingSpawner != null)
            fallingSpawner.OnWordCleared -= OnFallingWordCleared;
        if (Hp != null)
            Hp.OnDied -= SubmitScore;
    }

    // 낙하식(ChallengeScene 실제 사용 시스템)에서 글자를 맞혀서 없앴을 때
    void OnFallingWordCleared(FallingWordSpawner.FallingWord word) => OnWordCleared();

    /// <summary>새 게임 시작 — 점수/상태 초기화. (다시하기 버튼 등에서 호출)</summary>
    public void ResetGame()
    {
        score = 0;
        wordsCleared = 0;
        submitted = false;
        onScoreChanged?.Invoke(score);
    }

    // 단어를 하나 완성할 때마다 호출 — 고정 점수를 더한다.
    void OnWordCleared()
    {
        score += pointsPerWord;
        wordsCleared++;
        onScoreChanged?.Invoke(score);
    }

    void OnAllStagesCleared() => SubmitScore();

    /// <summary>
    /// 게임 종료 처리 — 최종 점수를 랭킹에 올리고 최고점수를 저장한다.
    /// 목숨 소진/시간 초과 등 게임오버 로직에서 이 메서드를 호출하면 된다.
    /// 한 게임에서 여러 번 불려도 실제 제출은 한 번만 일어난다.
    /// </summary>
    public void SubmitScore()
    {
        if (submitted) return;
        submitted = true;

        Debug.Log($"[ChallengeScore] 게임 종료 — 최종 점수 {score} (단어 {wordsCleared}개)");
        onGameFinished?.Invoke(score);

        if (uploadToRanking) UploadToRanking();

        // 최고점수 저장 여부와 무관하게, 결과창에 보여줄 최고점수는 항상 한 번 읽어 온다
        // (저장을 껐다면 이번 판 점수로 최고점수가 갱신되지 않은 채로만 보여준다).
        if (SaveManager.Instance == null)
        {
            onResultReady?.Invoke(score, score);
            return;
        }

        SaveManager.Instance.Load(data =>
        {
            if (data == null) data = new PlayerData();
            bool isNewBest = score > data.bestScore;
            if (isNewBest && saveBestScore)
            {
                data.bestScore = score;
                SaveManager.Instance.Save(data);
            }
            onResultReady?.Invoke(score, Mathf.Max(data.bestScore, score));
        });
    }

    void UploadToRanking()
    {
        if (rankingManager == null)
        {
            Debug.LogWarning("[ChallengeScore] rankingManager가 비어 있어 업로드를 건너뜁니다.");
            return;
        }

        // 랭킹 문서 키 = 로그인된 계정의 고유 id. 비로그인/식별 실패 시 기기 고유값으로 대체.
        string id = AuthManager.Instance != null ? AuthManager.Instance.UserId : "";
        if (string.IsNullOrEmpty(id)) id = SystemInfo.deviceUniqueIdentifier;

        rankingManager.UploadScore(id, score,
            () => Debug.Log($"[ChallengeScore] 랭킹 업로드 완료: {id} = {score}"));
    }
}

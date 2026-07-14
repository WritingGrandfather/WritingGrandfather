using UnityEngine;

/// <summary>
/// 도전 모드(낙하식)의 생존 규칙을 담당한다.
///  - 글자를 맞혀서 없애면(FallingWordSpawner.OnWordCleared) 남은 시간이 늘어난다.
///  - 글자가 바닥까지 떨어지면(FallingWordSpawner.OnWordReachedBottom) 체력이 1 줄어든다.
///  - 쿠키런 스태미나처럼, 남은 시간은 계속 줄어들고 0이 되면 게임 오버(PlayerHp.Die()).
///    단, 줄어드는 속도 자체가 게임을 진행할수록(경과 시간 기준) 점점 빨라진다.
/// </summary>
public class ChallengeSurvivalController : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("글자를 맞히거나 놓치는 이벤트를 구독할 낙하 스포너")]
    [SerializeField] FallingWordSpawner spawner;

    [Tooltip("비워두면 PlayerHp.Instance를 사용")]
    [SerializeField] PlayerHp playerHp;

    [Header("시간 설정")]
    [Tooltip("시작 제한 시간(초)")]
    [SerializeField] float startTime = 120f;

    [Tooltip("글자 하나를 없앨 때마다 늘어나는 시간(초)")]
    [SerializeField] float bonusTimePerKill = 3f;

    [Tooltip("남은 시간이 시작 시간을 넘지 못하게 캡을 걸지 여부")]
    [SerializeField] bool capAtStartTime = true;

    [Header("가속 - 쿠키런처럼 진행할수록 시간이 더 빨리 줄어든다")]
    [Tooltip("이 간격(경과 시간, 초)마다 감소 배속이 한 단계씩 늘어난다")]
    [SerializeField] float speedRampInterval = 10f;

    [Tooltip("한 단계마다 감소 배속에 더해지는 값 (기본 배속 1.0 기준)")]
    [SerializeField] float speedRampAmount = 0.5f;

    [Tooltip("바닥에 닿친 글자 하나당 잃는 체력")]
    [SerializeField] int hpLossOnMiss = 1;

    float remaining;
    float elapsed; // 게임 시작 후 실제로 흐른 시간 - 가속 배속 계산 전용, 보너스로 늘어나지 않음
    bool timeUp;

    public float Remaining => remaining;
    public float StartTime => startTime;

    /// <summary>남은 시간이 바뀔 때마다(현재, 시작값) - HUD가 구독해서 게이지를 갱신한다.</summary>
    public event System.Action<float, float> OnTimeChanged;

    /// <summary>시간이 다 됐을 때 한 번</summary>
    public event System.Action OnTimeUp;

    PlayerHp Hp => playerHp != null ? playerHp : PlayerHp.Instance;

    void Start()
    {
        remaining = startTime;
        OnTimeChanged?.Invoke(remaining, startTime);

        if (spawner != null)
        {
            spawner.OnWordCleared += HandleWordCleared;
            spawner.OnWordReachedBottom += HandleWordMissed;
        }
        else
        {
            Debug.LogWarning("[ChallengeSurvival] spawner 참조가 비어 있습니다 - 시간 보너스/체력 감소가 동작하지 않습니다.");
        }
    }

    void OnDestroy()
    {
        if (spawner != null)
        {
            spawner.OnWordCleared -= HandleWordCleared;
            spawner.OnWordReachedBottom -= HandleWordMissed;
        }
    }

    void Update()
    {
        if (timeUp) return;

        elapsed += Time.deltaTime;

        // 10초마다 0.5배씩 빨라짐: 0~10초=1.0배, 10~20초=1.5배, 20~30초=2.0배 ...
        float speedMul = 1f + Mathf.Floor(elapsed / speedRampInterval) * speedRampAmount;

        remaining -= Time.deltaTime * speedMul;
        OnTimeChanged?.Invoke(Mathf.Max(remaining, 0f), startTime);

        if (remaining <= 0f)
        {
            remaining = 0f;
            timeUp = true;
            OnTimeUp?.Invoke();

            // 시간 초과도 체력 초과와 동일하게 하나의 게임 오버 경로(PlayerHp.Die())로 합친다.
            if (Hp != null) Hp.HP = 0;
            else Time.timeScale = 0f;
        }
    }

    void HandleWordCleared(FallingWordSpawner.FallingWord word)
    {
        if (timeUp) return;

        remaining += bonusTimePerKill;
        if (capAtStartTime) remaining = Mathf.Min(remaining, startTime);
        OnTimeChanged?.Invoke(remaining, startTime);
    }

    void HandleWordMissed(FallingWordSpawner.FallingWord word)
    {
        if (timeUp) return;
        if (Hp != null) Hp.HP -= hpLossOnMiss;
    }
}

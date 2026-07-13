using UnityEngine;

public class PlayerHp : MonoBehaviour
{
    public static PlayerHp Instance;
    private int hp;

    public int HP
    {
        get { return hp; }
        set
        {
            // 위/아래로 범위를 벗어나 설정되는 걸 막는다 (예: 최대 초과, 음수).
            int clamped = Mathf.Clamp(value, 0, maxHp);
            if (clamped == hp) return;
            hp = clamped;

            OnHpChanged?.Invoke(hp, maxHp);

            if (hp <= 0)
            {
                Die();
            }
        }
    }

    public int maxHp = 3;

    // HP가 바뀔 때마다(현재값, 최대값) - HUD 등 UI가 구독해서 표시를 갱신한다.
    public event System.Action<int, int> OnHpChanged;

    // 체력이 0이 되어 죽었을 때 한 번 - 다른 시스템(타이머 등)이 정지 처리를 하도록 구독 가능.
    public event System.Action OnDied;

    public void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
        hp = maxHp;
    }

    void Start()
    {
        // 시작 값도 구독자에게 알려서(Awake 이후 구독한 UI도) 초기 하트 3개가 바로 채워지게 한다.
        OnHpChanged?.Invoke(hp, maxHp);
    }

    public void Die()
    {
        Time.timeScale = 0;
        OnDied?.Invoke();
    }
}

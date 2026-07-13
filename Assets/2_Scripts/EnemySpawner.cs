using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// UIDocument 패널에 일정 간격으로 글자 적(Label)을 스폰한다.
/// 화면에 같은 글자가 동시에 두 개 나오지 않도록 관리한다.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private Font gungseoFont; // 궁서체 폰트 파일(.ttf/.otf)을 그대로 드래그

    [Header("모드")]
    [SerializeField] private GameMode mode = GameMode.Letter;

    [Header("난이도 (시작 → 최대, 완만한 곡선으로 상승)")]
    [SerializeField] private float startSpawnInterval = 2.5f; // 초기 스폰 간격(초)
    [SerializeField] private float minSpawnInterval = 0.9f;   // 최대 난이도일 때 간격
    [SerializeField] private float startFallSpeed = 80f;      // 초기 낙하 속도(px/sec)
    [SerializeField] private float maxFallSpeed = 240f;       // 최대 낙하 속도
    [SerializeField] private float rampDuration = 180f;       // 이 시간(초)쯤에 최대 난이도의 약 95% 도달

    [Header("스폰")]
    [SerializeField] private int fontSize = 72;

    [Header("위치")]
    [SerializeField] private float sideMarginPx = 80f;              // 좌우 여백
    [Range(0f, 1f)][SerializeField] private float deadlineRatio = 0.8f; // 패널 높이 대비 플레이어 라인

    [Header("자모 풀 (여기 있는 것들만 조합에 사용)")]
    [SerializeField] private string choseongPool = "ㄱㄴㄷㄹㅁㅂㅅㅇㅈㅊㅋㅌㅍㅎ";
    [SerializeField] private string jungseongPool = "ㅏㅑㅓㅕㅗㅛㅜㅠㅡㅣㅐㅔ";
    [SerializeField] private string jongseongPool = "ㄱㄴㄹㅁㅂㅅㅇ";
    [Range(0f, 1f)][SerializeField] private float startJongseongChance = 0.2f; // 초기 받침 확률
    [Range(0f, 1f)][SerializeField] private float maxJongseongChance = 0.6f;   // 최대 난이도일 때 받침 확률

    [Header("단어 풀 (단어 모드에서 사용)")]
    [SerializeField] private string[] wordPool =
    {
        "나무", "하늘", "바다", "구름", "바람", "사랑", "노래", "달빛",
        "별빛", "가락", "마음", "소리", "거울", "나비", "고래", "수박",
        "가방", "모자", "신발", "우산", "지도", "약속", "무지개", "도서관",
    };

    private readonly List<Enemy> activeEnemies = new List<Enemy>();
    private readonly List<Enemy> toRemove = new List<Enemy>();
    private float timer;
    private float elapsed; // 게임 시작 후 경과 시간

    public IReadOnlyList<Enemy> ActiveEnemies => activeEnemies;

    /// <summary>현재 모드. 모드 선택 UI에서 SetMode로 변경한다.</summary>
    public GameMode Mode => mode;

    /// <summary>모드 변경 (화면의 적을 비우고 난이도를 처음부터 다시 시작)</summary>
    public void SetMode(GameMode newMode)
    {
        mode = newMode;
        foreach (Enemy enemy in activeEnemies)
            enemy.Remove();
        activeEnemies.Clear();
        toRemove.Clear();
        elapsed = 0f;
        timer = 0f;
    }

    /// <summary>난이도 진행도 0~1. 지수 곡선이라 초반엔 천천히, 갈수록 최대치에 수렴.</summary>
    private float Progress => 1f - Mathf.Exp(-3f * elapsed / rampDuration);

    private float CurrentFallSpeed => Mathf.Lerp(startFallSpeed, maxFallSpeed, Progress);
    private float CurrentSpawnInterval => Mathf.Lerp(startSpawnInterval, minSpawnInterval, Progress);
    private float CurrentJongseongChance => Mathf.Lerp(startJongseongChance, maxJongseongChance, Progress);

    // 캐시하지 않고 매번 가져온다 (UIDocument가 패널을 재생성하면 캐시가 무효화됨)
    private VisualElement Root => uiDocument.rootVisualElement;

    private void Start()
    {
        if (gungseoFont == null)
            Debug.LogWarning("[EnemySpawner] FontAsset이 비어 있습니다. 기본 폰트에는 한글이 없어 글자가 안 보일 수 있어요.");
    }

    private void Update()
    {
        // 레이아웃이 아직 계산 전이면 대기
        if (float.IsNaN(Root.resolvedStyle.width) || Root.resolvedStyle.width <= 0f)
            return;

        elapsed += Time.deltaTime;

        timer += Time.deltaTime;
        if (timer >= CurrentSpawnInterval)
        {
            timer = 0f;
            Spawn();
        }

        foreach (Enemy enemy in activeEnemies)
            enemy.Tick(Time.deltaTime);

        // 콜백에서 모은 제거 대상 정리
        if (toRemove.Count > 0)
        {
            foreach (Enemy enemy in toRemove)
            {
                enemy.Remove();
                activeEnemies.Remove(enemy);
            }
            toRemove.Clear();
        }
    }

    private void Spawn()
    {
        string text = mode == GameMode.Letter ? PickLetter() : PickWord();
        if (string.IsNullOrEmpty(text)) return; // 뽑을 게 없으면 스킵

        // 글자 수만큼 폭을 고려해 X 범위 계산
        float textWidth = fontSize * text.Length;
        float x = Random.Range(sideMarginPx, Mathf.Max(sideMarginPx + 1f, Root.resolvedStyle.width - sideMarginPx - textWidth));
        float startY = -fontSize; // 화면 위쪽 밖에서 시작
        float deadlineY = Root.resolvedStyle.height * deadlineRatio;

        var enemy = new Enemy(text, x, startY, CurrentFallSpeed, deadlineY);
        enemy.Element.style.fontSize = fontSize;
        enemy.Element.style.color = Color.white; // 테마 기본색(검정)이 배경에 묻히는 것 방지
        if (gungseoFont != null)
            enemy.Element.style.unityFontDefinition = new StyleFontDefinition(gungseoFont);

        enemy.OnReachedDeadline += HandleReachedDeadline;
        enemy.OnDied += HandleDied;

        Root.Add(enemy.Element);
        activeEnemies.Add(enemy);

        Debug.Log($"[EnemySpawner] 스폰: '{text}' x={x:F0}");
    }

    /// <summary>[낱말 모드] 자모를 랜덤 조합해 화면에 없는 외자를 뽑는다.</summary>
    private string PickLetter()
    {
        var used = new HashSet<string>(activeEnemies.Select(e => e.Text));

        const int maxAttempts = 30; // 중복 회피 재시도 횟수
        for (int i = 0; i < maxAttempts; i++)
        {
            char cho = choseongPool[Random.Range(0, choseongPool.Length)];
            char jung = jungseongPool[Random.Range(0, jungseongPool.Length)];
            char jong = (jongseongPool.Length > 0 && Random.value < CurrentJongseongChance)
                ? jongseongPool[Random.Range(0, jongseongPool.Length)]
                : '\0';

            char letter = HangulComposer.Compose(cho, jung, jong);
            if (letter != '\0' && !used.Contains(letter.ToString()))
                return letter.ToString();
        }
        return null; // 계속 중복이면 이번 스폰은 스킵
    }

    /// <summary>[단어 모드] 단어 풀에서 화면에 없는 단어를 뽑는다.</summary>
    private string PickWord()
    {
        var used = new HashSet<string>(activeEnemies.Select(e => e.Text));
        var candidates = wordPool.Where(w => !string.IsNullOrEmpty(w) && !used.Contains(w)).ToList();
        if (candidates.Count == 0) return null;
        return candidates[Random.Range(0, candidates.Count)];
    }

    private void HandleReachedDeadline(Enemy enemy)
    {
        toRemove.Add(enemy);
        // TODO: 하트(목숨) 감소 처리
    }

    private void HandleDied(Enemy enemy)
    {
        toRemove.Add(enemy);
        // TODO: 점수 증가 처리
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

/// <summary>
/// UIDocument 패널에 일정 간격으로 글자 적(Label)을 스폰한다.
/// 화면에 같은 글자가 동시에 두 개 나오지 않도록 관리한다.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private FontAsset gungseoFont; // 궁서체 FontAsset (없으면 USS 클래스로 지정)

    [Header("스폰")]
    [SerializeField] private float spawnInterval = 2f;
    [SerializeField] private float fallSpeed = 120f; // px/sec
    [SerializeField] private int fontSize = 72;

    [Header("위치")]
    [SerializeField] private float sideMarginPx = 80f;              // 좌우 여백
    [Range(0f, 1f)][SerializeField] private float deadlineRatio = 0.8f; // 패널 높이 대비 플레이어 라인

    [Header("자모 풀 (여기 있는 것들만 조합에 사용)")]
    [SerializeField] private string choseongPool = "ㄱㄴㄷㄹㅁㅂㅅㅇㅈㅊㅋㅌㅍㅎ";
    [SerializeField] private string jungseongPool = "ㅏㅑㅓㅕㅗㅛㅜㅠㅡㅣㅐㅔ";
    [SerializeField] private string jongseongPool = "ㄱㄴㄹㅁㅂㅅㅇ";
    [Range(0f, 1f)][SerializeField] private float jongseongChance = 0.4f; // 받침이 붙을 확률

    private readonly List<Enemy> activeEnemies = new List<Enemy>();
    private readonly List<Enemy> toRemove = new List<Enemy>();
    private float timer;

    public IReadOnlyList<Enemy> ActiveEnemies => activeEnemies;

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

        timer += Time.deltaTime;
        if (timer >= spawnInterval)
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
        char letter = PickLetter();
        if (letter == '\0') return; // 남은 글자가 없으면 스킵

        float x = Random.Range(sideMarginPx, Root.resolvedStyle.width - sideMarginPx - fontSize);
        float startY = -fontSize; // 화면 위쪽 밖에서 시작
        float deadlineY = Root.resolvedStyle.height * deadlineRatio;

        var enemy = new Enemy(letter, x, startY, fallSpeed, deadlineY);
        enemy.Element.style.fontSize = fontSize;
        enemy.Element.style.color = Color.white; // 테마 기본색(검정)이 배경에 묻히는 것 방지
        if (gungseoFont != null)
            enemy.Element.style.unityFontDefinition = new StyleFontDefinition(gungseoFont);

        enemy.OnReachedDeadline += HandleReachedDeadline;
        enemy.OnDied += HandleDied;

        Root.Add(enemy.Element);
        activeEnemies.Add(enemy);

        Debug.Log($"[EnemySpawner] 스폰: '{letter}' x={x:F0}");
    }

    /// <summary>자모를 랜덤 조합해 화면에 없는 글자를 뽑는다.</summary>
    private char PickLetter()
    {
        var used = new HashSet<char>(activeEnemies.Select(e => e.Letter));

        const int maxAttempts = 30; // 중복 회피 재시도 횟수
        for (int i = 0; i < maxAttempts; i++)
        {
            char cho = choseongPool[Random.Range(0, choseongPool.Length)];
            char jung = jungseongPool[Random.Range(0, jungseongPool.Length)];
            char jong = (jongseongPool.Length > 0 && Random.value < jongseongChance)
                ? jongseongPool[Random.Range(0, jongseongPool.Length)]
                : '\0';

            char letter = HangulComposer.Compose(cho, jung, jong);
            if (letter != '\0' && !used.Contains(letter))
                return letter;
        }
        return '\0'; // 계속 중복이면 이번 스폰은 스킵
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

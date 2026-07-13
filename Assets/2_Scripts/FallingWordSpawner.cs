using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 낙하식 출제: 낱말/단어가 화면 위에서 아래로 떨어진다.
///
/// 글자는 월드 공간 TextMesh로 렌더링 — 정렬 순서(sortingOrder)를 낮게 줘서
/// 쓰기 칸(WritingCellBox)·획(LineRenderer) 뒤에 그려진다.
/// (예전 UI Toolkit Label 방식은 오버레이 UI라 항상 맨 위에 그려져 칸을 가렸음)
///
/// EnemySpawner(넘기기 방식)와 독립된 별도 시스템.
/// 스테이지가 있으면 스테이지 글자들이 셔플로 떨어지고, 없으면 경고.
///
/// 게임 로직 연결:
///  - TryClearWord(text) : 해당 글자를 맞혔을 때 호출 → 제거 + OnWordCleared
///  - OnWordReachedBottom : 바닥(데드라인)에 닿았을 때 (목숨 감소 등)
///  - ActiveWords          : 현재 떨어지는 중인 글자들
/// </summary>
public class FallingWordSpawner : MonoBehaviour
{
    /// <summary>떨어지는 글자 하나 (월드 TextMesh 래퍼)</summary>
    public class FallingWord
    {
        public string Text { get; }
        public GameObject Go { get; }

        readonly float speed;      // 월드 단위/초 (아래로)
        readonly float deadlineY;  // 이 월드 y 이하로 내려가면 바닥
        float y;

        public bool ReachedDeadline { get; private set; }

        public FallingWord(string text, GameObject go, float startY, float speed, float deadlineY)
        {
            Text = text;
            Go = go;
            this.speed = speed;
            this.deadlineY = deadlineY;
            y = startY;
        }

        public void Tick(float deltaTime)
        {
            y -= speed * deltaTime;
            if (Go != null)
            {
                Vector3 p = Go.transform.position;
                p.y = y;
                Go.transform.position = p;
            }
            if (y <= deadlineY) ReachedDeadline = true;
        }

        public void Remove()
        {
            if (Go != null) Destroy(Go);
        }
    }

    [Header("참조")]
    [Tooltip("글자를 그릴 폰트 (레거시 다이내믹 폰트 — 한글은 OS 폰트 폴백으로 렌더링)")]
    [SerializeField] Font font;

    [Header("모드 / 스테이지")]
    [SerializeField] GameMode mode = GameMode.Letter;
    [SerializeField] StageData[] stages;

    [Header("난이도 (시작 → 최대, 완만하게 상승)")]
    [SerializeField] float startSpawnInterval = 2.5f;
    [SerializeField] float minSpawnInterval = 0.9f;
    [SerializeField] float startFallSpeed = 80f;   // px/sec (화면 픽셀 기준 — 내부에서 월드로 환산)
    [SerializeField] float maxFallSpeed = 240f;
    [SerializeField] float rampDuration = 180f;    // 이 시간쯤 최대 난이도의 95% 도달

    [Header("표시")]
    [SerializeField] int fontSize = 90;            // px 기준 글자 높이
    [SerializeField] float sideMarginPx = 80f;
    [Range(0f, 1f)][SerializeField] float deadlineRatio = 0.85f;

    [Tooltip("정렬 순서 — 쓰기 칸(-10)과 획(0)보다 낮아야 글자가 칸 뒤로 지나간다")]
    [SerializeField] int sortingOrder = -20;

    readonly List<FallingWord> active = new List<FallingWord>();
    readonly List<string> queue = new List<string>(); // 현재 스테이지 셔플 큐
    int stageIndex;
    float spawnTimer;
    float elapsed;
    Camera cam;

    public IReadOnlyList<FallingWord> ActiveWords => active;
    public int CurrentStage => stageIndex + 1;

    /// <summary>스테이지가 바뀔 때 (번호, 데이터)</summary>
    public System.Action<int, StageData> OnStageChanged;
    /// <summary>글자가 바닥에 닿았을 때</summary>
    public System.Action<FallingWord> OnWordReachedBottom;
    /// <summary>글자를 맞혀서 제거됐을 때</summary>
    public System.Action<FallingWord> OnWordCleared;

    float Progress => 1f - Mathf.Exp(-3f * elapsed / rampDuration);
    float CurrentSpeedPx => Mathf.Lerp(startFallSpeed, maxFallSpeed, Progress);
    float CurrentInterval => Mathf.Lerp(startSpawnInterval, minSpawnInterval, Progress);

    /// <summary>화면 픽셀 → 월드 단위 환산 계수</summary>
    float WorldPerPx => cam != null && cam.pixelHeight > 0
        ? 2f * cam.orthographicSize / cam.pixelHeight
        : 0.01f;

    void OnEnable()
    {
        cam = Camera.main;
    }

    void Update()
    {
        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) return;
        }

        elapsed += Time.deltaTime;
        spawnTimer += Time.deltaTime;
        if (spawnTimer >= CurrentInterval)
        {
            spawnTimer = 0f;
            Spawn();
        }

        // 낙하 + 바닥 처리
        for (int i = active.Count - 1; i >= 0; i--)
        {
            FallingWord w = active[i];
            w.Tick(Time.deltaTime);
            if (w.ReachedDeadline)
            {
                active.RemoveAt(i);
                w.Remove();
                OnWordReachedBottom?.Invoke(w);
            }
        }
    }

    void Spawn()
    {
        string text = NextText();
        if (string.IsNullOrEmpty(text)) return;

        float wpp = WorldPerPx;
        float camH = 2f * cam.orthographicSize;
        float camW = camH * cam.aspect;
        float left = cam.transform.position.x - camW * 0.5f;
        float top = cam.transform.position.y + camH * 0.5f;

        float charWorld = fontSize * wpp;                 // 글자 높이 (월드)
        float textWorldW = charWorld * text.Length;       // 대략적 글자 폭
        float margin = sideMarginPx * wpp;

        float x = Random.Range(left + margin,
                               Mathf.Max(left + margin + 0.01f, left + camW - margin - textWorldW));
        float startY = top + charWorld;                    // 화면 위 살짝 바깥에서 시작
        float deadlineY = top - camH * deadlineRatio;      // 예전 UI 기준(위에서 85%)과 동일

        var go = new GameObject("FallingWord_" + text);
        go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(x, startY, 0f);

        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.font = font;
        tm.fontSize = fontSize;
        tm.characterSize = 10f * wpp;   // 줄 높이 ≈ fontSize × characterSize × 0.1 = fontSize(px) × wpp
        tm.color = Color.white;
        tm.anchor = TextAnchor.UpperLeft;

        var mr = go.GetComponent<MeshRenderer>();
        if (font != null) mr.material = font.material;
        mr.sortingOrder = sortingOrder;   // 쓰기 칸/획 뒤에 그리기

        var word = new FallingWord(text, go, startY, CurrentSpeedPx * wpp, deadlineY);
        active.Add(word);
    }

    /// <summary>스테이지 큐에서 다음 글자. 화면에 떠 있는 글자와는 겹치지 않게.</summary>
    string NextText()
    {
        if (stages == null || stages.Length == 0)
        {
            Debug.LogWarning("[FallingWord] 스테이지가 비어 있습니다.");
            return null;
        }

        var onScreen = new HashSet<string>(active.Select(w => w.Text));

        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (queue.Count == 0) FillQueue();
            if (queue.Count == 0) return null;

            string text = queue[queue.Count - 1];
            queue.RemoveAt(queue.Count - 1);
            if (!onScreen.Contains(text)) return text;
        }
        return null;
    }

    void FillQueue()
    {
        // 현재 스테이지의 글자들을 셔플해서 채움. 다 쓰면 다음 스테이지로.
        for (int guard = 0; guard < stages.Length + 1; guard++)
        {
            string[] texts = stages[Mathf.Min(stageIndex, stages.Length - 1)].GetTexts(mode);
            if (texts.Length > 0)
            {
                queue.AddRange(texts);
                for (int i = queue.Count - 1; i > 0; i--)
                {
                    int k = Random.Range(0, i + 1);
                    (queue[i], queue[k]) = (queue[k], queue[i]);
                }
                OnStageChanged?.Invoke(stageIndex + 1, stages[Mathf.Min(stageIndex, stages.Length - 1)]);
                if (stageIndex < stages.Length - 1) stageIndex++; // 다음 채움 때 다음 스테이지 (마지막은 반복)
                return;
            }
            if (stageIndex < stages.Length - 1) stageIndex++;
            else return;
        }
    }

    /// <summary>글자를 맞혔을 때 호출. 화면의 해당 글자를 제거하고 true 반환.</summary>
    public bool TryClearWord(string text)
    {
        for (int i = 0; i < active.Count; i++)
        {
            if (active[i].Text == text)
            {
                FallingWord w = active[i];
                active.RemoveAt(i);
                w.Remove();
                OnWordCleared?.Invoke(w);
                return true;
            }
        }
        return false;
    }

    /// <summary>화면 비우고 난이도/스테이지 초기화</summary>
    public void ResetAll()
    {
        foreach (var w in active) w.Remove();
        active.Clear();
        queue.Clear();
        stageIndex = 0;
        elapsed = 0f;
        spawnTimer = 0f;
    }
}

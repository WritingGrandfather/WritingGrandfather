using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 낙하식 출제: 낱말/단어가 화면 위에서 아래로 떨어진다. (UI Toolkit)
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
    /// <summary>떨어지는 글자 하나 (Label 래퍼)</summary>
    public class FallingWord
    {
        public string Text { get; }
        public Label Element { get; }

        readonly float speed;
        readonly float deadlineY;
        float y;

        public bool ReachedDeadline { get; private set; }

        public FallingWord(string text, float x, float startY, float speed, float deadlineY)
        {
            Text = text;
            this.speed = speed;
            this.deadlineY = deadlineY;
            y = startY;

            Element = new Label(text);
            Element.AddToClassList("falling-word");
            Element.pickingMode = PickingMode.Ignore; // 포인터 통과 (그리기/버튼 방해 금지)
            Element.style.position = Position.Absolute;
            Element.style.left = x;
            Element.style.top = y;
        }

        public void Tick(float deltaTime)
        {
            y += speed * deltaTime;
            Element.style.top = y;
            if (y >= deadlineY) ReachedDeadline = true;
        }

        public void Remove() => Element.RemoveFromHierarchy();
    }

    [Header("참조")]
    [SerializeField] UIDocument uiDocument;
    [SerializeField] Font font;

    [Header("모드 / 스테이지")]
    [SerializeField] GameMode mode = GameMode.Letter;
    [SerializeField] StageData[] stages;

    [Header("난이도 (시작 → 최대, 완만하게 상승)")]
    [SerializeField] float startSpawnInterval = 2.5f;
    [SerializeField] float minSpawnInterval = 0.9f;
    [SerializeField] float startFallSpeed = 80f;   // px/sec
    [SerializeField] float maxFallSpeed = 240f;
    [SerializeField] float rampDuration = 180f;    // 이 시간쯤 최대 난이도의 95% 도달

    [Header("표시")]
    [SerializeField] int fontSize = 90;
    [SerializeField] float sideMarginPx = 80f;
    [Range(0f, 1f)][SerializeField] float deadlineRatio = 0.85f;

    readonly List<FallingWord> active = new List<FallingWord>();
    readonly List<string> queue = new List<string>(); // 현재 스테이지 셔플 큐
    int stageIndex;
    float spawnTimer;
    float elapsed;

    public IReadOnlyList<FallingWord> ActiveWords => active;
    public int CurrentStage => stageIndex + 1;

    /// <summary>스테이지가 바뀔 때 (번호, 데이터)</summary>
    public System.Action<int, StageData> OnStageChanged;
    /// <summary>글자가 바닥에 닿았을 때</summary>
    public System.Action<FallingWord> OnWordReachedBottom;
    /// <summary>글자를 맞혀서 제거됐을 때</summary>
    public System.Action<FallingWord> OnWordCleared;

    VisualElement Root => uiDocument.rootVisualElement;

    float Progress => 1f - Mathf.Exp(-3f * elapsed / rampDuration);
    float CurrentSpeed => Mathf.Lerp(startFallSpeed, maxFallSpeed, Progress);
    float CurrentInterval => Mathf.Lerp(startSpawnInterval, minSpawnInterval, Progress);

    void OnEnable()
    {
        VisualElement root = Root;
        root.pickingMode = PickingMode.Ignore; // 전체 화면 root가 포인터를 삼키지 않게
        root.style.position = Position.Absolute;
        root.style.top = 0;
        root.style.bottom = 0;
        root.style.left = 0;
        root.style.right = 0;
    }

    void Update()
    {
        if (float.IsNaN(Root.resolvedStyle.width) || Root.resolvedStyle.width <= 0f)
            return;

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

        float width = Root.resolvedStyle.width;
        float textWidth = fontSize * text.Length;
        float x = Random.Range(sideMarginPx, Mathf.Max(sideMarginPx + 1f, width - sideMarginPx - textWidth));
        float deadlineY = Root.resolvedStyle.height * deadlineRatio;

        var word = new FallingWord(text, x, -fontSize, CurrentSpeed, deadlineY);
        word.Element.style.fontSize = fontSize;
        word.Element.style.color = Color.white;
        if (font != null)
            word.Element.style.unityFontDefinition = new StyleFontDefinition(font);

        Root.Add(word.Element);
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

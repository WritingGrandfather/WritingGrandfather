using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

public class Eraser : MonoBehaviour
{
    public DrawLine drawLine;

    [Range(0.05f, 1f)]
    public float eraserRadius = 0.2f;

    public string eraserSoundName = "EraserSound";

    Camera cam;
    Coroutine eraseCoroutine;
    bool isActive;
    AudioSource currentSfxSource;
    float stillTimer = 0f;
    const float stopGraceTime = 0.05f;

    List<(System.Action undo, System.Action redo)> sessionActions;

    void Awake()
    {
        cam = Camera.main;
        var playerInput = GetComponent<PlayerInput>();
        var clickAction = playerInput.actions["Click"];
        clickAction.started  += OnEraseStart;
        clickAction.canceled += OnEraseEnd;
    }

    void OnDestroy()
    {
        var playerInput = GetComponent<PlayerInput>();
        if (playerInput == null) return;
        var clickAction = playerInput.actions["Click"];
        clickAction.started  -= OnEraseStart;
        clickAction.canceled -= OnEraseEnd;
    }

    public void Activate()                    { isActive = true;  drawLine.drawingEnabled = false; }
    public void Deactivate()                  { isActive = false; drawLine.drawingEnabled = true;  }
    public void SetEraserRadius(float radius) { eraserRadius = radius; }

    void OnEraseStart(InputAction.CallbackContext ctx)
    {
        if (!isActive || IsPointerOverUI() || Pointer.current == null) return;
        sessionActions = new List<(System.Action, System.Action)>();
        eraseCoroutine = StartCoroutine(EraseLoop());
    }

    void OnEraseEnd(InputAction.CallbackContext ctx)
    {
        if (eraseCoroutine == null) return;
        StopCoroutine(eraseCoroutine);
        eraseCoroutine = null;

        ReleaseSfxSource();
        stillTimer = 0f;

        if (sessionActions != null && sessionActions.Count > 0)
            UndoManager.Instance.RecordBatch(sessionActions);
        sessionActions = null;
    }

    // 사운드 소스를 정지하고 놓아줌 — mute 상태를 반드시 풀어서 반환해야
    // 풀에서 재사용될 때 음소거된 채로 나오는 문제를 방지함
    void ReleaseSfxSource()
    {
        if (currentSfxSource != null)
        {
            currentSfxSource.Stop();
            currentSfxSource.mute = false;
        }
        currentSfxSource = null;
    }

    IEnumerator EraseLoop()
    {
        Vector2 prevPos = cam.ScreenToWorldPoint(Pointer.current.position.ReadValue());

        // Play()는 호출 시점마다 클립 로드/오디오 시작 지연이 생길 수 있으므로,
        // 세션 시작과 동시에 루프 사운드를 미리 재생해두고 mute로만 on/off 제어함.
        // → 실제로 지워지는 순간 mute만 풀면 되므로 시작 지연이 0에 가까움
        currentSfxSource = SoundManager.Instance.PlaySfx(eraserSoundName, loop: true);
        if (currentSfxSource != null) currentSfxSource.mute = true;

        bool initialErased = ProcessErase(new List<Vector2> { prevPos });
        if (initialErased && currentSfxSource != null)
            currentSfxSource.mute = false;
        stillTimer = 0f;

        while (true)
        {
            yield return null;

            Vector2 currentPos = cam.ScreenToWorldPoint(Pointer.current.position.ReadValue());
            float dist = Vector2.Distance(prevPos, currentPos);

            // 이전 위치와 현재 위치 사이를 반지름 절반 간격으로 보간
            int steps = Mathf.Max(1, Mathf.CeilToInt(dist / (eraserRadius * 0.5f)));
            var samples = new List<Vector2>();
            for (int i = 1; i <= steps; i++)
                samples.Add(Vector2.Lerp(prevPos, currentPos, (float)i / steps));

            bool erased = ProcessErase(samples);

            if (erased)
            {
                stillTimer = 0f;

                // 소스가 어떤 이유로든 죽었으면 재생성 (안전장치)
                if (currentSfxSource == null || !currentSfxSource.isPlaying)
                    currentSfxSource = SoundManager.Instance.PlaySfx(eraserSoundName, loop: true);

                if (currentSfxSource != null) currentSfxSource.mute = false;
            }
            else
            {
                stillTimer += Time.deltaTime;
                if (stillTimer >= stopGraceTime && currentSfxSource != null)
                    currentSfxSource.mute = true; // Stop 대신 mute — 다시 지울 때 즉시 소리 재개
            }

            prevPos = currentPos;
        }
    }

    // 이번 프레임의 모든 샘플 위치로 영향받는 오브젝트를 먼저 수집한 뒤 각각 한 번씩만 처리.
    // 새로 생성된 조각이 같은 프레임에 재감지되는 연쇄 지우기를 방지함.
    bool ProcessErase(List<Vector2> samples)
    {
        var affected = new HashSet<GameObject>();
        foreach (var pos in samples)
            foreach (var hit in Physics2D.OverlapCircleAll(pos, eraserRadius))
                if (hit is EdgeCollider2D)
                    affected.Add(hit.gameObject);

        if (affected.Count == 0) return false;

        foreach (var go in affected)
        {
            var lr = go.GetComponent<LineRenderer>();
            if (lr == null) continue;

            var original = new List<Vector2>();
            for (int i = 0; i < lr.positionCount; i++)
                original.Add(lr.GetPosition(i));

            float width = lr.startWidth;
            Color color  = lr.startColor;

            var segments = SplitLine(original, samples);

            drawLine.RemoveLine(go);

            var created = new List<GameObject>();
            foreach (var seg in segments)
            {
                var newGo = drawLine.CreateLine(seg, width, color);
                if (newGo != null) created.Add(newGo);
            }

            var capturedOriginal  = new List<Vector2>(original);
            var capturedWidth     = width;
            var capturedColor     = color;
            var capturedSegments  = segments;
            var originalGoRef     = new GameObject[] { null };
            var createdGoRefs     = new List<GameObject>(created);

            sessionActions?.Add((
                undo: () =>
                {
                    foreach (var seg in createdGoRefs) drawLine.RemoveLine(seg);
                    createdGoRefs.Clear();
                    originalGoRef[0] = drawLine.CreateLine(capturedOriginal, capturedWidth, capturedColor);
                },
                redo: () =>
                {
                    drawLine.RemoveLine(originalGoRef[0]);
                    originalGoRef[0] = null;
                    createdGoRefs.Clear();
                    foreach (var seg in capturedSegments)
                    {
                        var newGo = drawLine.CreateLine(seg, capturedWidth, capturedColor);
                        if (newGo != null) createdGoRefs.Add(newGo);
                    }
                }
            ));
        }
        return true;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 선분(segment) 기반 SplitLine
    //
    // 이전 포인트 기준 방식의 문제:
    //   두 포인트가 모두 원 밖에 있어도 그 사이의 선분이 원을 통과할 수 있음.
    //   → 이 경우 삭제가 일어나지 않거나 부정확하게 일어남.
    //
    // 새 방식:
    //   각 선분(p1→p2)에 대해 이차방정식으로 원과의 교점 t ∈ (0,1)을 계산.
    //   여러 지우개 원의 합집합 기준으로 inside ↔ outside 전환이 일어나는 t값을
    //   오름차순으로 추출한 뒤, 그 지점에서만 선을 자름.
    // ──────────────────────────────────────────────────────────────────────────
    List<List<Vector2>> SplitLine(List<Vector2> points, List<Vector2> centers)
    {
        float r      = eraserRadius;
        var result   = new List<List<Vector2>>();
        var current  = new List<Vector2>();

        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector2 p1 = points[i];
            Vector2 p2 = points[i + 1];

            bool startInside = IsInsideAny(p1, centers, r);

            // p1이 밖에 있으면 현재 kept 구간에 추가 (이미 있으면 중복 추가 안 함)
            if (!startInside && current.Count == 0)
                current.Add(p1);

            // 이 선분에서 '지우개 합집합 안/밖' 상태가 전환되는 t 값 목록 (오름차순)
            var transitions = GetTransitions(p1, p2, centers, r);
            bool inside = startInside;

            foreach (float t in transitions)
            {
                Vector2 pt = Vector2.Lerp(p1, p2, t);

                if (inside) // inside → outside : kept 구간 재개
                {
                    current.Add(pt);
                    inside = false;
                }
                else        // outside → inside : kept 구간 종료
                {
                    current.Add(pt);
                    if (current.Count >= 2) result.Add(current);
                    current = new List<Vector2>();
                    inside = true;
                }
            }

            // 선분 끝(p2) 처리
            if (!inside)
                current.Add(p2);
            else
            {
                if (current.Count >= 2) result.Add(current);
                current = new List<Vector2>();
            }
        }

        if (current.Count >= 2) result.Add(current);

        // 너무 짧은 구간(미세 잔재)은 제거
        result.RemoveAll(seg => SegmentLength(seg) < 0.05f);
        return result;
    }

    // 선분(p1→p2)에서 지우개 원들의 합집합 경계를 넘는 t 값들을 반환 (오름차순).
    // 단순히 원 경계를 넘는 모든 t가 아닌, '합집합 기준 inside ↔ outside' 전환만 추출.
    // 예: 두 원이 겹쳐있을 때 한 원에서 나와도 다른 원 안이면 전환으로 보지 않음.
    List<float> GetTransitions(Vector2 p1, Vector2 p2, List<Vector2> centers, float radius)
    {
        Vector2 d = p2 - p1;
        float aSq = Vector2.Dot(d, d);
        if (aSq < 1e-10f) return new List<float>(); // 길이 0인 선분

        // 각 원과의 교점을 (t값, 방향) 쌍으로 수집
        // +1 = 원 진입, -1 = 원 탈출 (무한 직선 기준)
        var crossings = new List<(float t, int delta)>();

        foreach (var c in centers)
        {
            Vector2 f  = p1 - c;
            float b    = 2f * Vector2.Dot(f, d);
            float cv   = Vector2.Dot(f, f) - radius * radius;
            float disc = b * b - 4f * aSq * cv;

            if (disc < 0f) continue; // 원과 교점 없음

            disc = Mathf.Sqrt(disc);
            float t1 = (-b - disc) / (2f * aSq); // 진입 t (항상 t1 ≤ t2)
            float t2 = (-b + disc) / (2f * aSq); // 탈출 t

            // 선분 내부 (끝점 제외) 교점만 수집; 끝점은 IsInsideAny/CountCirclesContaining 에서 처리
            const float eps = 1e-5f;
            if (t1 > eps && t1 < 1f - eps) crossings.Add((t1, +1));
            if (t2 > eps && t2 < 1f - eps) crossings.Add((t2, -1));
        }

        crossings.Sort((a, b_) => a.t.CompareTo(b_.t));

        // 합집합 기준 실제 전환(0→1 또는 1→0) 지점만 추출
        var events   = new List<float>();
        int insideCount = CountCirclesContaining(p1, centers, radius);

        foreach (var (t, delta) in crossings)
        {
            bool was = insideCount > 0;
            insideCount = Mathf.Max(0, insideCount + delta);
            if ((insideCount > 0) != was)
                events.Add(t);
        }

        return events;
    }

    // p 가 circles 중 어느 하나라도 안에 있는지 (경계 포함하지 않음 — 경계선상은 밖으로 처리)
    bool IsInsideAny(Vector2 p, List<Vector2> centers, float radius)
    {
        foreach (var c in centers)
            if (Vector2.Distance(p, c) < radius) return true;
        return false;
    }

    // p 를 포함하는 원의 개수 (합집합 inside 카운트 초기값 계산용)
    int CountCirclesContaining(Vector2 p, List<Vector2> centers, float radius)
    {
        int n = 0;
        foreach (var c in centers)
            if (Vector2.Distance(p, c) < radius) n++;
        return n;
    }

    float SegmentLength(List<Vector2> seg)
    {
        float len = 0f;
        for (int i = 1; i < seg.Count; i++)
            len += Vector2.Distance(seg[i - 1], seg[i]);
        return len;
    }

    bool IsPointerOverUI()
    {
        if (EventSystem.current == null) return false;
        if (EventSystem.current.IsPointerOverGameObject()) return true;
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            return EventSystem.current.IsPointerOverGameObject(0);
        return false;
    }
}
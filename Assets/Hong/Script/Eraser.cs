using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

public class Eraser : MonoBehaviour
{
    public DrowLine drawLine;

    [Range(0.05f, 1f)]
    public float eraserRadius = 0.2f;

    Camera cam;
    Coroutine eraseCoroutine;
    bool isActive;

    // 드래그 세션 동안 발생한 undo 액션 목록
    // 드래그가 끝날 때 UndoManager에 한 번에 배치로 기록됨
    List<System.Action> sessionUndoActions;

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

    public void Activate()
    {
        isActive = true;
        drawLine.drawingEnabled = false;
    }

    public void Deactivate()
    {
        isActive = false;
        drawLine.drawingEnabled = true;
    }

    public void SetEraserRadius(float radius)
    {
        eraserRadius = radius;
    }

    void OnEraseStart(InputAction.CallbackContext ctx)
    {
        if (!isActive || IsPointerOverUI()) return;

        // 새 드래그 세션 시작 — undo 액션 목록 초기화
        sessionUndoActions = new List<System.Action>();
        eraseCoroutine = StartCoroutine(EraseLoop());
    }

    void OnEraseEnd(InputAction.CallbackContext ctx)
    {
        if (eraseCoroutine == null) return;
        StopCoroutine(eraseCoroutine);
        eraseCoroutine = null;

        // 드래그 세션 전체를 하나의 undo 스텝으로 기록
        if (sessionUndoActions != null && sessionUndoActions.Count > 0)
            UndoManager.Instance.RecordBatch(sessionUndoActions);

        sessionUndoActions = null;
    }

    IEnumerator EraseLoop()
    {
        while (true)
        {
            Vector2 worldPos = cam.ScreenToWorldPoint(Pointer.current.position.ReadValue());
            EraseAt(worldPos);
            yield return null;
        }
    }

    // 지우개 위치에서 닿은 라인들을 부분적으로 잘라냄
    void EraseAt(Vector2 center)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(center, eraserRadius);

        // 같은 프레임에 딕셔너리가 바뀌는 걸 막기 위해 먼저 수집
        var toProcess = new List<GameObject>();
        foreach (var hit in hits)
        {
            if (hit is EdgeCollider2D)
                toProcess.Add(hit.gameObject);
        }

        foreach (var go in toProcess)
        {
            var lr = go.GetComponent<LineRenderer>();
            if (lr == null) continue;

            // 원본 라인의 포인트, 두께, 색상 수집
            var original = new List<Vector2>();
            for (int i = 0; i < lr.positionCount; i++)
                original.Add(lr.GetPosition(i));

            float width = lr.startWidth;
            Color color = lr.startColor;

            // 지우개 원과 겹치지 않는 구간들로 분리
            var segments = SplitLine(original, center, eraserRadius);

            // 원본 제거
            drawLine.RemoveLine(go);

            // 남은 구간마다 새 라인 생성 후 GO 수집
            var createdSegments = new List<GameObject>();
            foreach (var seg in segments)
            {
                var created = drawLine.CreateLine(seg, width, color);
                if (created != null) createdSegments.Add(created);
            }

            // undo 액션 : 생성된 조각들 제거 + 원본 복원
            var capturedPoints   = new List<Vector2>(original);
            var capturedWidth    = width;
            var capturedColor    = color;
            var capturedSegments = createdSegments;

            sessionUndoActions?.Add(() =>
            {
                foreach (var seg in capturedSegments)
                    drawLine.RemoveLine(seg);
                drawLine.CreateLine(capturedPoints, capturedWidth, capturedColor);
            });
        }
    }

    // 라인 포인트 목록을 지우개 원 기준으로 살아있는 구간들로 분리
    List<List<Vector2>> SplitLine(List<Vector2> points, Vector2 center, float radius)
    {
        var result  = new List<List<Vector2>>();
        var current = new List<Vector2>();

        for (int i = 0; i < points.Count; i++)
        {
            bool inside = Vector2.Distance(points[i], center) <= radius;

            if (!inside)
            {
                // 직전 포인트가 원 안이었다면 경계 교차점을 구간 시작점으로 추가
                if (i > 0 && Vector2.Distance(points[i - 1], center) <= radius)
                {
                    var cross = CircleIntersection(points[i - 1], points[i], center, radius);
                    if (cross.HasValue) current.Add(cross.Value);
                }
                current.Add(points[i]);
            }
            else
            {
                // 직전 포인트가 원 밖이었다면 경계 교차점을 현재 구간 끝점으로 추가
                if (i > 0 && Vector2.Distance(points[i - 1], center) > radius)
                {
                    var cross = CircleIntersection(points[i - 1], points[i], center, radius);
                    if (cross.HasValue) current.Add(cross.Value);
                }

                // 현재 구간 마감
                if (current.Count >= 2) result.Add(current);
                current = new List<Vector2>();
            }
        }

        if (current.Count >= 2) result.Add(current);
        return result;
    }

    // 선분 p1→p2 와 원의 교차점 계산 (이차방정식 풀이)
    // p1이 원 안이면 탈출점, p1이 원 밖이면 진입점을 반환
    Vector2? CircleIntersection(Vector2 p1, Vector2 p2, Vector2 center, float radius)
    {
        Vector2 d = p2 - p1;
        Vector2 f = p1 - center;

        float a = Vector2.Dot(d, d);
        float b = 2f * Vector2.Dot(f, d);
        float c = Vector2.Dot(f, f) - radius * radius;

        float disc = b * b - 4f * a * c;
        if (disc < 0f) return null;

        disc = Mathf.Sqrt(disc);
        float t1 = (-b - disc) / (2f * a);
        float t2 = (-b + disc) / (2f * a);

        bool p1Inside = Vector2.Distance(p1, center) <= radius;

        // p1이 안에 있으면 탈출점(t2), 밖에 있으면 진입점(t1)
        float t = p1Inside ? t2 : t1;
        if (t >= 0f && t <= 1f) return p1 + t * d;

        return null;
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

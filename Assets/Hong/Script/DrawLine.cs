using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Pool;
using UnityEngine.EventSystems;
using Random = UnityEngine.Random;

public class DrowLine : MonoBehaviour
{
    // Resources/Prefabs/Line.prefab 의 파일명이 그대로 id가 됨
    const string linePoolId = "Line";

    // 선 두께 — Inspector 또는 UI Slider에서 조절 가능
    [Range(0.01f, 1f)]
    public float lineWidth = 0.1f;

    // 선 색상 — Inspector 또는 UI에서 조절 가능
    public Color lineColor = Color.black;

    LineRenderer lr;
    new EdgeCollider2D collider2D;
    List<Vector2> points = new List<Vector2>();

    // 현재 그리고 있는 라인의 풀 핸들
    // Dispose() 호출 시 id 없이도 풀로 자동 반환됨
    PooledObject<GameObject> currentHandle;

    // GameObject → 핸들 매핑 : 특정 라인을 지울 때 핸들을 찾아 Dispose하기 위해 사용
    Dictionary<GameObject, PooledObject<GameObject>> lineHandles = new Dictionary<GameObject, PooledObject<GameObject>>();

    // false면 드로우 입력을 무시 — 지우개 모드일 때 Eraser에서 false로 설정
    public bool drawingEnabled = true;

    // 특정 화면 좌표에서 지금 그리기를 시작해도 되는지 판단하는 델리게이트(선택, 비워두면 항상 허용).
    // drawingEnabled는 PointerMoveEvent 등 다른 이벤트가 미리 계산해 둔 값을 그냥 읽는 것이라,
    // 새 Input System의 액션 콜백(OnDrawStart/DrawLoop)과 그 이벤트가 같은 프레임 안에서 어느 쪽이
    // 먼저 실행되는지 보장이 없어 한 프레임 지연된(아직 갱신 안 된) 값을 참조하는 경우가 있었다 -
    // 그 결과 버튼을 눌러도 가끔 무시되거나, 버튼 위에서 그림이 같이 그려지는 문제가 생겼다.
    // 이 델리게이트는 실제로 확인이 필요한 그 순간의 포인터 위치로 매번 새로 판정해서 그런
    // 프레임 지연 없이 항상 정확하게 판단한다.
    public Func<Vector2, bool> canDrawAt;

    // OnDrawStart가 정상적으로 완료됐을 때만 true — OnDrawEnd에서 핸들 저장 여부 판단
    bool isDrawing;

    // true면 포인터를 뗄 때까지 새 그리기를 차단 (자동 판정 직후, 누른 손가락이 계속 그리는 것 방지)
    bool suppressUntilRelease;

    // Camera.main은 호출할 때마다 FindWithTag로 탐색하므로 매 프레임 호출하면 비효율적
    // Awake에서 한 번만 캐싱해서 사용
    Camera cam;

    Coroutine drawCoroutine;

    public PencilSound pencilSound;

    AudioSource currentSfxSource;
    int lastSoundIndex = -1;
    float soundTimer = 0f;
    const float soundSwitchInterval = 2f;

    // 사운드 정지 유예 시간 — 이 시간 이상 포인터가 멈춰 있어야 소리를 끔
    // 입력 장치 폴링 주기 < 게임 프레임 주기라서 움직이는 중에도 위치가 그대로인 프레임이 생기는데,
    // 그때마다 즉시 Stop하면 소리가 뚝뚝 끊기므로 유예를 둠
    const float stopGraceTime = 0.15f;
    float stillTimer = 0f;

    // Awake : PlayerInput 컴포넌트에서 Action을 찾아 콜백 등록
    // PlayerInput의 Behavior가 "Invoke C Sharp Events" 여야 동작함
    void Awake()
    {
        cam = Camera.main;

        var playerInput = GetComponent<PlayerInput>();

        // Click : 마우스 왼쪽 버튼 + 터치 press 바인딩
        var clickAction = playerInput.actions["Click"];

        pencilSound = GetComponent<PencilSound>();
        clickAction.started  += OnDrawStart;
        clickAction.canceled += OnDrawEnd;
    }

    // OnDestroy : PlayerInput이 오브젝트와 함께 파괴되므로 별도 Dispose 불필요
    // 단, 콜백은 명시적으로 해제해 잠재적 누수 방지
    void OnDestroy()
    {
        var playerInput = GetComponent<PlayerInput>();
        if (playerInput == null) return;

        var clickAction = playerInput.actions["Click"];
        clickAction.started  -= OnDrawStart;
        clickAction.canceled -= OnDrawEnd;
    }

    // UI 위에서 입력 중인지 확인.
    // IsPointerOverGameObject()는 InputAction 콜백 내에서 전 프레임 상태를 반환해
    // 버튼 클릭이 그리기로 새어나오는 문제가 있었다 - RaycastAll은 현재 위치로
    // 즉시 판정하므로 콜백 안에서도 정확하다.
    static readonly List<RaycastResult> _raycastResults = new List<RaycastResult>();
    bool IsPointerOverUI()
    {
        if (EventSystem.current == null || Pointer.current == null) return false;

        var ped = new PointerEventData(EventSystem.current)
        {
            position = Pointer.current.position.ReadValue()
        };
        _raycastResults.Clear();
        EventSystem.current.RaycastAll(ped, _raycastResults);
        return _raycastResults.Count > 0;
    }

    // started 콜백 : 마우스 왼쪽 버튼을 누르는 순간 호출
    // 풀에서 라인 오브젝트를 꺼내 초기화한 뒤 드로우 루프를 시작함
    void OnDrawStart(InputAction.CallbackContext ctx)
    {
        // 지우개 모드이거나 UI 조작 중에는 드로우 차단
        isDrawing = false;
        if (suppressUntilRelease || !drawingEnabled || IsPointerOverUI() || Pointer.current == null) return;

        Vector2 pointerScreenPos = Pointer.current.position.ReadValue();
        if (canDrawAt != null && !canDrawAt(pointerScreenPos)) return;

        Vector2 startPos = cam.ScreenToWorldPoint(pointerScreenPos);

        // out으로 핸들을 받아 저장 — 반환 시 Dispose()만 호출하면 됨
        GameObject currentLineGO = PoolManager.Instance.Spawn(linePoolId, Vector3.zero, transform, out currentHandle);

        if (currentLineGO == null)
        {
            Debug.LogError($"[DrowLine] '{linePoolId}' 스폰 실패 — Resources/Prefabs 안에 해당 id의 프리팹이 있는지, PooledObject 컴포넌트 id가 '{linePoolId}' 인지 확인하세요.");
            return;
        }

        lr = currentLineGO.GetComponent<LineRenderer>();
        collider2D = currentLineGO.GetComponent<EdgeCollider2D>();

        // 풀에서 꺼낸 오브젝트는 이전 사용 흔적이 남아있을 수 있으므로 초기화
        lr.positionCount = 0;
        collider2D.points = new Vector2[0];

        // 현재 설정된 두께 및 색상 적용
        lr.startWidth  = lineWidth;
        lr.endWidth    = lineWidth;
        lr.startColor  = lineColor;
        lr.endColor    = lineColor;

        points.Add(startPos);
        lr.positionCount = 1;
        lr.SetPosition(0, startPos);

        isDrawing = true;
        drawCoroutine = StartCoroutine(DrawLoop());
    }

    // 코루틴 : 버튼을 누르고 있는 동안 매 프레임 실행됨
    // yield return null 로 한 프레임씩 대기하며 마우스 위치를 추적함
    // 이전 포인트와 거리가 threshold 이상일 때만 포인트를 추가해 과도한 포인트 생성을 방지
    IEnumerator DrawLoop()
    {
        Vector2 prevPos = cam.ScreenToWorldPoint(Pointer.current.position.ReadValue());
        stillTimer = 0f;

        while (true)
        {
            Vector2 screenPos = Pointer.current.position.ReadValue();
            if (!drawingEnabled || IsPointerOverUI() || (canDrawAt != null && !canDrawAt(screenPos)))
            {
                yield return null;
                continue;
            }

            Vector2 pos = cam.ScreenToWorldPoint(screenPos);

            // 드로잉 포인트 추가 (드로잉 전용 threshold)
            if (Vector2.Distance(points[points.Count - 1], pos) > 0.001f)
            {
                points.Add(pos);
                lr.positionCount++;
                lr.SetPosition(lr.positionCount - 1, pos);
                collider2D.points = points.ToArray();
            }

            // 사운드는 매 프레임 이전 프레임 위치와 비교 — 드로잉 threshold와 독립적
            bool moving = Vector2.Distance(prevPos, pos) > 0.001f;
            prevPos = pos;

            if (moving)
            {
                stillTimer = 0f;
                soundTimer += Time.deltaTime;
                if (currentSfxSource == null || !currentSfxSource.isPlaying || soundTimer >= soundSwitchInterval)
                {
                    if (currentSfxSource != null) currentSfxSource.Stop();
                    lastSoundIndex = PlayRandomPencilSound(lastSoundIndex);
                    soundTimer = 0f;
                }
            }
            else
            {
                // 정지 프레임이 stopGraceTime 이상 누적됐을 때만 소리를 끔
                // (한두 프레임 멈춘 걸로는 끄지 않음 → 소리 뚝뚝 끊김 방지)
                stillTimer += Time.deltaTime;
                if (stillTimer >= stopGraceTime && currentSfxSource != null)
                {
                    currentSfxSource.Stop();
                    currentSfxSource = null;
                    soundTimer = 0f;
                }
            }

            yield return null;
        }
    }

    // canceled 콜백 : 마우스 왼쪽 버튼을 떼는 순간 호출
    // 드로우 루프를 중단하고 완성된 라인을 drawnLines에 보관함
    void OnDrawEnd(InputAction.CallbackContext ctx)
    {
        suppressUntilRelease = false; // 포인터를 뗐으므로 차단 해제

        if (drawCoroutine != null)
        {
            StopCoroutine(drawCoroutine);
            drawCoroutine = null;
        }

        // 정상적으로 그리기가 시작된 경우에만 핸들 저장 및 undo/redo 기록
        if (isDrawing)
        {
            var capturedGO = lr.gameObject;
            lineHandles[capturedGO] = currentHandle;
            isDrawing = false;

            var capturedPoints = new List<Vector2>(points);
            var capturedWidth  = lineWidth;
            var capturedColor  = lineColor;
            var goRef          = new GameObject[] { capturedGO };

            UndoManager.Instance.Record(
                () => RemoveLine(goRef[0]),
                () => { goRef[0] = CreateLine(capturedPoints, capturedWidth, capturedColor); }
            );
        }
        if (currentSfxSource != null) currentSfxSource.Stop();
        currentSfxSource = null;
        stillTimer = 0f;
        soundTimer = 0f;
        currentHandle = default;

        points.Clear();
    }

    int PlayRandomPencilSound(int exclude)
    {
        // pencilSound(같은 오브젝트의 PencilSound 컴포넌트)나 SoundManager 싱글톤이 없는 씬(예: 사운드 설정 없이
        // WritingPracticeScene을 단독으로 재생하는 경우)에서도 그리기 자체는 죽지 않도록 방어
        if (pencilSound == null || SoundManager.Instance == null) return -1;

        var sounds = pencilSound.pencilSounds;
        if (sounds == null || sounds.Count == 0) return -1;

        int idx = exclude;
        if (sounds.Count > 1)
            while (idx == exclude) idx = Random.Range(0, sounds.Count);
        else
            idx = 0;

        currentSfxSource = SoundManager.Instance.PlaySfx(sounds[idx], loop: true);
        return idx;
    }

    /// <summary>
    /// 지금 그리던 획을 강제 종료한다 (undo 기록 없이 풀로 반환).
    /// 포인터를 누른 채라면 뗄 때까지 새 그리기를 차단 — 자동 판정 직후 손가락이
    /// 계속 눌려 있어도 펜이 나오지 않게 한다.
    /// </summary>
    public void CancelCurrentStroke()
    {
        if (drawCoroutine != null)
        {
            StopCoroutine(drawCoroutine);
            drawCoroutine = null;
        }

        if (isDrawing)
        {
            isDrawing = false;
            ((IDisposable)currentHandle).Dispose(); // 그리던 라인을 풀로 반환
            currentHandle = default;
            points.Clear();
        }

        if (currentSfxSource != null)
        {
            currentSfxSource.Stop();
            currentSfxSource = null;
        }
        soundTimer = 0f;
        stillTimer = 0f;

        // 아직 누르고 있으면 뗄 때까지 새 그리기 차단
        if (Pointer.current != null && Pointer.current.press.isPressed)
            suppressUntilRelease = true;
    }

    // UI Slider의 OnValueChanged에 연결해서 두께를 실시간으로 조절
    public void SetLineWidth(float width)
    {
        lineWidth = width;
    }

    // 색상 변경 — UI 버튼 OnClick에 연결해서 사용
    public void SetLineColor(Color color)
    {
        lineColor = color;
    }

    // R/G/B/A 개별 채널 변경용 — 필요 시 사용
    public void SetLineColorR(float r) => lineColor.r = r;
    public void SetLineColorG(float g) => lineColor.g = g;
    public void SetLineColorB(float b) => lineColor.b = b;
    public void SetLineColorA(float a) => lineColor.a = a;

    // 특정 라인 하나를 풀로 반환 — Eraser에서 호출
    public void RemoveLine(GameObject line)
    {
        if (!lineHandles.TryGetValue(line, out var handle)) return;

        ((IDisposable)handle).Dispose();
        lineHandles.Remove(line);
    }

    // 포인트 목록으로 새 라인을 풀에서 꺼내 생성 — 부분 지우개로 선을 쪼갤 때 사용
    // 생성된 GameObject를 반환해 Eraser가 undo 추적에 활용할 수 있게 함
    public GameObject CreateLine(List<Vector2> linePoints, float width, Color color)
    {
        if (linePoints == null || linePoints.Count < 2) return null;

        GameObject go = PoolManager.Instance.Spawn(linePoolId, Vector3.zero, transform, out PooledObject<GameObject> handle);
        if (go == null) return null;

        var newLR       = go.GetComponent<LineRenderer>();
        var newCollider = go.GetComponent<EdgeCollider2D>();

        newLR.positionCount  = 0;
        newCollider.points   = new Vector2[0];
        newLR.startWidth     = width;
        newLR.endWidth       = width;
        newLR.startColor     = color;
        newLR.endColor       = color;

        newLR.positionCount = linePoints.Count;
        for (int i = 0; i < linePoints.Count; i++)
            newLR.SetPosition(i, linePoints[i]);

        newCollider.points = linePoints.ToArray();
        lineHandles[go]    = handle;
        return go;
    }

    // 그려진 모든 라인을 풀로 반환하고 화면을 초기화
    public void ClearAll()
    {
        foreach (var handle in lineHandles.Values)
            ((IDisposable)handle).Dispose();

        lineHandles.Clear();
    }
}
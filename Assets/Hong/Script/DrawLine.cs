using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Pool;

public class DrowLine : MonoBehaviour
{
    // Resources/Prefabs/Line.prefab 의 파일명이 그대로 id가 됨
    const string linePoolId = "Line";

    LineRenderer lr;
    EdgeCollider2D collider2D;
    List<Vector2> points = new List<Vector2>();

    // 현재 그리고 있는 라인의 풀 핸들
    // Dispose() 호출 시 id 없이도 풀로 자동 반환됨
    PooledObject<GameObject> currentHandle;

    // 완성된 라인 핸들 목록 (ClearAll 호출 시 각각 Dispose)
    List<PooledObject<GameObject>> drawnHandles = new List<PooledObject<GameObject>>();

    // Camera.main은 호출할 때마다 FindWithTag로 탐색하므로 매 프레임 호출하면 비효율적
    // Awake에서 한 번만 캐싱해서 사용
    Camera cam;

    Coroutine drawCoroutine;

    // Awake : PlayerInput 컴포넌트에서 Action을 찾아 콜백 등록
    // PlayerInput의 Behavior가 "Invoke C Sharp Events" 여야 동작함
    void Awake()
    {
        cam = Camera.main;

        var playerInput = GetComponent<PlayerInput>();

        // Click : 마우스 왼쪽 버튼 + 터치 press 바인딩
        var clickAction = playerInput.actions["Click"];
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

    // started 콜백 : 마우스 왼쪽 버튼을 누르는 순간 호출
    // 풀에서 라인 오브젝트를 꺼내 초기화한 뒤 드로우 루프를 시작함
    void OnDrawStart(InputAction.CallbackContext ctx)
    {
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

        Vector2 startPos = cam.ScreenToWorldPoint(Pointer.current.position.ReadValue());
        points.Add(startPos);
        lr.positionCount = 1;
        lr.SetPosition(0, startPos);

        drawCoroutine = StartCoroutine(DrawLoop());
    }

    // 코루틴 : 버튼을 누르고 있는 동안 매 프레임 실행됨
    // yield return null 로 한 프레임씩 대기하며 마우스 위치를 추적함
    // 이전 포인트와 거리가 0.1f 이상일 때만 포인트를 추가해 과도한 포인트 생성을 방지
    IEnumerator DrawLoop()
    {
        while (true)
        {
            Vector2 pos = cam.ScreenToWorldPoint(Pointer.current.position.ReadValue());
            if (Vector2.Distance(points[points.Count - 1], pos) > 0.1f)
            {
                points.Add(pos);
                lr.positionCount++;
                lr.SetPosition(lr.positionCount - 1, pos);
                collider2D.points = points.ToArray();
            }
            yield return null; // 다음 프레임까지 대기
        }
    }

    // canceled 콜백 : 마우스 왼쪽 버튼을 떼는 순간 호출
    // 드로우 루프를 중단하고 완성된 라인을 drawnLines에 보관함
    void OnDrawEnd(InputAction.CallbackContext ctx)
    {
        if (drawCoroutine != null)
        {
            StopCoroutine(drawCoroutine);
            drawCoroutine = null;
        }

        // 완성된 라인 핸들을 목록에 보관 (ClearAll에서 Dispose로 반환)
        drawnHandles.Add(currentHandle);
        currentHandle = default;

        points.Clear();
    }

    // 그려진 모든 라인을 풀로 반환하고 화면을 초기화
    // 지우개 기능이나 전체 초기화 버튼에서 호출하면 됨
    // PooledObject.Dispose()가 내부적으로 Release를 처리하므로 id 불필요
    public void ClearAll()
    {
        // PooledObject<T>는 IDisposable을 명시적으로 구현하므로 캐스팅 필요
        foreach (var handle in drawnHandles)
            ((IDisposable)handle).Dispose();

        drawnHandles.Clear();
    }
}

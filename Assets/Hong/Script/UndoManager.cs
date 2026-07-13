using System;
using System.Collections.Generic;
using UnityEngine;

public class UndoManager : MonoBehaviour
{
    public static UndoManager Instance { get; private set; }

    // 최대 저장 스텝 수 — 초과 시 가장 오래된 스텝 삭제
    [SerializeField] int maxHistory = 30;

    // 각 스텝은 Action 리스트 — 지우개 드래그처럼 여러 조작이 하나의 스텝으로 묶임
    LinkedList<List<Action>> history = new LinkedList<List<Action>>();

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    // 단일 조작을 하나의 스텝으로 기록 — 드로우 완료 시 사용
    public void Record(Action undoAction)
    {
        var step = new List<Action> { undoAction };
        Push(step);
    }

    // 여러 조작을 하나의 스텝으로 묶어 기록 — 지우개 드래그 세션 종료 시 사용
    public void RecordBatch(List<Action> undoActions)
    {
        if (undoActions == null || undoActions.Count == 0) return;
        Push(new List<Action>(undoActions));
    }

    // 가장 최근 스텝을 되돌림 — UI 버튼 OnClick에 연결
    public void Undo()
    {
        if (history.Count == 0) return;

        var step = history.Last.Value;
        history.RemoveLast();

        // 스텝 안의 액션을 역순으로 실행 (나중에 생긴 것부터 되돌림)
        for (int i = step.Count - 1; i >= 0; i--)
            step[i]?.Invoke();
    }

    void Push(List<Action> step)
    {
        history.AddLast(step);
        if (history.Count > maxHistory)
            history.RemoveFirst();
    }
}

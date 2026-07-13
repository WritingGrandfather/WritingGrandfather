using System;
using System.Collections.Generic;
using UnityEngine;

public class UndoManager : MonoBehaviour
{
    public static UndoManager Instance { get; private set; }

    [SerializeField] int maxHistory = 30;

    LinkedList<List<(Action undo, Action redo)>> history     = new LinkedList<List<(Action undo, Action redo)>>();
    LinkedList<List<(Action undo, Action redo)>> redoHistory = new LinkedList<List<(Action undo, Action redo)>>();

    public bool CanUndo => history.Count > 0;
    public bool CanRedo => redoHistory.Count > 0;

    public event Action OnStateChanged;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void Record(Action undoAction, Action redoAction)
    {
        Push(new List<(Action, Action)> { (undoAction, redoAction) });
        redoHistory.Clear();
        OnStateChanged?.Invoke();
    }

    public void RecordBatch(List<(Action undo, Action redo)> actions)
    {
        if (actions == null || actions.Count == 0) return;
        Push(new List<(Action, Action)>(actions));
        redoHistory.Clear();
        OnStateChanged?.Invoke();
    }

    // ClearAll() 등으로 그려진 선을 전부 풀에 반환한 뒤에는, 그 이전 히스토리에
    // 남아있던 undo/redo 델리게이트가 이미 사라진 GameObject를 참조하게 된다 -
    // 단어를 넘어가거나 초기화할 때 반드시 같이 호출해서 History를 비워야 한다.
    public void Clear()
    {
        history.Clear();
        redoHistory.Clear();
        OnStateChanged?.Invoke();
    }

    public void Undo()
    {
        if (!CanUndo) return;

        var step = history.Last.Value;
        history.RemoveLast();

        for (int i = step.Count - 1; i >= 0; i--)
            step[i].undo?.Invoke();

        redoHistory.AddLast(step);
        if (redoHistory.Count > maxHistory)
            redoHistory.RemoveFirst();

        OnStateChanged?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo) return;

        var step = redoHistory.Last.Value;
        redoHistory.RemoveLast();

        for (int i = 0; i < step.Count; i++)
            step[i].redo?.Invoke();

        history.AddLast(step);
        if (history.Count > maxHistory)
            history.RemoveFirst();

        OnStateChanged?.Invoke();
    }

    void Push(List<(Action, Action)> step)
    {
        history.AddLast(step);
        if (history.Count > maxHistory)
            history.RemoveFirst();
    }
}

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

using UnityEngine;
using UnityEngine.UI;

public class UndoRedoButtons : MonoBehaviour
{
    public Button undoButton;
    public Button redoButton;

    void OnEnable()
    {
        UndoManager.Instance.OnStateChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        if (UndoManager.Instance != null)
            UndoManager.Instance.OnStateChanged -= Refresh;
    }

    void Refresh()
    {
        if (undoButton != null) undoButton.interactable = UndoManager.Instance.CanUndo;
        if (redoButton != null) redoButton.interactable = UndoManager.Instance.CanRedo;
    }
}

using UnityEngine;

public class ToolSelector : MonoBehaviour
{
    public DrawLine drawLine;
    public Eraser eraser;

    public GameObject pencilSizePanel;
    public GameObject eraserSizePanel;

    enum Tool { Pencil, Eraser }
    Tool currentTool = Tool.Pencil;

    void Start()
    {
        eraser.Deactivate();
        pencilSizePanel.SetActive(false);
        eraserSizePanel.SetActive(false);
    }

    public void OnPencilButtonClicked()
    {
        if (currentTool == Tool.Pencil)
        {
            // 이미 연필 선택 중 → 크기 패널 토글
            pencilSizePanel.SetActive(!pencilSizePanel.activeSelf);
            eraserSizePanel.SetActive(false);
        }
        else
        {
            currentTool = Tool.Pencil;
            eraser.Deactivate();
            pencilSizePanel.SetActive(false);
            eraserSizePanel.SetActive(false);
        }
    }

    public void OnEraserButtonClicked()
    {
        if (currentTool == Tool.Eraser)
        {
            // 이미 지우개 선택 중 → 크기 패널 토글
            eraserSizePanel.SetActive(!eraserSizePanel.activeSelf);
            pencilSizePanel.SetActive(false);
        }
        else
        {
            currentTool = Tool.Eraser;
            eraser.Activate();
            pencilSizePanel.SetActive(false);
            eraserSizePanel.SetActive(false);
        }
    }

    // 연필 크기 버튼 OnClick에 연결 — 선택 후 패널 자동으로 닫힘
    public void SelectPencilSize(float width)
    {
        drawLine.SetLineWidth(width);
        pencilSizePanel.SetActive(false);
    }

    // 지우개 크기 버튼 OnClick에 연결 — 선택 후 패널 자동으로 닫힘
    public void SelectEraserSize(float radius)
    {
        eraser.SetEraserRadius(radius);
        eraserSizePanel.SetActive(false);
    }
}

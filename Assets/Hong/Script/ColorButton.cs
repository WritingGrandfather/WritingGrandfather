using UnityEngine;
using UnityEngine.UI;

public class ColorButton : MonoBehaviour
{
    public DrawLine drawLine;
    public Color color;

    void Awake()
    {
        if (drawLine == null)
            drawLine = GameObject.FindWithTag("DrawLine").GetComponent<DrawLine>();
        
        // 버튼 이미지를 지정한 색으로 설정해서 어떤 색인지 시각적으로 표시
        GetComponent<Image>().color = color;
        GetComponent<Button>().onClick.AddListener(OnClick);
        if (color.a < 1)
        {
            Debug.LogWarning("알파값이 0입니다.");
        }
    }

    void OnClick()
    {
        drawLine.SetLineColor(color);
    }
}

using UnityEngine;
using UnityEngine.UI;

public class ClickSound : MonoBehaviour
{
    public string soundName = "clickSound";

    void Awake()
    {
        GetComponent<Button>().onClick.AddListener(() =>
            SoundManager.Instance.PlaySfx(soundName));
    }
}

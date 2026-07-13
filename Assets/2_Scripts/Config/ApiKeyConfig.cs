using UnityEngine;

// ⚠️ 진짜 OpenAI API 키는 더 이상 여기 넣지 않는다 — 클라이언트(APK/IPA)에 들어간 값은
// 누구나 뜯어볼 수 있어서, 실제 키를 넣으면 그대로 유출·도용된다.
// 지금은 서버(Firebase Functions functions/index.js의 openaiProxy)가 실제 OpenAI 키를
// 갖고 있고, 여기에는 그 프록시를 호출할 때 증명하는 "공유 비밀값"(PROXY_SHARED_SECRET과
// 같은 문자열)만 넣는다 - 유출돼도 이 값만 바꾸면 무효화되고 OpenAI 계정 자체는 안전하다.
// 자세한 배포/설정 방법은 functions/README.md 참고.
[CreateAssetMenu(fileName = "ApiKeyConfig", menuName = "Config/Api Key Config")]
public class ApiKeyConfig : ScriptableObject
{
    [Tooltip("OpenAI 키가 아니라, 프록시(openaiProxy)의 PROXY_SHARED_SECRET과 동일한 값")]
    [SerializeField] private string apiKey;

    public string ApiKey => apiKey;

    private static ApiKeyConfig instance;

    public static ApiKeyConfig Instance
    {
        get
        {
            if (instance == null)
            {
                instance = Resources.Load<ApiKeyConfig>("ApiKeyConfig");
                if (instance == null)
                {
                    Debug.LogError("ApiKeyConfig.asset not found in Assets/Resources. " +
                        "Create one via Assets > Create > Config > Api Key Config, name it 'ApiKeyConfig', " +
                        "and set your key in the Inspector. This file is gitignored and must be created locally.");
                }
            }
            return instance;
        }
    }
}

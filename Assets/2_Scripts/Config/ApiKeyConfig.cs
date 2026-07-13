using UnityEngine;

[CreateAssetMenu(fileName = "ApiKeyConfig", menuName = "Config/Api Key Config")]
public class ApiKeyConfig : ScriptableObject
{
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

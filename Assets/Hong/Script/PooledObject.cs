using UnityEngine;

public class PooledObject : MonoBehaviour
{
    public string id;

    public void ReturnToPool()
    {
        PoolManager.Instance.Release(id, gameObject);
    }
}

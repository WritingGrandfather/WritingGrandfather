using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

public class obstacle : MonoBehaviour
{
    public string obstacleId;
    public float spawnTime;
    public Transform[] spawnPoint;
    public int startTime = 3;

    public void Start()
    {
        StartCoroutine(ObstacleSpawn());
    }

    public IEnumerator ObstacleSpawn()
    {
        yield return new WaitForSeconds(startTime);
        while (PlayerHp.Instance.HP > 0)
        {
            int rand = Random.Range(0, spawnPoint.Length);
            PoolManager.Instance.Spawn(obstacleId, spawnPoint[rand].position);
            yield return new WaitForSeconds(spawnTime);
        }
    }
}

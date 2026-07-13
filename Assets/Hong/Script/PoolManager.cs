using UnityEngine;
using UnityEngine.Pool;
using System.Collections.Generic;

public class PoolManager : MonoBehaviour
{
    public static PoolManager Instance { get; private set; }

    // Inspector에서 미리 풀 항목을 등록할 때 사용하는 데이터 클래스
    [System.Serializable]
    public class PoolEntry
    {
        public string id;           // 풀을 구분하는 고유 ID
        public GameObject prefab;   // 풀링할 프리팹
        public int defaultCapacity = 10; // 초기 생성 수
        public int maxSize = 100;        // 풀의 최대 크기 (초과 시 Destroy)
    }

    [SerializeField] List<PoolEntry> entries = new List<PoolEntry>();

    // Resources 폴더 기준 자동 로드 경로 (Assets/Resources/ 이하 상대 경로)
    [SerializeField] string resourcesPath = "Prefabs";

    // Resources에서 로드된 프리팹의 기본 풀 설정
    [SerializeField] int resourceDefaultCapacity = 5;
    [SerializeField] int resourceMaxSize = 50;

    // id → ObjectPool 매핑 : 실제 풀 로직을 담당
    Dictionary<string, ObjectPool<GameObject>> pools = new Dictionary<string, ObjectPool<GameObject>>();

    // id → prefab 매핑 : createFunc 내부에서 어떤 프리팹을 Instantiate할지 참조
    Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();

    // GameObject → id 역방향 매핑 : id 없이 Release할 때 어느 풀로 반환할지 조회
    Dictionary<GameObject, string> objectToId = new Dictionary<GameObject, string>();

    // PooledObject.id → prefab 캐시
    // Spawn 시 풀이 없을 때 PooledObject.id로 프리팹을 찾아 자동 등록하는 데 사용
    Dictionary<string, GameObject> prefabCache = new Dictionary<string, GameObject>();

    // Awake : 싱글톤 설정 후 Inspector에 등록된 항목들을 풀로 초기화
    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Resources/Prefabs 폴더의 프리팹을 먼저 자동 등록
        // 프리팹 이름이 id로 사용됨 (예: "LinePrefab" → Spawn("LinePrefab", ...))
        LoadFromResources();

        // Inspector에 수동 등록한 항목 추가 (같은 id면 이미 등록됐다는 경고 출력 후 스킵)
        foreach (var entry in entries)
            Register(entry.id, entry.prefab, entry.defaultCapacity, entry.maxSize);
    }

    // Resources/{resourcesPath} 폴더 안의 모든 프리팹을 풀로 등록
    // PooledObject 컴포넌트가 있으면 그 id를 사용, 없으면 파일명을 id로 사용
    void LoadFromResources()
    {
        GameObject[] loaded = Resources.LoadAll<GameObject>(resourcesPath);

        if (loaded.Length == 0)
        {
            Debug.LogWarning($"[PoolManager] Resources/{resourcesPath} 에서 프리팹을 찾지 못했습니다.");
            return;
        }

        foreach (var prefab in loaded)
        {
            var pooledObj = prefab.GetComponent<PooledObject>();
            string id = (pooledObj != null && !string.IsNullOrEmpty(pooledObj.id))
                ? pooledObj.id
                : prefab.name;

            prefabCache[id] = prefab;
            Register(id, prefab, resourceDefaultCapacity, resourceMaxSize);
        }

        Debug.Log($"[PoolManager] Resources/{resourcesPath} 에서 {loaded.Length}개 프리팹 자동 등록 완료");
    }

    // Register : 런타임 중에 새로운 풀을 동적으로 추가할 때 사용
    // Inspector 등록 외에도 코드에서 직접 호출해 풀을 추가할 수 있음
    public void Register(string id, GameObject prefab, int defaultCapacity = 10, int maxSize = 100)
    {
        if (pools.ContainsKey(id))
        {
            Debug.LogWarning($"[PoolManager] '{id}' 는 이미 등록된 ID입니다.");
            return;
        }

        // Inspector에서 0으로 입력됐을 경우 최솟값으로 보정
        if (defaultCapacity < 1) defaultCapacity = 10;
        if (maxSize < 1) maxSize = 50;

        prefabs[id] = prefab;

        // ObjectPool : Unity 내장 풀 시스템 (UnityEngine.Pool)
        // - createFunc    : 풀이 비었을 때 새 오브젝트를 만드는 방법
        // - actionOnGet   : 풀에서 꺼낼 때 실행 (비활성 → 활성)
        // - actionOnRelease: 풀로 반환할 때 실행 (활성 → 비활성)
        // - actionOnDestroy: maxSize 초과로 풀에 못 넣을 때 정리
        // - collectionCheck: true면 이미 반환된 오브젝트를 또 반환하면 예외 발생 (디버그용)
        pools[id] = new ObjectPool<GameObject>(
            createFunc:      () => Instantiate(prefabs[id]),
            actionOnGet:     obj => obj.SetActive(true),
            // 반환 시 PoolManager(DontDestroyOnLoad) 밑으로 옮긴다 —
            // 씬 오브젝트 밑에 남겨두면 씬 전환 때 파괴돼서 풀에 죽은 참조가 쌓임
            actionOnRelease: obj =>
            {
                if (obj != null)
                {
                    obj.SetActive(false);
                    obj.transform.SetParent(transform, false);
                }
                objectToId.Remove(obj);
            },
            actionOnDestroy: obj => { objectToId.Remove(obj); Destroy(obj); },
            collectionCheck: false,
            defaultCapacity: defaultCapacity,
            maxSize:         maxSize
        );
    }

    // 풀이 없을 때 prefabCache에서 프리팹을 찾아 자동 등록
    // PooledObject.id 또는 파일명으로 등록된 캐시를 조회함
    bool TryAutoRegister(string id)
    {
        if (!prefabCache.TryGetValue(id, out var prefab))
        {
            Debug.LogWarning($"[PoolManager] '{id}' 에 해당하는 풀과 프리팹을 찾지 못했습니다.");
            return false;
        }

        Debug.Log($"[PoolManager] '{id}' 풀이 없어 prefabCache에서 자동 등록합니다.");
        Register(id, prefab, resourceDefaultCapacity, resourceMaxSize);
        return true;
    }

    // Spawn : id에 해당하는 풀에서 오브젝트를 꺼내 원하는 위치에 배치
    // 풀이 없으면 PooledObject.id 기반으로 자동 등록 후 스폰
    public GameObject Spawn(string id, Vector3 position, Transform parent = null)
    {
        if (!pools.TryGetValue(id, out var pool))
        {
            if (!TryAutoRegister(id)) return null;
            pool = pools[id];
        }

        GameObject obj = pool.Get();
        // 씬 전환 등으로 파괴된 잔재가 나오면 새로 생성 (죽은 참조 방어)
        while (obj == null && pool.CountInactive > 0) obj = pool.Get();
        if (obj == null) obj = Instantiate(prefabs[id]);

        objectToId[obj] = id;
        obj.transform.SetParent(parent);
        obj.transform.SetAsLastSibling(); // 재사용 시에도 하이어라키 순서 = 스폰 순서 보장 (획순 판정에 필요)
        obj.transform.position = position;
        return obj;
    }

    // Spawn (PooledObject 오버로드) : out으로 핸들을 받아 사용
    // handle.Dispose() 만 호출하면 Release 없이 자동으로 풀에 반환됨
    // 풀이 없으면 PooledObject.id 기반으로 자동 등록 후 스폰
    public GameObject Spawn(string id, Vector3 position, Transform parent, out PooledObject<GameObject> handle)
    {
        if (!pools.TryGetValue(id, out var pool))
        {
            if (!TryAutoRegister(id))
            {
                handle = default;
                return null;
            }
            pool = pools[id];
        }

        // Get(out T v) : 반환값이 PooledObject<T>(핸들), out 파라미터가 실제 오브젝트
        handle = pool.Get(out GameObject obj);
        // 씬 전환 등으로 파괴된 잔재가 나오면 버리고 다시 꺼낸다 (죽은 참조 방어)
        while (obj == null && pool.CountInactive > 0)
            handle = pool.Get(out obj);
        if (obj == null)
        {
            handle = pool.Get(out obj); // 풀이 비면 createFunc로 새로 생성됨
        }

        objectToId[obj] = id;
        obj.transform.SetParent(parent);
        obj.transform.SetAsLastSibling(); // 재사용 시에도 하이어라키 순서 = 스폰 순서 보장 (획순 판정에 필요)
        obj.transform.position = position;
        return obj;
    }

    // Release : 사용이 끝난 오브젝트를 풀로 반환
    // id가 null이면 objectToId 역방향 딕셔너리에서 자동으로 id를 조회함
    // Destroy 대신 이 함수를 호출해야 풀링이 유지됨
    public void Release(string id, GameObject obj)
    {
        if (id == null)
        {
            // id 없이 오브젝트만 넘겼을 때 역방향 매핑으로 소속 풀 조회
            if (!objectToId.TryGetValue(obj, out id))
            {
                Debug.LogWarning("[PoolManager] 오브젝트에 매핑된 id가 없습니다. Destroy로 처리합니다.");
                Destroy(obj);
                return;
            }
        }

        if (!pools.TryGetValue(id, out var pool))
        {
            Debug.LogWarning($"[PoolManager] '{id}' 에 해당하는 풀이 없습니다.");
            Destroy(obj);
            return;
        }

        pool.Release(obj);
    }
}

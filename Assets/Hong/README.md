# Hong - 드로우 라인 시스템

## 폴더 구조

```
Assets/Hong/
├── InputAction/
│   └── DrawAction.inputactions   ← 인풋 액션 에셋
├── Resources/
│   └── Prefab/
│       └── Line.prefab           ← 라인 프리팹 (PooledObject id: "Line")
├── Scene/
│   └── New Scene 1.unity
└── Script/
    ├── PoolManager.cs
    ├── PooledObject.cs
    ├── DrawLine.cs
    ├── ColorButton.cs
    └── Eraser.cs
```

---

## PoolManager

### 개요
`Resources/Prefab` 폴더의 프리팹을 자동으로 불러와 풀을 생성합니다.  
프리팹에 붙은 `PooledObject` 컴포넌트의 `id`를 키로 사용하며, 컴포넌트가 없으면 파일명을 id로 사용합니다.

### 씬 설정
1. 빈 오브젝트 생성 → 이름 `PoolManager`
2. `PoolManager` 컴포넌트 추가
3. Inspector 설정

| 필드 | 값 | 설명 |
|---|---|---|
| Resources Path | `Prefab` | `Assets/Hong/Resources/` 기준 상대 경로 |
| Resource Default Capacity | `5` | 풀 초기 생성 수 |
| Resource Max Size | `50` | 풀 최대 크기 |

> `Entries` 리스트는 코드 외부에서 수동으로 추가 등록할 때 사용 (선택사항)

### 코드 사용법

```csharp
// 스폰 (id + 위치)
GameObject obj = PoolManager.Instance.Spawn("Line", position);

// 스폰 (PooledObject 핸들 방식 — Dispose()로 자동 반환)
GameObject obj = PoolManager.Instance.Spawn("Line", position, transform, out PooledObject<GameObject> handle);
((IDisposable)handle).Dispose(); // 반환

// 반환 (id 있을 때)
PoolManager.Instance.Release("Line", obj);

// 반환 (id 모를 때 — objectToId 역방향 매핑으로 자동 조회)
PoolManager.Instance.Release(null, obj);
```

### 프리팹 준비 방법
1. 프리팹에 `PooledObject` 컴포넌트 추가
2. `id` 필드에 원하는 id 입력 (예: `Line`)
3. `Assets/Hong/Resources/Prefab/` 안에 저장

---

## DrawLine (DrowLine)

### 개요
마우스 또는 터치로 선을 그리는 컴포넌트입니다.  
PoolManager에서 라인 오브젝트를 꺼내 사용하며, UI 위에서는 드로우가 차단됩니다.

### 씬 설정

#### 필수 컴포넌트 (같은 오브젝트에 추가)
| 컴포넌트 | 설정 |
|---|---|
| `DrowLine` | - |
| `PlayerInput` | Actions: `DrawAction`, Default Map: `Draw`, Behavior: `Invoke C Sharp Events` |

#### 인풋 액션 에셋 경로
```
Assets/Hong/InputAction/DrawAction.inputactions
```

| 액션 맵 | 액션 | 바인딩 |
|---|---|---|
| Draw | Click | `<Mouse>/leftButton`, `<Touchscreen>/touch*/Press` |

#### PoolManager 연동
- `Resources/Prefab/Line.prefab` 의 `PooledObject.id` = `"Line"` 이어야 합니다.
- PoolManager가 씬에 존재해야 합니다.

### 두께 조절 (UI Slider 연결)
1. `UI → Slider` 생성
2. Inspector 설정
   - `Min Value`: `0.01`
   - `Max Value`: `1`
   - `Value`: `0.1`
3. `On Value Changed` → `DrowLine` 오브젝트 → `DrowLine.SetLineWidth`

### 전체 초기화
```csharp
GetComponent<DrowLine>().ClearAll(); // 그려진 라인 전부 풀로 반환
```

### UI 조작 중 드로우 차단
슬라이더 등 UI 위에서 입력 시 자동으로 드로우가 막힙니다. (`EventSystem` 기반)  
별도 설정 불필요.

---

---

## Eraser

### 개요
드래그하면서 닿는 라인을 감지해 풀로 반환하는 지우개 컴포넌트입니다.  
`Activate` / `Deactivate`로 펜 모드와 지우개 모드를 전환하며, 모드 전환 시 DrawLine의 드로우 입력이 자동으로 차단됩니다.

### 씬 설정

#### 필수 컴포넌트 (DrawLine과 같은 오브젝트에 추가)
| 컴포넌트 | 설정 |
|---|---|
| `Eraser` | `Draw Line` 필드에 DrawLine 오브젝트 연결 |

#### 펜 / 지우개 전환 버튼
| 버튼 | On Click 연결 |
|---|---|
| 펜 버튼 | `Eraser.Deactivate` |
| 지우개 버튼 | `Eraser.Activate` |

### 지우개 크기 조절 (UI Slider 연결)
1. `UI → Slider` 생성
2. Inspector 설정
   - `Min Value`: `0.05`
   - `Max Value`: `1`
   - `Value`: `0.2`
3. `On Value Changed` → `Eraser` 오브젝트 → `Eraser.SetEraserRadius`

### 동작 방식
- 지우개 모드에서 드래그하면 `eraserRadius` 반경 안의 `EdgeCollider2D`를 감지
- 감지된 라인을 `DrawLine.RemoveLine`으로 풀에 반환
- UI 위에서는 지우개도 동작하지 않음

---

## 씬 오브젝트 구성 예시

```
Scene
├── Main Camera          (태그: MainCamera)
├── PoolManager          (PoolManager 컴포넌트)
├── DrawLine             (DrowLine + PlayerInput + Eraser 컴포넌트)
└── Canvas
    ├── PenButton        (On Click → Eraser.Deactivate)
    ├── EraserButton     (On Click → Eraser.Activate)
    ├── WidthSlider      (On Value Changed → DrowLine.SetLineWidth)
    ├── EraserSlider     (On Value Changed → Eraser.SetEraserRadius)
    └── ColorButtons     (ColorButton 컴포넌트, On Click → DrowLine.SetLineColor)
```

---

## Script Execution Order 설정 (권장)
`Edit → Project Settings → Script Execution Order`  
PoolManager를 DrowLine보다 위에 추가해 Awake 실행 순서를 보장합니다.

```
PoolManager   : -100
DrowLine      :    0  (기본값)
```

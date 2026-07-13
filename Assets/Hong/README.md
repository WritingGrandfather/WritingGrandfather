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
    ├── Eraser.cs
    ├── UndoManager.cs
    └── DrawCursor.cs
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

### 색상 변경 (ColorButton)
1. `UI → Button` 생성
2. `ColorButton` 컴포넌트 추가
3. Inspector에서 `Draw Line` 연결, `Color` 설정
4. 버튼 이미지가 지정 색으로 자동 변경됨

### 전체 초기화
```csharp
GetComponent<DrowLine>().ClearAll(); // 그려진 라인 전부 풀로 반환
```

### UI 조작 중 드로우 차단
슬라이더 등 UI 위에서 입력 시 자동으로 드로우가 막힙니다. (`EventSystem` 기반)  
별도 설정 불필요.

---

## Eraser

### 개요
드래그한 경로에서 라인의 닿은 부분만 잘라내는 부분 지우개입니다.  
- 지우개가 지나간 구간만 제거되고, 나머지는 새 라인으로 유지됩니다.
- 빠르게 드래그해도 프레임 사이를 보간해 끊김 없이 지워집니다.
- `Activate` / `Deactivate`로 펜 모드와 지우개 모드를 전환합니다.

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
- 드래그 시 이전 프레임 위치와 현재 위치 사이를 `eraserRadius * 0.5f` 간격으로 보간 샘플링
- 각 샘플 위치에서 `Physics2D.OverlapCircleAll`로 `EdgeCollider2D` 감지
- 지우개 원과 교차하는 구간만 잘라내고 나머지는 새 라인으로 재생성
- 총 길이 `0.05f` 미만의 미세 잔재 구간은 자동 필터링
- UI 위에서는 동작하지 않음

---

## UndoManager

### 개요
드로우와 지우개 조작을 되돌리는 undo 시스템입니다.  
- 선 하나 그리기 = undo 스텝 1개
- 지우개 드래그 한 번 = undo 스텝 1개 (드래그 중 여러 선을 지워도 하나로 묶임)

### 씬 설정
1. 빈 오브젝트 생성 → `UndoManager` 컴포넌트 추가
2. `Max History` : 저장할 최대 undo 스텝 수 (기본값 `30`)
3. UI 버튼 → `On Click` → `UndoManager.Undo`

---

## DrawCursor

### 개요
마우스/터치 위치에 현재 범위를 회색 반투명 원으로 표시합니다.  
- 드로우 모드 : 선 두께(`lineWidth * 0.5f`) 기준
- 지우개 모드 : 지우개 반지름(`eraserRadius`) 기준

### 씬 설정
1. 빈 오브젝트 생성 → 이름 `DrawCursor`
2. `DrawCursor` 컴포넌트 추가
3. Inspector 연결
   - `Draw Line` → DrawLine 오브젝트
   - `Eraser` → Eraser 컴포넌트 (DrawLine 오브젝트)
4. 오브젝트 `Layer` → `Ignore Raycast` (지우개 감지에 영향주지 않도록)

---

## 씬 오브젝트 구성 예시

```
Scene
├── Main Camera          (태그: MainCamera)
├── PoolManager          (PoolManager 컴포넌트)
├── DrawLine             (DrowLine + PlayerInput + Eraser 컴포넌트)
├── DrawCursor           (DrawCursor 컴포넌트)
└── Canvas
    ├── PenButton        (On Click → Eraser.Deactivate)
    ├── EraserButton     (On Click → Eraser.Activate)
    ├── UndoButton       (On Click → UndoManager.Undo)
    ├── ClearButton      (On Click → DrowLine.ClearAll)
    ├── WidthSlider      (On Value Changed → DrowLine.SetLineWidth)
    ├── EraserSlider     (On Value Changed → Eraser.SetEraserRadius)
    └── ColorButtons     (ColorButton 컴포넌트, 색상별 버튼)
```

---

## Script Execution Order 설정 (권장)
`Edit → Project Settings → Script Execution Order`  
PoolManager를 DrowLine보다 위에 추가해 Awake 실행 순서를 보장합니다.

```
PoolManager   : -100
DrowLine      :    0  (기본값)
```

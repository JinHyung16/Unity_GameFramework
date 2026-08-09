# GameFramework

새 게임을 시작할 때 가져다 쓰는 공용 코드다. **UI 창 관리**와 **데이터 로드** 두 부분으로 나뉜다.

## 설치

- `Assets/` 안의 내용을 Unity 프로젝트의 `Assets/`에 넣는다.
- `_DataExporter/`는 프로젝트 루트(= `Assets` 바깥)에 둔다. Assets 밖이라 Unity가 무시한다.

## 전체 구조

```
프로젝트 루트/
├─ Assets/
│  ├─ Scripts/
│  │  ├─ UIFramework/          UI 창 관리 + 프리팹 풀링 + 재활용 스크롤 (그대로 사용)
│  │  ├─ DataLoader/           데이터 로드 코드
│  │  │  ├─ Base/              공용 부모 코드 (건드리지 않음)
│  │  │  ├─ Editor/            에디터 도구
│  │  │  ├─ Generated/         도구가 자동 생성 (건드리지 않음)
│  │  │  └─ Containers/        표마다 직접 만드는 콘크리트 컨테이너
│  │  ├─ Game/Core/            GameRoot (컨테이너 접근 루트)
│  │  └─ Utility/              공용 확장 메서드 (Game_Utility)
│  ├─ GameData/                변환된 JSON (도구가 자동 생성)
│  └─ Resources/               창·컴포넌트 프리팹을 여기에 둔다
│
└─ _DataExporter/              엑셀 변환 도구 (Assets 밖)
   └─ GameData/                원본 엑셀을 여기에 넣는다
```

한눈에 보는 데이터 흐름:

```
_DataExporter/GameData/*.xlsx        ← 원본 엑셀을 직접 넣음
        │   run_win.bat 실행
        ▼
Assets/GameData/*.json                             JSON 결과
Assets/Scripts/DataLoader/Generated/*.cs           C# 클래스 결과 (+ Containers.Generated.cs)
Assets/Scripts/Game/Core/GameRoot.Generated.cs     GameRoot 프로퍼티 (Containers/ 스캔 결과)
```

---

## 1. UIFramework — UI 창 관리

**역할:** 창(Window)을 열고 닫고, 겹칠 때 뜨는 순서를 자동으로 맞춘다.

창은 프리팹으로 만들고, 코드에서는 이름표(Key)로 부른다.

### 준비

1. 창 프리팹을 `Assets/.../Resources/` 폴더 안에 만든다. (`Resources` 폴더는 직접 만들어야 한다)
2. 프리팹의 루트에 `Canvas`와 내가 만든 창 스크립트를 붙인다.
3. 창의 이름표에 넣는 문자열은 **Resources 폴더 기준 경로**로 맞춘다.
   예) 프리팹이 `Resources/UI/TitleWindow.prefab`이면 이름표는 `"UI/TitleWindow"`.

### 사용법

창 클래스를 만든다. 이름표는 `static readonly`로 하나만 둔다.

```csharp
using Game_UIFramework;

public class TitleWindow : BaseWindow
{
    public static readonly WindowKey<TitleWindow> Key =
        new WindowKey<TitleWindow>("UI/TitleWindow");   // Resources 기준 경로

    protected override void OnOpening() { }   // 창이 열릴 때 한 번 호출
}
```

관리자(Management)에서 등록하고 연다. 등록은 `AddWindows()`에서만 한다.

```csharp
using Game_UIFramework;

public class TitleManagement : BaseManagement
{
    protected override void AddWindows()
    {
        RegisterWindow(TitleWindow.Key, WindowType.Normal);
    }

    public void ShowTitle() => OpenWindow(TitleWindow.Key);
}
```

매 프레임 갱신이 필요하면 `IWindowUpdate`를 붙인다. 창이 열려 있는 동안만 호출돼 낭비가 없다.

```csharp
public class HudWindow : BaseWindow, IWindowUpdate
{
    public static readonly WindowKey<HudWindow> Key =
        new WindowKey<HudWindow>("UI/HudWindow");

    public void OnUpdate(float deltaTime) { }
    public void OnFixedUpdate(float fixedDeltaTime) { }
}
```

### 알아둘 점

- 창 종류(`WindowType`)에 따라 뜨는 높이가 자동으로 정해진다.
  **HUD → Normal → Popup → Modal → GlobalPopup** 순으로 위에 올라온다.
- Modal 창은 `Close()`로 닫히지 않는다. 강제로 닫으려면 `ForcedClose()`를 쓴다.
- 프리팹은 반드시 `Resources` 폴더 안에 있어야 하고, 경로가 이름표 문자열과 같아야 찾는다.

### Window가 아닌 UI 조각 — PrefabAuto (풀링)

창이 아닌 재사용 컴포넌트(리스트 아이템 등)는 `BaseComponent`를 상속하고 `PrefabAuto`로 만든다.
풀에서 꺼내고(Create) 풀로 돌려보낸다(Release). 상태 초기화는 `IPoolable.OnDespawn()`에서 한다 (이벤트 구독 해제 필수).

```csharp
public class ItemComponent : BaseComponent
{
    public static readonly PrefabAuto<ItemComponent> Auto =
        PrefabAuto.Get<ItemComponent>("UI/Components/ItemComponent");   // Resources 기준 경로
}

var comp = ItemComponent.Auto.CreateForUI(parent);   // 생성 (UI용)
ItemComponent.Auto.Release(comp);                    // 반환
```

### 긴 목록 — RecyclableScrollView

항목이 많은 스크롤은 `Scroll/`의 `RecyclableScrollView`를 쓴다. 보이는 만큼만 만들어 재활용한다.
데이터 소스(`IRecyclableScrollDataSource`)를 구현해 물리면 된다.

### 폴더 안에 뭐가 있나

```
UIFramework/
├─ BaseComponent.cs        모든 UI 조각의 부모 (Transform 캐싱)
├─ Window/
│  ├─ BaseWindow.cs        모든 창의 부모 (열기·닫기·상태)
│  ├─ WindowManagement.cs  창을 실제로 열고 닫는 관리자 (씬당 하나)
│  ├─ BaseManagement.cs    화면별로 쓸 창을 등록하는 부모
│  ├─ WindowKey.cs         창을 구분하는 이름표
│  ├─ WindowFactory.cs     프리팹을 불러와 창을 만드는 부분
│  ├─ WindowType.cs        창 종류 (Normal / Popup / HUD / GlobalPopup / Modal)
│  ├─ WindowStateType.cs   창 상태 + 닫기 방식 (CloseType)
│  ├─ IWindowUpdate.cs     매 프레임 갱신이 필요한 창이 붙이는 선택 기능
│  └─ Singleton/           씬당 하나만 존재하는 관리자용 부모
├─ Prefab/                 비Window 컴포넌트 풀링 (PrefabAuto / PrefabLoader / IPoolable)
├─ Scroll/                 재활용 스크롤 뷰 (RecyclableScrollView)
└─ Examples/               사용 예시 코드
```

※ `Prefab/`의 `PrefabAuto`는 `Utility/TransformExtensions.cs`(Game_Utility)를 쓴다. 같이 복사한다.

---

## 2. DataLoader — 데이터 로드

**역할:** `Assets/GameData/`의 JSON 표를 읽어, 게임에서 꺼내 쓸 수 있게 담아둔다.

데이터는 **그릇(Container)** 단위로 담긴다. 그릇을 만든 표만 불러오고, 안 만든 표는 메모리도 쓰지 않는다.

### 사용법

엑셀을 변환하면 표마다 아래가 `Generated/`에 자동으로 생긴다. (직접 수정하지 않는다)

- `<표이름>.cs` — 한 줄(row)을 담는 클래스
- `Containers.Generated.cs` — 표마다 컨테이너 부모를 한 파일에 병합 생성
  (`XxxCodeContainer` / `XxxDictionaryContainer` / `XxxDictionaryGroupContainer`)

`Containers/`에 실제로 쓸 그릇을 직접 만든다. **사용할 표만** 만들고, 맞는 부모를 상속한다.

```csharp
using Game_DataLoader;

// Code 컬럼이 있는 표 — Get("code") / GetGroup("code")
public class ModeDataContainer : ModeDataCodeContainer { }

// Id만 있는 표 (1:1)
public class StageThemeDataContainer : StageThemeDataDictionaryContainer { }

// Id 하나에 값 여러 개 (1:N)
public class DropDataContainer : DropDataDictionaryGroupContainer { }
```

콘크리트를 만들거나 지운 뒤 `run_win.bat`을 다시 돌리면 (엑셀 변경 없어도 됨)
`Containers/`를 스캔해 `GameRoot.Generated.cs`에 접근 프로퍼티를 자동 생성한다.

```csharp
await DataManager.Instance.InitializeAsync();   // 시작 시 한 번

var mode  = GameRoot.Instance.ModeDataContainer.Get("normal");   // Code 단건
var theme = GameRoot.Instance.StageThemeDataContainer.Get(1);    // int Id
var all   = GameRoot.Instance.ModeDataContainer.AllValues;
```

`DataManager.Instance.GetContainer<T>()` 직접 호출 대신 GameRoot 프로퍼티를 쓴다.

### Id 외 키로 조회하기 (보조 자료구조)

`Id`/`Code`가 아닌 컬럼으로 조회해야 하면 콘크리트 컨테이너에서
`SubCollectionConstructor` / `SubCollectionAdd`를 재정의해 자료구조를 직접 만든다.
로드마다 새로 만들어 채우므로 재로드 시 중복 누적이 없다.

### 알아둘 점

- 표에는 `Id` 열(`int!`, 1부터 순번)이 있어야 하고, 그 값이 표의 키다.
- 세이브·로직이 참조하는 문자열 식별자는 `Code` 열(`string!`)로 따로 둔다.
- 직접 만드는 그릇은 자동 생성 코드와 같은 `Game_DataLoader`를 쓴다.
- 값 검사나 표끼리 연결하는 코드는 `Generated/`가 아니라 `Containers/` 그릇에 넣는다.
  (`Validate`, `AfterAllTableLoaded`)
- JSON은 Addressables 라벨 `game_data`로 묶여 있어야 불러온다. (변환 도구가 알아서 붙인다)
- JSON 파싱에 Newtonsoft.Json(com.unity.nuget.newtonsoft-json) 패키지가 필요하다.

### 폴더 안에 뭐가 있나

```
DataLoader/
├─ DataManager.cs        시작 시 JSON을 모두 불러와 그릇에 채우는 관리자
├─ Base/                 그릇 부모 (건드리지 않음)
│  ├─ DictionaryContainer.cs        1:1  — Get(키)로 값 하나
│  ├─ DictionaryGroupContainer.cs   1:N  — Get(키)로 값 목록
│  └─ ListContainer.cs              순서대로 담는 목록
├─ Editor/
│  ├─ GameEnumGenerator.cs      _Enum 표를 읽어 enum 코드 생성
│  └─ LiveDataEditorWindow.cs   에디터에서 데이터 표를 보고 수정
├─ Generated/            도구가 자동 생성 (건드리지 않음)
└─ Containers/           표마다 쓸 그릇을 직접 만드는 곳
```

---

## 3. _DataExporter — 엑셀 변환 도구

**역할:** 엑셀을 읽어 JSON과 C# 클래스를 만든다. Node.js(14 이상)가 필요하다.

### 사용법

1. 엑셀 파일을 `_DataExporter/GameData/`에 넣는다. **시트 한 장 = 표 하나**다.
2. 각 시트는 아래 형식으로 채운다.

   | 행 | 내용 |
   |----|------|
   | 1행 | 자료형 (int, float, bool, string, intArray, stringArray, enum 이름 등) |
   | 2행 | 열 이름 (PascalCase) |
   | 3행부터 | 실제 데이터 |

3. `run_win.bat`을 더블클릭한다. 처음 한 번은 필요한 패키지를 자동 설치한다.
4. 결과가 아래에 생긴다.
   - JSON → `Assets/GameData/`
   - C# 클래스 → `Assets/Scripts/DataLoader/Generated/` (+ `Containers.Generated.cs`)
   - GameRoot 프로퍼티 → `Assets/Scripts/Game/Core/GameRoot.Generated.cs`

### 시트 컨벤션

- `Id`는 무조건 `int!` — 1부터의 순번이고 표의 키다.
- 문자열 논리 식별자는 `Code` 열(`string!`)로 따로 둔다. 다른 표를 가리키는 참조 열은
  `Code` 값을 담고 이름에 `~Code`/`~Codes`를 쓴다. (`~Id`는 int로 오해되니 금지)
- 필수 열은 자료형 뒤에 `!`를 붙인다. (`int!`, `string!`) — 빈 행이 있으면 변환이 에러를 낸다.
- 배열은 `기본자료형 + Array`(`intArray`)이고 셀 값은 쉼표 구분(`1,2,3`)만 허용한다.
  요소 안에 쉼표가 필요한 데이터는 배열이 아니라 1:N 시트로 정규화한다.
- enum 열은 먼저 `_Enum` 엑셀에 enum을 추가하고, 열의 자료형 자리에 그 enum 이름을 적는다.
  enum 이름은 `E` 접두사 금지, `~Type` 접미사. (예: `StatType`) — 도구가 자동 정규화하고 경고를 낸다.

### 알아둘 점

- 시트 이름이 곧 표 이름이자 클래스 이름이자 JSON 파일 이름이 된다.
- 이름이 `_`로 시작하는 시트나 열은 변환하지 않는다. (임시/메모용으로 쓰면 된다)
- 도구는 클래스와 컨테이너 부모까지만 만든다. 실제로 쓸 그릇은 `DataLoader/Containers/`에 직접 만든다.
- 데이터 작성이 끝나면 항상 `run_win.bat`을 다시 실행해 JSON/C#을 재생성한다.

### 폴더 안에 뭐가 있나

```
_DataExporter/
├─ run_win.bat                  실행 버튼 (더블클릭)
├─ install_node_packages_win.bat  필요한 패키지 수동 설치용
├─ smart_exporter.js            엑셀 → JSON 변환 본체
├─ class_generator.js           JSON에 맞는 C# 클래스 생성
├─ config.json                  제외할 시트/열 등 설정
└─ GameData/                    원본 엑셀을 넣는 곳
```

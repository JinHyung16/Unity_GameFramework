# GameFramework

새 Unity 프로젝트에 그대로 얹어 쓰는 공용 코드. 두 축으로 구성된다.

| 모듈 | 역할 |
|------|------|
| **UIFramework** | 창(Window) 생명주기·깊이 관리, 프리팹 풀링, 재활용 스크롤 |
| **DataLoader** + **_DataExporter** | 엑셀·JS·Python → JSON + 스키마 → C# 코드 생성 → 런타임 로드 |

## Requirements

| 항목 | 용도 |
|------|------|
| Unity **2022.2+** | `FindAnyObjectByType`, C# 9 `new()` |
| `com.unity.ugui` | `UnityEngine.UI` — `BaseWindow` · `BaseComponent` · `RecyclableScrollView`. Unity 6 에서는 TextMeshPro 도 여기 포함된다 |
| `com.unity.nuget.newtonsoft-json` | JSON 역직렬화 |
| `com.unity.addressables` | `DataManager` 의 JSON 로드 |
| Node.js **14+** | `_DataExporter` 실행 (Unity 외부) |
| Python **3.x** *(선택)* | `.py` 데이터 파일을 쓸 때만. 없으면 `.py` 만 건너뛰고 나머지는 변환된다 |

동작을 확인한 조합은 Unity `6000.3.13f1` · ugui `2.0.0` · newtonsoft-json `3.2.1` · addressables `2.3.16` 이다.

## Install

1. `Assets/` 내용을 프로젝트 `Assets/`에 복사한다.
2. `_DataExporter/`는 프로젝트 루트(`Assets` 바깥)에 둔다 — Unity가 무시한다.
3. [Addressables 설정](#addressables-설정)을 한 번 해준다. 안 하면 데이터가 로드되지 않는다.

## 디렉터리 구조

```
프로젝트 루트/
├─ Assets/
│  ├─ GameData/                       (생성) 변환된 JSON — Addressables 대상
│  ├─ GameDataSchema/                 (생성) _Schema.json — 에디터 전용
│  ├─ Resources/                      창·컴포넌트 프리팹 (직접 생성)
│  └─ Scripts/
│     ├─ UIFramework/
│     │  ├─ BaseComponent.cs          모든 UI 조각의 부모 (Transform 캐싱)
│     │  ├─ Window/                   BaseWindow · WindowManagement · BaseManagement · WindowKey
│     │  ├─ Prefab/                   PrefabAuto · PrefabLoader · IPoolable
│     │  ├─ Scroll/                   RecyclableScrollView
│     │  └─ Examples/                 사용 예시
│     ├─ DataLoader/
│     │  ├─ DataManager.cs            JSON 일괄 로드 → 컨테이너 주입
│     │  ├─ Base/                     컨테이너 부모 — 수정 금지
│     │  ├─ Containers/               표별 콘크리트 컨테이너 — 직접 작성
│     │  ├─ Generated/                (생성) 데이터 클래스 + Containers.Generated.cs
│     │  ├─ GameEnum.cs               (생성) _Enum 정의 기준 enum
│     │  └─ Editor/                   DbGenerator · Live Data Editor
│     ├─ Game/Core/                   GameRoot (컨테이너 접근 루트)
│     └─ Utility/                     Game_Utility 확장 메서드
│
└─ _DataExporter/                     데이터 변환 도구
   ├─ run_win.bat                     실행 진입점
   ├─ smart_exporter.js               소스 로딩 · 파싱 → JSON + 스키마  (SOURCE_EXTENSIONS 여기)
   ├─ enum_name.js                    enum 이름 정규화 규칙
   ├─ config.json                     표/열 제외 규칙
   └─ GameData/                       원본 .xlsx · .js · .py
```

`(생성)` 표시된 경로는 도구가 덮어쓴다. 직접 수정하지 않는다. `Assets/Resources/` 와 `Assets/GameData/` 는 저장소에 없다 — 앞은 직접 만들고, 뒤는 첫 변환 때 도구가 만든다.

| 네임스페이스 | 위치 | 파일 수 |
|------------|------|--------|
| `Game_DataLoader` | `DataLoader/` — 생성되는 데이터 클래스와 컨테이너도 여기에 속한다 | 10 |
| `Game_UIFramework` | `UIFramework/` | 29 |
| `Game_Core` | `Game/Core/` — `GameRoot` | 1 |
| `Game_Utility` | `Utility/` | 1 |

---

## 데이터 파이프라인

작업이 두 단계로 갈린다. **기획자는 JSON까지, 프로그래머가 C#을 만든다.**

```
_DataExporter/GameData/*.xlsx  *.js  *.py       기획자가 편집
        │
        │  run_win.bat                          기획자. Node 만 있으면 된다
        ▼
Assets/GameData/*.json                          런타임 데이터
Assets/GameData/_Enum.json                      enum 정의 (참고용)
Assets/GameDataSchema/_Schema.json              표별 컬럼 · 자료형 + enum
        │
        │  Unity: Tools > GameData > DB Generate  (Ctrl+G)    프로그래머
        ▼
Assets/Scripts/DataLoader/Generated/*.cs                     데이터 클래스
Assets/Scripts/DataLoader/Generated/Containers.Generated.cs  컨테이너 추상 부모
Assets/Scripts/DataLoader/GameEnum.cs                        Game.GameEnum.XxxType
Assets/Scripts/Game/Core/GameRoot.Generated.cs               컨테이너 접근 프로퍼티
```

`run_win.bat` 은 C# 을 한 줄도 만들지 않는다. 기획자가 올리는 것은 원본 파일과 `Assets/GameData/`, `Assets/GameDataSchema/` 뿐이다.

### 왜 스키마 파일이 따로 필요한가

JSON 에는 자료형이 남지 않는다. 값만 있으면 `stringArray` 인지 `intArray` 인지, `string` 인지 enum 인지, 필수(`!`) 인지 구분할 수 없다. 그래서 엑셀 2행의 자료형을 `_Schema.json` 으로 따로 내보내고, Unity 쪽 생성기가 그것만 읽는다.

원본 dfw 는 이 역할을 `.graphql` 파일 257개가 맡는다. 서버와 계약을 공유해야 해서 손으로 관리하지만, 이 프레임워크는 서버가 없으므로 엑셀에서 자동 추출한다.

### DB Generate

Unity 메뉴 **Tools > GameData > DB Generate** 또는 **Ctrl+G**.

- `_Schema.json` 이 없으면 에러 로그를 남기고 아무것도 만들지 않는다. `run_win.bat` 을 먼저 돌린다.
- 내용이 같은 파일은 다시 쓰지 않는다.
- 스키마에서 사라진 표의 `Generated/*.cs` 는 지운다.
- `Containers/` 를 스캔해 `GameRoot.Generated.cs` 를 갱신한다. 콘크리트 컨테이너를 추가·삭제한 뒤에도 다시 실행한다.

### 원본 파일 — 위치와 확장자

모든 원본은 **`_DataExporter/GameData/`** 에 넣는다. 하위 폴더는 스캔하지 않는다.

| 확장자 | 용도 | 비고 |
|--------|------|------|
| `.xlsx` | 기획자 주력 | 시트 1장 = 표 1개 |
| `.js` | 계산으로 생성하는 표 | 별도 설치 불필요 |
| `.py` | 계산으로 생성하는 표 | 로컬 Python 필요 |

읽을 확장자는 [smart_exporter.js](_DataExporter/smart_exporter.js) 최상단에서 켜고 끈다.

```js
// ─────────────────────────────────────────────────────────────────────────────
// 이 부분에 Json 변환할 파일 확장자명 넣으세요.
//
// 넣을 수 있는 확장자 (아래 SOURCE_LOADERS에 로더가 있는 것만):
//
//   '.xlsx'   엑셀      시트 1장 = 표 1개
//   '.js'     Node      module.exports = { 표이름: [[컬럼명..], [자료형..], [값..]] }
//   '.py'     Python    TABLES      = { "표이름": [[컬럼명..], [자료형..], [값..]] }
//                       ※ 로컬에 python 필요. 없으면 .py만 건너뛰고 나머지는 그대로 변환된다
// ─────────────────────────────────────────────────────────────────────────────
const SOURCE_EXTENSIONS = ['.xlsx', '.js', '.py'];
```

- 여기서 뺀 확장자는 `GameData/`에 파일이 있어도 **아예 읽지 않는다.** 예를 들어 `['.xlsx']`만 남기면 엑셀만 변환된다.
- 이 배열은 **켜고 끄는 스위치**지 아무 확장자나 추가하는 곳이 아니다. 로더가 없는 확장자(예: `.csv`)를 적으면 시작할 때 경고하고 무시한다.

```
[WARN] SOURCE_EXTENSIONS에 로더가 없는 확장자가 있어 무시합니다: .csv (사용 가능: .xlsx, .js, .py)
```

- 새 포맷을 지원하려면 `SOURCE_LOADERS`에 한 줄 추가하고 로더 메서드를 만든다. → [확장자 추가하기](#코드-생성-진입점)

### 표 규칙 — 확장자와 무관하게 동일

**표 1개 = 컬럼명 → 자료형 → 값의 3단 구조.** 표 이름이 곧 클래스명이자 JSON 파일명이다.

| 행 | 내용 | 예 |
|----|------|-----|
| 1행 | 열 이름 (PascalCase) | `Id` · `Code` · `Atk` |
| 2행 | 자료형 | `int!` · `string!` · `float` |
| 3행~ | 데이터 | `1` · `knight` · `10.5` |

> ⚠️ **1행이 열 이름, 2행이 자료형이다.** 순서를 바꾸면 전부 `string`으로 떨어진다.

**xlsx** — 시트명이 표 이름

| | A | B | C |
|---|---|---|---|
| **1** | `Id` | `Code` | `Atk` |
| **2** | `int!` | `string!` | `float` |
| **3** | `1` | `knight` | `10.5` |

**js** — `module.exports`의 키가 표 이름. 한 파일에 표 여러 개를 담을 수 있다(= 워크북의 시트 여러 장).

```js
module.exports = {
  HeroData: [
    ['Id',   'Code',    'Atk'  ],
    ['int!', 'string!', 'float'],
    [1,      'knight',  10.5   ],
  ],
};
```

**py** — `TABLES` 딕셔너리의 키가 표 이름

```python
TABLES = {
    "HeroData": [
        ["Id",   "Code",    "Atk"  ],
        ["int!", "string!", "float"],
        [1,      "knight",  10.5   ],
    ],
}
```

셋 다 결과가 완전히 동일하다. 자료형 표기·키 규칙·`!` 필수 검사·`_` 접두사 제외가 전부 같게 적용된다.

코드 포맷의 쓸모는 **계산으로 표를 만들 수 있다**는 점이다. 밸런스 곡선을 상수로 관리하고 재실행하면 전체가 다시 생성된다.

```js
const rows = [
  ['Id',   'Level', 'RequiredExp', 'Hp'  ],
  ['int!', 'int!',  'int!',        'int!'],
];
for (let lv = 1; lv <= 100; lv++) {
  rows.push([lv, lv, Math.floor(100 * Math.pow(1.15, lv - 1)), 50 + lv * 12]);
}
module.exports = { LevelData: rows };
```

> `.js`는 `require()`로, `.py`는 서브프로세스로 **실제 실행된다.** 외부에서 받은 파일을 그대로 `GameData/`에 넣지 않는다.

### 표 작성 규칙

| 규칙 | 내용 |
|------|------|
| 키 | `Id`(`int!`, 1부터 순번)가 키다. 없으면 `Key` 열, 그것도 없으면 맨 왼쪽 열. `Id`와 `Key`를 같이 두지 않는다 |
| 논리 식별자 | 세이브·로직이 참조하는 문자열은 `Code`(`string!`). 타 표 참조 열은 `~Code`/`~Codes` (`~Id`는 금지 — int로 오해된다) |
| 필수 | 자료형 뒤 `!` (`int!`). 빈 값이면 에러 |
| 배열 | `intArray` · `floatArray` · `stringArray`, 셀은 쉼표 구분(`1,2,3`). 요소에 쉼표가 필요하면 배열 대신 1:N 표로 정규화 |
| enum | 아래 `_Enum` 참고. 자료형 칸에 enum 이름만 적는다. `E` 접두사 금지, `~Type` 접미사 |
| 제외 | `_`로 시작하는 표·열은 변환하지 않는다 (`_Enum` 파일은 예외) |
| 자료형 | `int` `long` `float` `double` `bool` `string` · `intArray` `floatArray` `stringArray` · enum 이름 |

### `_Enum` — enum 정의만 규약이 다르다

enum은 3단 구조가 아니라 **열 1개 = enum 1개**(헤더 = 이름, 아래 = 멤버)다. 값은 등장 순서대로 `0, 1, 2…`가 배정되므로 **중간에 멤버를 끼워 넣으면 기존 데이터가 밀린다.** 추가는 항상 끝에 한다.

**xlsx** — `_Enum.xlsx`

| | A | B |
|---|---|---|
| **1** | `StatType` | `CurrencyType` |
| **2** | `Attack` | `Gold` |
| **3** | `Defense` | `Gem` |

**js / py** — 배열이 아니라 객체/딕셔너리로 쓴다. 도구가 위 표 형태로 변환한다.

```js
// _Enum.js
module.exports = {
  _Enum: {
    StatType:     ['Attack', 'Defense', 'Speed'],
    CurrencyType: ['Gold', 'Gem'],
  },
};
```

```python
# _Enum.py
TABLES = {
    "_Enum": {
        "StatType":     ["Attack", "Defense", "Speed"],
        "CurrencyType": ["Gold", "Gem"],
    },
}
```

정의된 enum은 어떤 포맷의 표에서든 자료형 칸에 이름만 적어 쓸 수 있다(`.js`에서 정의하고 `.py` 표에서 참조해도 된다). `Game.GameEnum.StatType`으로 생성된다.

### 컨테이너 작성

생성기는 추상 부모까지만 만든다. **실제로 쓸 표만** `Containers/`에 콘크리트로 선언한다. 선언하지 않은 표는 로드되지 않는다.

```csharp
using Game_DataLoader;

public class ModeDataContainer       : ModeDataCodeContainer { }             // Code 조회 (int 키 + Code 열)
public class StageThemeDataContainer : StageThemeDataDictionaryContainer { } // 1:1
public class DropDataContainer       : DropDataDictionaryGroupContainer { }  // 1:N
```

콘크리트를 추가·삭제한 뒤 **Ctrl+G**(DB Generate)를 다시 누르면 `GameRoot.Generated.cs`가 갱신된다. 데이터가 바뀌지 않았어도 된다.

```csharp
await DataManager.Instance.InitializeAsync();                     // 시작 시 1회

var mode  = GameRoot.Instance.ModeDataContainer.Get("normal");    // Code 단건
var theme = GameRoot.Instance.StageThemeDataContainer.Get(1);     // int 키
var all   = GameRoot.Instance.ModeDataContainer.AllValues;
```

- 컨테이너를 만든 표만 로드된다. 안 만든 표는 메모리를 쓰지 않는다.
- 네임스페이스는 생성 코드와 같은 `Game_DataLoader`.
- 값 검증·표 간 연결은 `Validate()` / `AfterAllTableLoaded()`에 넣는다. `Generated/`가 아니라 `Containers/`에.
- `Id`·`Code` 외의 열로 조회하려면 `SubCollectionConstructor` / `SubCollectionAdd`를 재정의한다. 로드마다 새로 만들어 채우므로 재로드 시 중복이 쌓이지 않는다.

### 런타임 로드 흐름

```
DataManager.InitializeAsync()
  │
  ├─ DiscoverContainers()
  │     로드된 모든 어셈블리를 훑어 DataContainer 파생 구상 클래스를 찾아 인스턴스화한다.
  │     Containers/ 에 선언한 표만 여기 잡히고, 선언하지 않은 표는 로드되지 않는다.
  │
  ├─ Addressables 라벨 game_data 로 TextAsset 일괄 로드
  │     { asset.name : text } 로 만든 뒤 핸들은 즉시 Release 한다.
  │     라벨이 없으면 에러 로그를 남기고 빈 맵으로 진행한다 (예외를 던지지 않는다).
  │
  ├─ 컨테이너마다
  │     container.Name 과 같은 이름의 JSON 이 있으면   LoadJson(text)
  │     없으면                                         Clear() + 경고
  │     LoadJson 이 예외를 던지면                       Clear() + 에러
  │
  ├─ ValidateAll()      Loaded == true 인 컨테이너의 Validate() 호출, 실패 시 에러 로그
  └─ AfterAllLoaded()   Loaded == true 인 컨테이너의 AfterAllTableLoaded() 호출
```

`Validate` 와 `AfterAllTableLoaded` 는 모든 표가 로드된 뒤에 불린다. 다른 표를 참조하는 검증이나 표 간 연결 캐싱은 여기서 한다.

### 컨테이너 한 개가 채워지는 순서

```
LoadJson(text)
  Deserialize(text)                 Newtonsoft → List<TValue>
  MainCollectionConstructor(count)  주 색인 할당
  SubCollectionConstructor(count)   보조 색인 할당
  행마다:
      MainCollectionAdd(item.Key, item)
      SubCollectionAdd(item.Key, item)
  SetLoaded(true)
  OnLoadCompleted()
```

`MainCollection` 은 베이스가 소유한다. `DictionaryContainer` 면 `_dict`, `DictionaryGroupContainer` 면 `_groups` 다. 어떤 컨테이너를 상속했는지로 이미 결정되므로, 콘크리트에서 재정의하는 경우는 색인을 늘릴 때가 아니라 행 자체를 가공할 때다.

`SubCollection` 은 콘크리트가 소유한다. 베이스 구현은 비어 있고, `Id` · `Code` 외의 열로 조회해야 할 때 여기에 자료구조를 만든다. `Constructor` 가 매 로드마다 다시 불리므로 재로드 시 중복이 쌓이지 않는다.

### DataLoader 타입

| 타입 | 역할 |
|------|------|
| `DataManager` | 컨테이너 발견, JSON 일괄 로드와 주입, 검증 호출. 싱글턴 |
| `IDataContainer` | 컨테이너 계약. `Name` `Loaded` `LoadJson` `Clear` `Validate` `AfterAllTableLoaded` |
| `DataContainer` | `IDataContainer` 기본 구현. `Loaded` 상태를 관리한다 |
| `DataContainer<TKey,TValue>` | 역직렬화와 로드 순서를 담당. 위 5개 확장점을 선언한다 |
| `DictionaryContainer<TKey,TValue>` | 키 1개에 값 1개. `Get` `TryGet` `ContainsKey` `All` `AllValues` |
| `DictionaryGroupContainer<TKey,TValue>` | 키 1개에 값 목록. `Get` 이 `IReadOnlyList` 를 돌려준다 |
| `ListContainer<TKey,TValue>` | 순서를 유지하는 목록. `GetByIndex`, `GetByKey`(조회 결과를 지연 캐싱) |
| `IData` | 데이터 행 마커 인터페이스 |
| `IDataKey<T>` | 행이 자기 키를 노출하는 계약. 생성 코드가 `Key` 를 구현한다 |
| `JsonSettings` | Newtonsoft 공용 설정. null 값과 모르는 컬럼을 무시한다 |
| `GameRoot` | 컨테이너 접근 루트. `partial` 이고 프로퍼티는 생성 코드가 채운다 |
| `LiveDataEditorWindow` | 에디터 도구. JSON 표를 보고 수정한다 |

### Addressables 설정

`DataManager`는 라벨 `game_data`가 붙은 TextAsset을 전부 로드한다. **변환 도구는 라벨을 붙이지 않는다 — 최초 1회 수동 설정이 필요하다.**

1. Window > Asset Management > Addressables > Groups
2. `Assets/GameData` 폴더를 그룹 창에 드래그 → 폴더 통째로 엔트리가 된다
3. 해당 엔트리에 `game_data` 라벨을 부여한다

이후 추가되는 JSON은 폴더 엔트리에 자동 포함된다. 설정이 없으면 `DataManager`가 콘솔에 안내를 출력하고 빈 상태로 진행한다(예외를 던지지 않는다).

### Live Data Editor

`Tools > GameData > Live Data Editor` — `Assets/GameData`의 JSON을 표로 보고 수정한다. 플레이 중이면 런타임 인스턴스에도 즉시 반영된다. Ctrl+Z / Ctrl+C / Ctrl+V 지원.

---

## UIFramework

창은 프리팹으로 만들고 코드에서는 `WindowKey`로 참조한다. 키 문자열은 **`Resources` 기준 경로**다.

```csharp
public class TitleWindow : BaseWindow
{
    public static readonly WindowKey<TitleWindow> Key = new("UI/TitleWindow");

    protected override void OnOpening() { }
}

public class TitleManagement : BaseManagement
{
    protected override void AddWindows() => RegisterWindow(TitleWindow.Key, WindowType.Normal);

    public void ShowTitle() => OpenWindow(TitleWindow.Key);
}
```

- 프리팹 루트에 `Canvas` + 해당 창 스크립트를 붙인다.
- 창 등록은 `AddWindows()`에서만 한다.
- 매 프레임 갱신이 필요하면 `IWindowUpdate`를 구현한다. 열려 있는 동안에만 호출된다.

### 창이 열리고 닫히는 흐름

```
BaseManagement.Awake()
  InitializeWindowManagement()   WindowManagement.Instance 를 잡고
                                 WindowRegistry / WindowController 를 만든다
  InitializeComponents()         재정의용 훅
  AddWindows()                   RegisterWindow(key, type) — 키만 등록한다.
                                 프리팹은 아직 로드하지 않는다

OpenWindow(key, onOpenBefore, onOpenAfter)
  └─ WindowController → WindowManagement.OpenWindow
       │
       ├─ GetWindow(key)                    wrapper.Window 가 null 이면 이때 생성한다
       │    WindowFactory.LoadWindow<T>      Resources.Load(key.Path) → Instantiate
       │                                     → SetActive(false) 로 넘긴다
       │    window.WindowType = wrapper.WindowType
       │    window.BindCamera(uiCamera)      uiCamera 가 있으면 ScreenSpaceCamera 로 바꾼다
       │    window.AddObserver(WindowManagement)
       │
       └─ window.OpenInternal(before, after)
            SetState(Opening) ──통지──▶ WindowManagement.OnWindowStateChanged
            │                             HandleOpenWindow
            │                               타입별 열린 순서 리스트에 추가
            │                               ReassignDepths  깊이를 처음부터 다시 매긴다
            │                               IWindowUpdate 면 Update 대상에 등록
            SetEnable(true)
            onOpenBefore → OnOpening()
            SetState(Opened)
            onOpenAfter
```

```
Close()          HUD 면 무시한다
                 CloseType.Handle 이고 HandleCanClose() 가 false 면 무시한다
ForcedClose()    위 두 검사를 건너뛴다

  BaseClose()
    BeforeClosed()
    SetState(Closed) ──통지──▶ HandleCloseWindow
    │                            리스트에서 제거 → ReassignDepths → Update 대상에서 해제
    OnClose()
    SetEnable(false)
    StopAllCoroutines()
```

창 인스턴스는 닫아도 파괴되지 않고 `WindowWrapper` 에 남는다. 다시 열면 `Resources.Load` 없이 재사용된다. 실제 파괴는 `RestoreAllWindows()` 와 `WindowManagement` 의 `OnDestroy` 에서 일어난다.

### 깊이

`WindowType`이 `Canvas.sortingOrder` 대역을 정한다. 같은 타입 안에서는 열린 순서대로 쌓이고, 중간 창이 닫히면 남은 창들이 자동으로 재정렬된다.

| WindowType | 시작 깊이 |
|-----------|----------|
| `HUD` | 10 |
| `Normal` | 100 |
| `Popup` | 200 |
| `Modal` | 400 |
| `GlobalPopup` | 500 |

### 닫기

| 상황 | 동작 |
|------|------|
| `Close()` | 일반 닫기 |
| `WindowType.HUD` | `Close()`로 닫히지 않는다 — `ForcedClose()`를 쓴다 |
| `CloseType.Handle` | `HandleCanClose()`가 `false`면 `Close()`가 무시된다 |
| `ForcedClose()` | 위 조건을 전부 무시하고 닫는다 |

### PrefabAuto — 비Window 컴포넌트 풀링

리스트 아이템처럼 반복 생성되는 조각은 `BaseComponent`를 상속하고 `PrefabAuto`로 관리한다.

```csharp
public class ItemComponent : BaseComponent
{
    public static readonly PrefabAuto<ItemComponent> Auto =
        PrefabAuto.Get<ItemComponent>("UI/Components/ItemComponent");
}

var comp = ItemComponent.Auto.CreateForUI(parent);
ItemComponent.Auto.Release(comp);
```

상태 초기화·이벤트 구독 해제는 `IPoolable.OnDespawn()`에서 한다. 풀은 씬 단위(`ScenePrefabPool`)와 글로벌(`GlobalPrefabPool`) 두 종류다.

### 프리팹 풀 흐름

```
Auto.Create(parent) / CreateForUI(parent)
  PrefabLoader.Get<T>(path)              IsGlobal 이면 GetGlobal
    풀의 스택에 있으면   pop → _activePaths 등록 → IPoolable.OnSpawn()
    없으면              Resources.Load → Instantiate → 등록 → OnSpawn()
  부모 지정 → 좌표 리셋 → SetActive

Auto.Release(comp)
  PrefabLoader.Release                   Global → Scene 순으로 소속 풀을 찾는다
    소속 풀이면   _activePaths 제거 → OnDespawn() → SetActive(false)
                  → 풀 루트로 이동 → 스택 push
    아니면        경고 후 Destroy
```

`Preload(count)` 는 인스턴스를 만들어 스택에 바로 쌓는다. 꺼낸 적이 없으므로 `OnSpawn` / `OnDespawn` 을 호출하지 않는다.

### RecyclableScrollView

`ScrollRect` 파생. 화면에 보이는 셀만 만들어 재활용한다. `IRecyclableScrollDataSource`를 구현해 연결하고, 셀은 `IRecyclableItem`을 구현한다.

`CellSizeMode`: `Static`(고정 크기) · `Free`(프리팹 크기 측정) · `PerItem`(항목별 가변 — `IRecyclableVariableSize` 추가 구현).

> `Prefab/`의 `PrefabAuto`는 `Utility/TransformExtensions.cs`(`Game_Utility`)에 의존한다. 같이 복사한다.

---

### UIFramework 타입

| 타입 | 역할 |
|------|------|
| `WindowManagement` | 씬당 하나. 창 등록·생성·개폐·깊이 재정렬·`IWindowUpdate` 분배 |
| `BaseManagement` | 화면별 관리자의 부모. `AddWindows()` 에서 창을 등록한다. `Get<T>()` 로 다른 Management 를 찾는다 |
| `BaseWindow` | 창의 부모. 상태 전이, 옵저버 통지, `Canvas.sortingOrder` 반영 |
| `BaseComponent` | UI 조각의 부모. `Transform` · `RectTransform` 을 캐싱한다 |
| `WindowKey` / `WindowKey<T>` | `Resources` 기준 경로를 담는 식별자. `T` 로 창 타입을 묶는다 |
| `WindowKeyEqualityComparer` | `Path` 기준 비교자. 딕셔너리 키로 쓴다 |
| `WindowFactory` | `Resources.Load` → `Instantiate` → 컴포넌트 확인. 비활성 상태로 돌려준다 |
| `WindowWrapper` | 창 인스턴스와 `WindowType` 을 함께 보관한다 |
| `WindowRegistry` / `WindowController` | `WindowManagement` 를 감싸 등록과 제어를 분리한다 |
| `IWindowRegistry` / `IWindowController` | 위 둘의 계약 |
| `IWindowObserver` | 창 상태 변경을 받는다. `WindowManagement` 가 구현한다 |
| `IWindowUpdate` | 창이 열려 있는 동안만 `OnUpdate` / `OnFixedUpdate` 를 받는다 |
| `WindowType` | `HUD` `Normal` `Popup` `Modal` `GlobalPopup`. 깊이 대역을 정한다 |
| `WindowStateType` | `Closed` `Opening` `Opened` |
| `CloseType` | `Close`(일반) / `Handle`(`HandleCanClose()` 검사) |
| `MonoSingleton<T>` | `DontDestroyOnLoad` 싱글턴. 종료 중에는 새로 만들지 않는다 |
| `MonoSceneSingleton<T>` | 씬 단위 싱글턴. 씬을 다시 열면 재생성된다 |
| `PrefabAuto` / `PrefabAuto<T>` | 풀링 키. `Create` `CreateForUI` `Release` `Preload` |
| `PrefabLoader` | 정적 진입점. 씬 풀 / 글로벌 풀을 고르고, 반환 시 소속 풀을 찾는다 |
| `PrefabPoolCore` | 경로별 스택 풀의 실체. 활성 인스턴스의 소속 경로를 기록한다 |
| `ScenePrefabPool` / `GlobalPrefabPool` | 풀의 수명. 씬 전환 시 해제 / 앱 종료까지 유지 |
| `IPoolable` | `OnSpawn` / `OnDespawn`. 상태 초기화와 이벤트 해제를 여기서 한다 |
| `RecyclableScrollView` | `ScrollRect` 파생. 보이는 범위의 셀만 만들어 재활용한다 |
| `IRecyclableScrollDataSource` | 항목 수와 셀 바인딩을 제공한다 |
| `IRecyclableItem` | 셀이 자기 `RectTransform` 을 노출한다 |
| `IRecyclableVariableSize` | `PerItem` 모드에서 항목별 길이를 제공한다 |
| `CellSizeModeType` | `Static`(고정) `Free`(프리팹 크기 측정) `PerItem`(항목별 가변) |
| `RecyclableScrollViewEditor` | 인스펙터와 편집 모드 미리보기 |
| `TransformExtensions` | `ResetLocalTM` / `ResetAnchoredPos` |

---

## 코드 생성 진입점

산출물을 고칠 일이 있으면 아래를 본다. **C# 을 찍는 건 전부 `DbGenerator.cs` 다.** `smart_exporter.js` 는 원본을 읽어 JSON 과 스키마까지만 만든다.

### Unity — C# 생성

| 산출물 / 단계 | 함수 |
|--------------|------|
| 진입점 (`Ctrl+G`) | `Generate()` |
| `<표>.cs` (데이터 클래스) | `WriteDataClasses()` |
| `Containers.Generated.cs` | `WriteContainers()` / `AppendCodeContainer()` |
| `GameEnum.cs` | `WriteGameEnum()` |
| `GameRoot.Generated.cs` | `WriteGameRoot()` / `CollectConcreteContainers()` |
| 자료형 → C# 타입 매핑 | `CsType()` |
| 키 열 판정 (`Id` → `Key` → 첫 열) | `FindKeyColumn()` |
| 스키마 읽기 | `ReadTables()` / `ReadEnums()` |

경로는 전부 `DbGenerator` 상단 상수다 (`SchemaPath` · `GeneratedFolder` · `GameEnumPath` · `GameRootPath`).

### Node — JSON · 스키마 생성

| 단계 | 함수 |
|------|------|
| 셀 → JSON 값 변환 | `convertValue()` |
| 표 파싱 · 스키마 수집 | `processSheetRows()` |
| JSON 쓰기 | `createJsonFiles()` |
| `_Schema.json` 쓰기 | `writeSchema()` |
| enum 정의 수집 | `loadEnumDefinitions()` |

**확장자를 추가하려면** 로더를 만들고 `SOURCE_LOADERS` 에 한 줄, `SOURCE_EXTENSIONS` 에 한 줄 넣는다. 모든 로더는 `{ sheets: [{ name, rows }] }` 한 가지 형태를 반환하고, 그 아래 파이프라인은 원본이 무엇이었는지 알지 못한다.

```js
const SOURCE_LOADERS = {
    '.xlsx': 'loadXlsxWorkbook',
    '.js': 'loadJsWorkbook',
    '.py': 'loadPyWorkbook'
};
```

| 단계 | 함수 |
|------|------|
| 확장자 검증 (로더 없는 것 걸러냄) | `resolveActiveExtensions()` |
| 확장자별 분기 | `loadWorkbook()` |
| xlsx / js / py 로더 | `loadXlsxWorkbook()` · `loadJsWorkbook()` · `loadPyWorkbook()` |
| 표 형태 정규화 (`_Enum` 객체 → 행렬 포함) | `normalizeTableMap()` / `tableToRows()` |

enum 이름 정규화(`E` 접두사 제거 · `Type` 접미사)는 `enum_name.js` 의 `normalizeEnumName()` 한 곳에 있다. `DbGenerator` 는 이미 정규화된 이름을 스키마로 받으므로 같은 규칙을 다시 구현하지 않는다.

양쪽 모두 내용이 같은 파일은 다시 쓰지 않는다 (Unity 재임포트 방지).

### CLI

```bash
node smart_exporter.js all        # 전체 (run_win.bat 기본값)
node smart_exporter.js modified   # 변경된 파일만 (mtime 비교)
node smart_exporter.js file HeroData.xlsx LevelData.js ShopData.py
```

C# 은 만들지 않는다. 변환 후 Unity 에서 **Ctrl+G**(DB Generate)를 눌러야 코드가 갱신된다.

`--verbose` 로 상세 로그. 경로는 `GAMEDATA_PATH` · `JSON_OUTPUT_PATH` · `SCHEMA_OUTPUT_PATH` · `CONFIG_PATH` 환경변수로 덮어쓸 수 있다(`run_win.bat` 이 설정한다).

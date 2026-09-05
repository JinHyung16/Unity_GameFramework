# GameFramework

새 Unity 프로젝트에 그대로 얹어 쓰는 공용 코드. 두 축으로 구성된다.

| 모듈 | 역할 |
|------|------|
| **UIFramework** | 창(Window) 생명주기·깊이 관리, 프리팹 풀링, 재활용 스크롤 |
| **DataLoader** | 엑셀·CSV·JS·TS·Python → JSON → C# 코드 생성 → 런타임 로드 |

## Requirements

| 항목 | 용도 |
|------|------|
| Unity **2022.2+** | `FindAnyObjectByType`, C# 9 `new()` |
| `com.unity.ugui` | `UnityEngine.UI` — `BaseWindow` · `BaseComponent` · `RecyclableScrollView`. Unity 6 에서는 TextMeshPro 도 여기 포함된다 |
| `com.unity.nuget.newtonsoft-json` | JSON 역직렬화 |
| `com.unity.addressables` | `DataManager` 의 JSON 로드 |
| Node.js *(선택)* | `.js` · `.ts` 원본을 쓸 때만 |
| tsx *(선택)* | `.ts` 원본을 쓸 때만 (`npm i -g tsx`) |
| Python **3.x** *(선택)* | `.py` 원본을 쓸 때만 |

`.xlsx` 와 `.csv` 는 외부 런타임 없이 읽는다. 선택 항목이 없으면 그 확장자만 건너뛰고 나머지는 정상 변환된다.

동작을 확인한 조합은 Unity `6000.3.13f1` · ugui `2.0.0` · newtonsoft-json `3.2.1` · addressables `2.3.16` 이다.

## Install

1. `Assets/` 내용을 프로젝트 `Assets/`에 복사한다.
2. `_DataExporter/`는 프로젝트 루트(`Assets` 바깥)에 둔다 — 원본 데이터 폴더다.
3. **Tools > GameData > Setup Addressables** 를 한 번 실행한다. 안 하면 런타임에 데이터가 비어 있다.

## 디렉터리 구조

```
프로젝트 루트/
├─ Assets/
│  ├─ GameData/                       (생성) 변환된 JSON — Addressables 대상
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
│     │  └─ Editor/                   변환·생성 도구
│     │     ├─ DataExportPipeline.cs  원본 → JSON
│     │     ├─ DbGenerator.cs         C# 코드 생성
│     │     ├─ SourceTableConverter.cs 자료형 해석 · 값 변환
│     │     ├─ DataIssueLog.cs        에러/경고 수집
│     │     └─ Sources/               확장자별 로더 (여기에 추가)
│     ├─ Game/Core/                   GameRoot (컨테이너 접근 루트)
│     └─ Utility/                     Game_Utility 확장 메서드
│
└─ _DataExporter/                     원본 데이터 (Assets 밖 — Unity 가 임포트하지 않는다)
   ├─ config.json                     경로(Paths) + 표/열 제외 규칙
   └─ GameData/                       원본 .xlsx · .csv · .js · .ts · .py (예제 포함)
```

`(생성)` 표시된 경로는 도구가 덮어쓴다. 직접 수정하지 않는다. 저장소에는 원본도 생성물도 들어 있지 않다 — `_DataExporter/GameData/` 에 데이터를 넣고 **Ctrl+G** 를 누르면 나머지가 만들어진다. `Assets/Resources/` 는 UI 프리팹용이라 직접 만든다.

| 네임스페이스 | 위치 | 파일 수 |
|------------|------|--------|
| `Game_DataLoader` | `DataLoader/` — 생성되는 데이터 클래스와 컨테이너도 여기에 속한다 | 10 |
| `Game_UIFramework` | `UIFramework/` | 29 |
| `Game_Core` | `Game/Core/` — `GameRoot` | 1 |
| `Game_Utility` | `Utility/` | 1 |

---

## 데이터 파이프라인

원본을 편집하는 쪽과 코드를 만드는 쪽이 갈린다. **기획자는 원본만, 커밋도 원본만.**

```
_DataExporter/GameData/*.xlsx  *.csv  *.js  *.ts  *.py     기획자가 편집
        │
        │  Unity: Tools > GameData > Data Generate  (Ctrl+G)
        ▼
Assets/GameData/*.json                          런타임 데이터 (Addressables 대상)
Assets/Scripts/DataLoader/Generated/*.cs        데이터 클래스
Assets/Scripts/DataLoader/Generated/Containers.Generated.cs
Assets/Scripts/DataLoader/GameEnum.cs
Assets/Scripts/Game/Core/GameRoot.Generated.cs
```

Ctrl+G 한 번이 두 단계를 모두 돈다. ① 원본을 읽어 JSON 을 쓰고, ② 같은 실행 안에서 그 결과로 C# 을 만든다. 내용이 같은 파일은 다시 쓰지 않으므로 반복 실행이 싸다.

원본을 하나도 못 읽으면 **아무것도 건드리지 않고 에러만 남긴다.** 경로를 잘못 잡았을 때 생성 코드가 지워지는 것을 막는다.

원본은 `Assets` 바깥에 둔다. 안에 두면 Unity 가 임포트하고 `.meta` 를 만들고 빌드 후보로 잡는데, 게임에 들어갈 파일이 아니다.

### 검증

두 단계가 같은 기록부를 쓴다. 어디서 깨졌든 콘솔 한 곳에 모인다.

```
[GameData] 원본 3개 → 표 2개 / 23행, enum 2개 · 코드 2개, 컨테이너 프로퍼티 0개
에러 1건, 경고 2건

[에러] HeroData.xlsx > HeroData [4행 Code] — 필수 열이 비어 있습니다. 자료형 'string!'
[경고] HeroData.xlsx > HeroData [3행 Atk]  — 숫자로 바꿀 수 없습니다: 'xyz'
```

행 번호는 원본에서 보이는 그 번호다. 자료형처럼 열 전체에 걸린 문제는 행마다가 아니라 **한 번만** 보고한다.

문제가 있어도 나머지 표는 계속 변환한다. 한 파일이 깨졌다고 전체가 멈추면 작업이 막히기 때문이다.

| 상황 | 처리 |
|------|------|
| 필수(`!`) 열이 빈 값 | 에러. 자료형 기본값을 넣는다 |
| 자료형 변환 실패 | 경고. `null` 로 둔다 (필수면 기본값) |
| 모르는 자료형 | 에러. `string` 으로 처리 |
| 열 이름 중복 | 에러. 뒤쪽 열을 버린다 |
| 표 이름 중복 | 에러. 뒤쪽 파일을 건너뛴다 |
| 로더의 런타임 없음 | 경고. 그 확장자만 건너뛴다 |

### 원본 포맷

`Tools > GameData > Settings` 에서 지금 쓸 수 있는 포맷을 확인한다.

| 확장자 | 로더 | 필요한 것 |
|--------|------|----------|
| `.xlsx` | `XlsxSourceLoader` | 없음 (ZIP + XML 직접 파싱) |
| `.csv` | `CsvSourceLoader` | 없음 |
| `.js` | `JsSourceLoader` | node |
| `.ts` | `TsSourceLoader` | node + tsx |
| `.py` | `PySourceLoader` | python 3 |

런타임이 없는 포맷은 목록에 흐리게 뜨고 이유가 함께 표시된다. 그 확장자만 건너뛰고 나머지는 정상 변환된다.

### 경로 설정

입력·출력 경로는 **`_DataExporter/config.json` 의 `Paths` 한 곳**에 있다. Unity 메뉴 **Tools > GameData > Settings** 에서 편집한다.

| 항목 | 쓰이는 단계 | 기본값 |
|------|-----------|--------|
| `SourceFolder` | ① 원본 읽기 | `_DataExporter/GameData` |
| `JsonOutput` | ① JSON 쓰기 | `Assets/GameData` |
| `GeneratedFolder` | ② 코드 쓰기 | `Assets/Scripts/DataLoader/Generated` |
| `ContainersFolder` | ② 스캔 | `Assets/Scripts/DataLoader/Containers` |
| `GameEnumFile` | ② | `Assets/Scripts/DataLoader/GameEnum.cs` |
| `GameRootFile` | ② | `Assets/Scripts/Game/Core/GameRoot.Generated.cs` |

상대 경로는 프로젝트 루트(`Assets` 의 부모) 기준이다. 절대 경로도 넣을 수 있다.

설정 창은 저장 전에 검사한다 — 원본 폴더가 없을 때, 코드 출력 폴더가 `Assets` 밖일 때(컴파일되지 않는다).

`Paths` 만 갈아끼우므로 `config.json` 의 다른 키(`ExcludeColumnPrefix` 등)는 건드리지 않는다.

### 전체 흐름 한눈에

원본 세 개를 넣고 **Ctrl+G** 를 눌렀을 때 실제로 나오는 모습이다.

```
_DataExporter/GameData/
├─ HeroData.xlsx        int! · string! · float · bool · stringArray, 한글 값
├─ LevelData.js         상수와 루프로 20행 생성
└─ _Enum.js             enum 정의 (객체 규약)
```

```
[GameData] 원본 3개 → 표 2개 / 23행, enum 2개 · 코드 2개, 컨테이너 프로퍼티 1개
```

```
Assets/GameData/
├─ HeroData.json                              3행
└─ LevelData.json                             20행

Assets/Scripts/DataLoader/
├─ GameEnum.cs                                StatType · CurrencyType
├─ Generated/
│  ├─ HeroData.cs                             public float Atk / string[] Tags …
│  ├─ LevelData.cs
│  └─ Containers.Generated.cs                 표마다 추상 부모 (아래 참고)
└─ Containers/
   └─ HeroDataContainer.cs                    ← 직접 작성한 것

Assets/Scripts/Game/Core/
└─ GameRoot.Generated.cs                      Containers/ 를 스캔한 결과
```

`Containers.Generated.cs` 에는 표마다 갈래가 나온다. `HeroData` 는 `int` 키에 `Code` 열이 있어 셋이 다 나왔고, `LevelData` 는 `Code` 가 없어 둘만 나왔다.

```csharp
HeroDataDictionaryContainer        HeroDataDictionaryGroupContainer        HeroDataCodeContainer
LevelDataDictionaryContainer       LevelDataDictionaryGroupContainer
```

`Containers/HeroDataContainer.cs` 를 직접 만들었기 때문에 `GameRoot` 에 프로퍼티가 하나 붙었다. 이 파일을 안 만들면 `HeroData` 는 JSON 이 있어도 로드되지 않는다.

### 포맷 추가하기

`Assets/Scripts/DataLoader/Editor/Sources/` 에 `IDataSourceLoader` 구현을 하나 넣으면 끝이다. 리플렉션으로 수집하므로 등록 코드를 고칠 필요가 없다.

```csharp
public sealed class TsvSourceLoader : IDataSourceLoader
{
    public string Extension => ".tsv";
    public string DisplayName => "TSV";
    public int Order => 16;

    public bool IsAvailable(out string reason)
    {
        reason = null;
        return true;
    }

    public IEnumerable<SourceTable> Load(string filePath, DataIssueLog log)
    {
        // 0행 컬럼명, 1행 자료형, 2행부터 데이터인 SourceTable 을 돌려준다.
        // 문제는 예외로 던지지 말고 log 에 쌓는다.
    }
}
```

로더는 원본이 무엇이든 `SourceTable` 한 가지 형태로만 돌려주면 된다. 자료형 해석과 값 변환은 `SourceTableConverter` 가 전담하므로 포맷이 늘어도 규칙이 갈리지 않는다.

구글 시트처럼 파일이 아닌 원본도 같은 자리에 붙는다.

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

셋 다 결과가 완전히 동일하다. 자료형 표기·키 규칙·`!` 필수 검사·`#` 제외가 전부 같게 적용된다.

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

> `.js` · `.ts` · `.py` 는 그 언어 런타임으로 **실제 실행된다.** 외부에서 받은 파일을 그대로 `GameData/` 에 넣지 않는다.

### 표 작성 규칙

| 규칙 | 내용 |
|------|------|
| 키 | `Id`(`int!`, 1부터 순번)가 키다. 없으면 `Key` 열, 그것도 없으면 맨 왼쪽 열. `Id`와 `Key`를 같이 두지 않는다 |
| 논리 식별자 | 세이브·로직이 참조하는 문자열은 `Code`(`string!`). 타 표 참조 열은 `~Code`/`~Codes` (`~Id`는 금지 — int로 오해된다) |
| 필수 | 자료형 뒤에 `!` (`int!`). 빈 값이면 에러가 나고 기본값이 들어간다 |
| 배열 | `intArray` · `floatArray` · `stringArray`, 셀은 쉼표 구분(`1,2,3`). 요소에 쉼표가 필요하면 배열 대신 1:N 표로 정규화 |
| enum | 아래 `_Enum` 참고. 자료형 칸에 enum 이름만 적는다. `E` 접두사 금지, `~Type` 접미사 |
| 제외 | 자료형 앞에 `#` 을 붙이면 그 열은 내보내지 않는다 (`#int`). 열 이름 앞에 붙여도(`#Memo`) 같다. 이름이 `_` 로 시작하는 열도 제외된다 |
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

**`Containers/` 는 생성 대상이 아니라 스캔 대상이다.** Ctrl+G 가 여기를 읽어 `GameRoot.Generated.cs` 를 만든다.

```csharp
namespace Game_DataLoader
{
    public class HeroDataContainer  : HeroDataCodeContainer { }             // Code 조회 (int 키 + Code 열)
    public class LevelDataContainer : LevelDataDictionaryContainer { }      // 1:1
    public class DropDataContainer  : DropDataDictionaryGroupContainer { }  // 1:N
}
```

콘크리트를 추가·삭제한 뒤 **Ctrl+G**(DB Generate)를 다시 누르면 `GameRoot.Generated.cs`가 갱신된다. 데이터가 바뀌지 않았어도 된다.

```csharp
await DataManager.Instance.InitializeAsync();                     // 시작 시 1회

var knight = GameRoot.Instance.HeroDataContainer.Get("knight");   // Code 단건
var hero   = GameRoot.Instance.HeroDataContainer.Get(1);          // int 키
var all    = GameRoot.Instance.HeroDataContainer.AllValues;
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

`DataManager` 는 라벨 `game_data` 가 붙은 TextAsset 을 전부 로드한다. 최초 1회만 해두면 된다.

**Tools > GameData > Setup Addressables** 를 누르면 끝난다. 그룹 `GameData` 를 만들고 JSON 폴더를 통째로 엔트리로 등록한 뒤 라벨을 붙인다. Addressables 설정 자체가 없으면 그것부터 만든다.

폴더째 등록하므로 표가 늘어도 다시 할 일이 없다.

등록이 안 된 상태로 Ctrl+G 를 누르면 경고가 뜬다.

```
[경고] 'Assets/GameData' 이 Addressables 라벨 'game_data' 로 등록되지 않았습니다.
       Tools > GameData > Setup Addressables 를 한 번 실행하세요.
       안 하면 런타임에 데이터가 비어 있습니다.
```

그래도 로드 시점에 죽지는 않는다. `DataManager` 가 콘솔에 안내를 남기고 빈 상태로 넘어간다.

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

- 프리팹 루트에 `Canvas` + `GraphicRaycaster` + 해당 창 스크립트를 붙인다.
- **`UIRoot` 에는 `Canvas` 를 붙이지 않는다.** 창 Canvas 가 중첩되면 깊이가 먹지 않는다.
- 창 등록은 `AddWindows()`에서만 한다.
- 매 프레임 갱신이 필요하면 `IWindowUpdate`를 구현한다. 열려 있는 동안에만 호출된다.

### 창의 생명주기는 Open / Close 로만 다룬다

**창 스크립트에 `OnEnable` · `OnDisable` 을 쓰지 않는다.** 창을 여닫으면 `GameObject` 가 켜지고 꺼지므로 그 둘도 불리기는 한다. 다만 창의 수명은 `Open` / `Close` 로만 다룬다 — 프레임워크는 그 콜백에 아무것도 걸지 않는다.

| 시점 | 재정의할 것 |
|------|------------|
| 열릴 때 | `OnOpening()` |
| 위로 다른 창이 열려 가려질 때 | `OnOtherWindowOpened()` |
| 위 창이 닫혀 다시 드러날 때 | `OnReOpened()` |
| 닫히기 직전 | `BeforeClosed()` |
| 닫힐 때 | `OnClose()` |
| 닫아도 되는지 판정 | `HandleCanClose()` — `CloseType.Handle` 일 때만 |

`OnEnable` 은 씬 로드나 부모 비활성화 같은 다른 경로로도 불려서 "열렸다"와 일대일로 맞지 않는다. 직접 `Open` 한 창은 직접 `Close` 하면서 정리하는 편이 흐름이 분명하다.

`Awake` 는 창을 처음 만들 때 한 번 불린다. 여닫을 때마다 필요한 정리는 `OnClose()` 에 둔다.

### 가려질 때와 다시 드러날 때

같은 `WindowType` 안에서 창이 겹치면 위아래가 생긴다. 그때 아래 창에게 알려준다.

```csharp
public class ShopWindow : BaseWindow
{
    protected override void OnOtherWindowOpened()
    {
        // 구매 팝업이 위에 떴다. 가려진 동안 갱신을 멈춘다.
        StopRefreshTimer();
    }

    protected override void OnReOpened()
    {
        // 팝업이 닫혀 다시 맨 위가 됐다. 그 사이 바뀐 재화를 다시 읽는다.
        RefreshCurrency();
        StartRefreshTimer();
    }
}
```

맨 위 창이 닫힐 때만 그 아래 창이 `OnReOpened()` 를 받는다. 중간 창이 닫히면 위아래 관계가 그대로라 아무 통지도 가지 않는다.

씬을 정리하며 창을 한꺼번에 파괴할 때는 이 통지를 보내지 않는다. 곧 사라질 창이 데이터를 다시 읽는 일을 막기 위해서다.

닫힌 창은 `GameObject` 가 꺼지므로 그 안의 `Update` · 코루틴 · `Animator` 도 함께 멈춘다. 그리기만 잠시 멈추고 싶으면 `CanvasEnable` 을 쓴다 — 여닫기용이 아니다.

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
            SetEnable(true)           gameObject.SetActive(true)
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

**UIRoot 에는 `Canvas` 를 두지 않는다.** 창은 저마다 `Canvas` 를 갖는데, 부모에도 `Canvas` 가 있으면 창 Canvas 가 중첩되어 `sortingOrder` 가 통째로 무시된다. `UIRoot` 는 빈 `GameObject` 여야 하고, 위에 Canvas 가 있으면 `WindowManagement` 가 경고를 띄운다.

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
| `BaseWindow` | 창의 부모. 상태 전이, 옵저버 통지, 깊이 반영, 가림/재노출 통지. `SetEnable()` 로 `GameObject` 를 켜고 끈다 |
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

산출물을 고칠 일이 있으면 아래를 본다. 전부 `Assets/Scripts/DataLoader/Editor/` 에 있다.

### ① 원본 → JSON

| 단계 | 파일 | 함수 |
|------|------|------|
| 진입점 (`Ctrl+G` 의 앞단) | `DataExportPipeline.cs` | `Run()` — 변환 결과를 그대로 ② 에 넘긴다 |
| 확장자별 로더 수집 | `Sources/DataSourceRegistry.cs` | `All` / `Find()` |
| 로더 계약 | `Sources/IDataSourceLoader.cs` | `SourceTable` |
| xlsx 파싱 | `Sources/XlsxSourceLoader.cs` | `ReadSheet()` / `ReadCell()` |
| csv 파싱 | `Sources/CsvSourceLoader.cs` | `Parse()` |
| js · ts · py 실행 | `Sources/JsSourceLoader.cs` 외 | `ExternalRuntime.Run()` |
| 스크립트 결과 해석 | `Sources/ScriptTableReader.cs` | `FromJson()` |
| 자료형 해석 · 값 변환 | `SourceTableConverter.cs` | `ParseColumns()` / `ConvertCell()` |
| enum 수집 | `EnumCollector.cs` | `Collect()` |

### ② 변환 결과 → C#

| 산출물 | 파일 | 함수 |
|--------|------|------|
| 진입점 (`Ctrl+G`) | `DbGenerator.cs` | `Generate()` / `GenerateCode()` |
| `<표>.cs` (데이터 클래스) | `DbGenerator.cs` | `WriteDataClasses()` |
| `Containers.Generated.cs` | `DbGenerator.cs` | `WriteContainers()` / `AppendCodeContainer()` |
| `GameEnum.cs` | `DbGenerator.cs` | `WriteGameEnum()` |
| `GameRoot.Generated.cs` | `DbGenerator.cs` | `WriteGameRoot()` |
| 자료형 → C# 타입 매핑 | `DbGenerator.cs` | `CsType()` |
| 키 열 판정 (`Id` → `Key` → 첫 열) | `DbGenerator.cs` | `FindKeyColumn()` |

### 공통

| 역할 | 파일 |
|------|------|
| 에러·경고 수집과 리포트 | `DataIssueLog.cs` |
| 경로 설정 읽기·쓰기 | `DataPipelineConfig.cs` |
| 설정 창 | `DataPipelineSettingsWindow.cs` |

두 단계 모두 내용이 같은 파일은 다시 쓰지 않는다 (Unity 재임포트 방지). 자료형 해석은 `SourceTableConverter` 한 곳에만 있으므로, 포맷을 추가해도 규칙이 갈리지 않는다.

① 과 ② 는 한 실행 안에서 이어지므로 중간 파일을 거치지 않는다. `ConvertedTable` 이 그대로 넘어간다.

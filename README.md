# GameFramework

새 Unity 프로젝트에 그대로 얹어 쓰는 공용 코드. 두 축으로 구성된다.

| 모듈 | 역할 |
|------|------|
| **UIFramework** | 창(Window) 생명주기·깊이 관리, 프리팹 풀링, 재활용 스크롤 |
| **DataLoader** + **_DataExporter** | 엑셀 → JSON → C# 코드 생성 → 런타임 로드 |

## Requirements

| 항목 | 용도 |
|------|------|
| Unity **2022.2+** | `FindAnyObjectByType`, C# 9 `new()` |
| `com.unity.nuget.newtonsoft-json` | JSON 역직렬화 |
| `com.unity.addressables` | `DataManager`의 JSON 로드 |
| `com.unity.textmeshpro` | `UIFramework/Examples`만 사용 — 예제 삭제 시 불필요 |
| Node.js **14+** | `_DataExporter` 실행 (Unity 외부) |

## Install

1. `Assets/` 내용을 프로젝트 `Assets/`에 복사한다.
2. `_DataExporter/`는 프로젝트 루트(`Assets` 바깥)에 둔다 — Unity가 무시한다.
3. [Addressables 설정](#addressables-설정)을 한 번 해준다. 안 하면 데이터가 로드되지 않는다.

## 디렉터리 구조

```
프로젝트 루트/
├─ Assets/
│  ├─ GameData/                       (생성) 변환된 JSON
│  ├─ Resources/                      창·컴포넌트 프리팹
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
│     │  ├─ GameEnum.cs               (생성) _Enum 엑셀 기준 enum
│     │  └─ Editor/                   Live Data Editor
│     ├─ Game/Core/                   GameRoot (컨테이너 접근 루트)
│     └─ Utility/                     Game_Utility 확장 메서드
│
└─ _DataExporter/                     엑셀 변환 도구
   ├─ run_win.bat                     실행 진입점
   ├─ smart_exporter.js               엑셀 파싱 → JSON
   ├─ class_generator.js              C# 코드 생성
   ├─ config.json                     시트/열 제외 규칙
   └─ GameData/                       원본 .xlsx
```

`(생성)` 표시된 경로는 도구가 덮어쓴다. 직접 수정하지 않는다.

---

## 데이터 파이프라인

```
_DataExporter/GameData/*.xlsx
        │  run_win.bat
        ▼
Assets/GameData/*.json                            런타임 데이터
Assets/Scripts/DataLoader/Generated/*.cs          표 1개 = 클래스 1개
Assets/Scripts/DataLoader/Generated/Containers.Generated.cs   컨테이너 추상 부모
Assets/Scripts/DataLoader/GameEnum.cs             Game.GameEnum.XxxType
Assets/Scripts/Game/Core/GameRoot.Generated.cs    컨테이너 접근 프로퍼티
```

코드 생성은 **`run_win.bat` 한 번으로 전부** 끝난다. Unity에서 눌러야 하는 메뉴는 없다.

### 시트 규칙

**시트 1장 = 표 1개.** 시트 이름이 곧 클래스명이자 JSON 파일명이다.

| 행 | 내용 | 예 |
|----|------|-----|
| 1행 | 열 이름 (PascalCase) | `Id` · `Code` · `Atk` |
| 2행 | 자료형 | `int!` · `string!` · `float` |
| 3행~ | 데이터 | `1` · `knight` · `10.5` |

> ⚠️ **1행이 열 이름, 2행이 자료형이다.** 순서를 바꾸면 전부 `string`으로 떨어진다.

| 규칙 | 내용 |
|------|------|
| 키 | `Id`(`int!`, 1부터 순번)가 키다. 없으면 `Key` 열, 그것도 없으면 맨 왼쪽 열. `Id`와 `Key`를 같이 두지 않는다 |
| 논리 식별자 | 세이브·로직이 참조하는 문자열은 `Code`(`string!`). 타 표 참조 열은 `~Code`/`~Codes` (`~Id`는 금지 — int로 오해된다) |
| 필수 | 자료형 뒤 `!` (`int!`). 빈 값이면 에러 |
| 배열 | `intArray` · `floatArray` · `stringArray`, 셀은 쉼표 구분(`1,2,3`). 요소에 쉼표가 필요하면 배열 대신 1:N 시트로 정규화 |
| enum | `_Enum` 엑셀에 열 1개 = enum 1개(헤더=이름, 아래=멤버). 자료형 칸에 enum 이름만 적는다. `E` 접두사 금지, `~Type` 접미사 |
| 제외 | `_`로 시작하는 시트·열은 변환하지 않는다 (`_Enum` 파일은 예외) |

### 컨테이너 작성

도구는 추상 부모까지만 만든다. **실제로 쓸 표만** `Containers/`에 콘크리트로 선언한다.

```csharp
using Game_DataLoader;

public class ModeDataContainer       : ModeDataCodeContainer { }             // Code 조회 (int 키 + Code 열)
public class StageThemeDataContainer : StageThemeDataDictionaryContainer { } // 1:1
public class DropDataContainer       : DropDataDictionaryGroupContainer { }  // 1:N
```

콘크리트를 추가/삭제한 뒤 `run_win.bat`을 다시 돌리면(엑셀 변경 없어도 됨) `GameRoot.Generated.cs`가 갱신된다.

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

### RecyclableScrollView

`ScrollRect` 파생. 화면에 보이는 셀만 만들어 재활용한다. `IRecyclableScrollDataSource`를 구현해 연결하고, 셀은 `IRecyclableItem`을 구현한다.

`CellSizeMode`: `Static`(고정 크기) · `Free`(프리팹 크기 측정) · `PerItem`(항목별 가변 — `IRecyclableVariableSize` 추가 구현).

> `Prefab/`의 `PrefabAuto`는 `Utility/TransformExtensions.cs`(`Game_Utility`)에 의존한다. 같이 복사한다.

---

## 코드 생성 진입점

산출물을 고칠 일이 있으면 아래를 본다.

| 산출물 | 파일 | 함수 |
|--------|------|------|
| `<표>.cs` (데이터 클래스) | `class_generator.js` | `buildTableClass()` |
| `Containers.Generated.cs` | `class_generator.js` | `buildContainerEntry()` / `generateContainersFile()` |
| `GameEnum.cs` | `class_generator.js` | `generateGameEnumFile()` |
| `GameRoot.Generated.cs` | `class_generator.js` | `generateGameRootFile()` |
| 엑셀 자료형 → C# 타입 매핑 | `class_generator.js` | `csType()` |
| 엑셀 셀 → JSON 값 변환 | `smart_exporter.js` | `convertValue()` |
| 시트 파싱 · 스키마 수집 | `smart_exporter.js` | `processWorksheet()` |
| JSON 쓰기 + 코드 생성 호출 | `smart_exporter.js` | `createJsonFiles()` |

`GameEnum.cs`와 `GameRoot.Generated.cs`는 매 실행마다 갱신되며, 내용이 같으면 파일을 건드리지 않는다(Unity 재임포트 방지).

### CLI

```bash
node smart_exporter.js all        # 전체 (run_win.bat 기본값)
node smart_exporter.js modified   # 변경된 엑셀만
node smart_exporter.js file A.xlsx B.xlsx
```

`--verbose`로 상세 로그. 경로는 `GAMEDATA_PATH` · `JSON_OUTPUT_PATH` · `CS_OUTPUT_PATH` · `CONFIG_PATH` 환경변수로 덮어쓸 수 있다(`run_win.bat`이 설정한다).

# C-Dir

**다중 창 파일 탐색과 전체 드라이브 즉시 검색**을 하나로 합친 Windows 파일 관리자입니다.

- **다중 창**: Q-Dir 스타일의 1 / 2 / 3 / 4 분할 창에서 동시에 여러 폴더를 탐색, 창마다 여러 탭 지원
- **즉시 검색**: F3 한 번으로 전체 드라이브를 즉시 검색 (Everything 연동은 선택 사항, 미설치 시 내장 인덱스로 자동 대체)
- **빠른 이동**: 클릭 가능한 경로(브레드크럼), 지연 로딩 폴더 트리, 폴더 즐겨찾기

![C-Dir](docs/screenshot.png)

> Everything 연동은 선택 사항입니다. Everything이 설치되어 있지 않으면 내장 인덱스로 검색합니다.

## 주요 기능

| 영역 | 기능 |
| --- | --- |
| 분할 탐색 | 1·2·3·4 분할 (툴바 또는 `Ctrl+1`~`Ctrl+4`), 활성 창 강조 |
| 탭 | 창마다 여러 탭 (`Ctrl+T` 새 탭, `Ctrl+W` 닫기, `Ctrl+PgUp`/`Ctrl+PgDn` 전환), 탭별 경로·히스토리·선택 유지, 재실행 시 복원 |
| 탐색 | 뒤로/앞으로/위로, 경로 직접 입력, 드라이브 빠른 이동, 현재 폴더 필터 |
| 즐겨찾기 | 별(☆) 메뉴에서 현재 폴더 추가/제거, 폴더 우클릭 메뉴로 추가/제거, 별이 채워지면(★) 현재 폴더가 즐겨찾기, 메뉴에서 바로 이동·관리 |
| 경로 이동 | 주소 표시줄의 각 폴더를 클릭해 상위 폴더로 즉시 이동(브레드크럼), 트리 버튼으로 폴더 트리 표시·선택, 현재 경로 자동 동기화 |
| 검색 | 전체 드라이브 즉시 검색, `regex:` 정규식 검색, 결과에서 Enter로 열기 / `Ctrl+Enter`로 폴더에서 보기, 우클릭으로 복사·이동·이름 바꾸기·삭제·경로 복사·속성 |
| 파일 작업 | 복사·잘라내기·붙여넣기(Windows 탐색기와 상호 붙여넣기 지원), 드래그 앤 드롭(창 간 이동·Ctrl 복사), 이름 바꾸기, 휴지통 삭제, 새 폴더, 탐색기에서 보기, 터미널 열기, 속성 |
| 표시 | 자세히 보기, 컬럼 정렬(폴더 우선, 자연 정렬), 파일 종류별 셸 아이콘, 라이트/다크 테마 |
| 기타 | 창 크기·레이아웃·마지막 경로·탭·테마 자동 저장 |

## 실행

```powershell
# 필요: .NET 10 SDK (Windows)
dotnet run
```

또는 빌드 후 실행:

```powershell
dotnet build -c Release
.\bin\Release\net10.0-windows\C-Dir.exe
```

## Everything 연동 (선택)

C-Dir는 **Everything이 설치되어 있으면 Everything의 색인을 그대로 사용**합니다. 별도 설정은 필요 없습니다.

1. [Everything](https://www.voidtools.com/) (x64)을 설치합니다.
2. C-Dir를 실행하면 자동으로 `Everything64.dll`을 찾아 사용합니다.
   - 탐색 순서: `C-Dir.exe`가 있는 폴더 → `C:\Program Files\Everything` → `C:\Program Files (x86)\Everything` → `%LOCALAPPDATA%\Programs\Everything`
3. 툴바 오른쪽 칩이 **“Everything 연결됨”** 으로 표시되면 연동 완료입니다.
4. Everything을 나중에 설치했다면 앱 메뉴(⋯) → **Everything 다시 감지**를 누르세요.

> Everything이 없거나 실행 중이 아니면 자동으로 **내장 인덱스**로 검색합니다.

> C-Dir는 `Everything.exe` / `Everything64.dll`을 포함하거나 재배포하지 않으며, 사용자가 설치한 Everything을 문서화된 SDK 방식으로 호출합니다.

## 내장 인덱스

- 첫 실행 시 고정 드라이브 전체를 스캔해 `%LOCALAPPDATA%\CDir\fileindex.bin` 에 저장합니다.
- 스캔 중에도 부분 결과로 검색이 가능하며, 상태 표시줄에서 진행 상황을 확인하거나 **중지**할 수 있습니다.
- 이후 실행에서는 저장된 인덱스를 즉시 불러옵니다. 파일이 많이 바뀌었다면 **인덱스 재생성**을 실행하세요.
- 검색 문법(내장 인덱스):
  - 공백으로 구분한 여러 단어는 **AND** 조건 (예: `report 2024`)
  - `"정확한 구문"` 큰따옴표 지원
  - `*.pdf`, `report-?.docx` 와일드카드
  - `ext:pdf,docx` 확장자 필터
  - `regex:`로 시작하면 **나머지 전체를 파일 이름 정규식**으로 검색합니다. 대소문자를 구분하지 않습니다.
    - 예: `regex:^IMG_\d{4}`, `regex:\.(jpg|png)$`, `regex:^(report|summary).*2024`
    - `regex:`는 검색어 맨 앞에서만 사용할 수 있으며, 정규식 모드에서는 `ext:`·와일드카드 등 다른 문법이 비활성화됩니다.
    - 패턴은 512자로 제한되며, 실행 시간이 너무 길면 검색을 중단하고 상태 표시줄에 오류를 표시합니다.

## 검색 결과 작업

검색 결과를 우클릭하면 다음 작업을 바로 실행할 수 있습니다.

- **복사... / 이동...**: 대상 폴더를 선택해 선택한 항목을 복사하거나 이동합니다.
- **이름 바꾸기...**: 파일 또는 폴더 이름을 바꿉니다. 폴더 이름을 바꾸면 결과에 포함된 하위 경로도 함께 갱신됩니다.
- **삭제 (휴지통)**: 선택한 항목을 휴지통으로 보냅니다.
- **경로 복사 / 속성**: 기존과 동일합니다.

검색 결과 목록에 포커스가 있을 때 `Delete`, `F2`, `Ctrl+C`, `Ctrl+X`도 선택한 결과에 적용됩니다(활성 탐색 창에는 적용되지 않습니다). `Ctrl+X`로 잘라낸 뒤 다른 창에서 붙여넣으면 원래 경로가 사라지므로 해당 결과는 목록에서 자동으로 제거됩니다. 검색 결과는 검색 시점의 스냅샷이므로, 이름 변경·이동·삭제 후에는 필요할 때 다시 검색하거나 내장 인덱스를 재생성하세요.

> Everything 연동 시 `regex:` 모드는 Everything의 PCRE 엔진을 사용하고, 내장 인덱스는 .NET 정규식을 사용합니다. 두 엔진에서 모두 동작하는 일반적인 패턴 사용을 권장합니다.

## 단축키

| 키 | 동작 |
| --- | --- |
| `F3`, `Ctrl+F` | 검색창으로 이동 |
| `Esc` | 검색 닫기 / 주소창 편집 취소 |
| `Ctrl+1`~`Ctrl+4` | 분할 수 변경 |
| `Ctrl+Tab` | 다음 창 활성화 |
| `Ctrl+T` | 현재 폴더에서 새 탭 열기 |
| `Ctrl+W` | 현재 탭 닫기 |
| `Ctrl+PgUp` / `Ctrl+PgDn` | 이전 / 다음 탭 |
| `Ctrl+L` | 주소창 편집 (경로 직접 입력) |
| `F5` | 새로 고침 |
| `Backspace` | 상위 폴더 |
| `Alt+←` / `Alt+→` | 뒤로 / 앞으로 |
| `Enter` | 열기 (폴더는 들어가기) |
| `F2` | 이름 바꾸기 |
| `Delete` | 휴지통으로 삭제 |
| `Ctrl+C` / `Ctrl+X` / `Ctrl+V` | 복사 / 잘라내기 / 붙여넣기 |
| `Ctrl+Shift+N` | 새 폴더 |
| `Ctrl+H` | 숨김 항목 표시 전환 |
| `Alt+Enter` | 속성 |

검색 결과 목록에 포커스가 있을 때 `Delete`, `F2`, `Ctrl+C`, `Ctrl+X`는 활성 탐색 창 대신 검색 결과에 적용됩니다.

## 설정

`%LOCALAPPDATA%\CDir\settings.json`

```jsonc
{
  "PaneCount": 2,
  "PanePaths": ["C:\\Users\\me\\Documents", "C:\\Users\\me\\Downloads", "C:\\Users\\me", "C:\\"],
  "PaneTabs": [
    ["C:\\Users\\me\\Documents", "C:\\Projects"],
    ["C:\\Users\\me\\Downloads"],
    ["C:\\Users\\me"],
    ["C:\\"]
  ],
  "PaneActiveTab": [1, 0, 0, 0],   // 창별로 활성화할 탭 인덱스
  "DarkTheme": false,
  "ShowHidden": false,
  "Favorites": ["C:\\Users\\me\\Documents", "C:\\Projects"],   // 즐겨찾기 폴더
  "SearchRoots": [],        // 비어 있으면 모든 고정 드라이브
  "WindowWidth": 1300,
  "WindowHeight": 820
}
```

앱 메뉴(⋯) → **설정 폴더 열기**로 바로 열 수 있습니다.

## 프로젝트 구조

```
CDir.csproj
App.xaml(.cs)              앱 시작, 테마, Everything DLL 탐색
MainWindow.xaml(.cs)       툴바·분할 레이아웃·검색 결과 패널·전역 단축키
Controls/FilePane.xaml(.cs)  한 개의 탐색 창 (탭, 탐색, 브레드크럼, 폴더 트리, 정렬, 파일 작업)
ViewModels/                FilePaneViewModel, PaneHostViewModel(탭 호스트), SearchViewModel
Models/                    FileItem, AppSettings
Services/
  NativeFileIndex.cs       내장 인덱스 (스캔·저장·부분 검색)
  SearchQuery.cs           검색어/정규식 파싱 및 시간 제한
  PathUtil.cs              경로 포함 관계·파일 이름 검사
  EverythingSearch.cs      Everything SDK P/Invoke 래퍼
  SearchService.cs         Everything → 내장 인덱스 폴백
  ShellOps.cs              셸 복사/이동/삭제/속성/터미널
  ShellIcons.cs            확장자별 셸 아이콘 캐시
  SettingsStore.cs         설정/캐시 경로
  FavoritesStore.cs        창 전체가 공유하는 폴더 즐겨찾기
  ThemeManager.cs          라이트·다크 팔레트 및 타이틀바
  FileClipboard.cs         내부/시스템 클립보드
Themes/Base.xaml           색상 토큰과 컨트롤 스타일
Dialogs/InputDialog.xaml   이름 입력 대화상자
LICENSE                    MIT 라이선스
THIRD-PARTY-NOTICES.md     제3자 라이선스 고지
```

## 상표 및 비공식 고지

C-Dir는 voidtools 및 Q-Dir 개발자와 제휴하거나 후원받지 않은 독립 프로젝트입니다. **Everything**과 **Q-Dir**는 각 소유자의 상표입니다. 이 프로젝트는 해당 제품의 소스 코드, 아이콘, 로고를 포함하지 않으며, Everything 연동은 사용자 PC에 설치된 공식 SDK를 호출하는 방식으로 동작합니다.

## 라이선스

이 프로젝트는 MIT 라이선스로 배포됩니다. 자세한 내용은 [LICENSE](LICENSE)를 참고하세요. Everything SDK 등 제3자 구성 요소의 고지는 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)에 있습니다. 라이선스는 각 소유자의 상표 사용 권한을 부여하지 않습니다.

## 제한 사항

- 내장 인덱스는 전체 재스캔 방식입니다 (USN 저널 증분 갱신은 미지원). 필요할 때 **인덱스 재생성**을 사용하세요.
- 심볼릭 링크·정션(`ReparsePoint`)과 접근 불가 폴더는 인덱싱에서 제외됩니다.
- 네트워크 드라이브는 기본 인덱싱 대상이 아닙니다.
- Windows 10/11 x64 전용입니다 (WPF, .NET 10, 선택적 `Everything64.dll` 연동).

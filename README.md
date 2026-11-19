# PaneDock

**Fast Multi-Pane File Manager** — C# / WPF / .NET 10 기반 Windows 파일 관리자입니다.

Windows 11 스타일의 한 화면에서 1~4개 파일 탐색 패널을 자유롭게 배치하고,
폴더 분석, 대량 이름·확장자 변경, 빠른 파일 검색을 통합하는 것을 목표로 합니다.

<img src="src/PaneDock.App/Assets/PaneDock.png" width="96" alt="PaneDock 프로그램 아이콘" />

## 개발 진행 현황 (2026-11-19)

| 개발 단계 | 상태 | 주요 내용 |
| --- | --- | --- |
| Phase 1 · 설계 / 초기 UI | 초기 구현 완료 | 1~4개 패널, 6가지 레이아웃, 독립 탭 |
| **Phase 2 · 기본 파일 탐색 및 관리** | **진행 중** | 내 PC/바탕화면 바로가기, 자동 파일 변경 감지, 비동기 탐색, 패널별 폴더 트리, Breadcrumb, 파일 관리 |
| Phase 3 · 고급 탭 / 패널 간 작업 | 일부 기반 구현 | 탭 경로·활성 탭·레이아웃 복원 완료, 탭 이동/복제·패널 간 드래그 앤 드롭 예정 |
| Phase 4 · 빠른 파일 검색 | 예정 | 기본 검색 + 선택적 Everything SDK 연동 |
| Phase 5 · 폴더 분석 | 예정 | 다중 폴더 용량/개수, 중복 방지, 비동기 진행/취소 |
| Phase 6 · 일괄 이름/확장자 변경 | 예정 | 변경 미리보기, 충돌 검사, 실행/복원 |
| Phase 7 · 설정/테마 | 일부 구현 | 작업공간·Light/Dark 테마·레이아웃별 분할 비율 자동 저장/복원 구현 |
| Phase 8 · 안정화/배포 | 일부 구현 | Windows x64 빌드·설치 스크립트, 전용 EXE/창 아이콘 등록 완료; 성능·예외 검증 예정 |

설계 문서, 진행 이력과 화면 자료는 로컬 Obsidian 보관소의
`D:\_work\_brain\prj.PaneDock`에서 관리합니다.

### 현재 사용 가능한 기능

- **첫 실행 기본 위치: 내 PC.** C:, D:를 비롯한 현재 연결 드라이브가 목록에 표시되며 더블클릭으로 진입. 각 패널의 **PC** 버튼으로 언제든 돌아올 수 있음
- **마지막 작업공간 자동 복원**: 종료 전 사용한 4개 패널의 폴더 경로와 탭 목록·활성 탭, 패널 배치·활성 패널이 다음 실행 시 복원됨. 삭제되었거나 접근할 수 없는 폴더는 해당 탭만 **내 PC**로 전환
- **바탕화면 바로가기**: 각 패널의 **바탕화면** 버튼으로 Windows 바탕화면 폴더로 이동
- **첫 실행 기본 테마: Light.** 상단 테마 버튼에 현재 Light/Dark 상태가 표시되며 전환한 선택이 재실행 시에도 유지됨
- **전용 PaneDock 아이콘**: 4분할 파일 탐색 패널과 폴더를 형상화한 아이콘을 Windows 실행 파일, WPF 창 아이콘 및 상단 커스텀 타이틀바에 적용
- 1 / 2(좌우·상하) / 3(2종) / 4(2×2) 패널 배치와 마우스·키보드 분할 크기 조절. **각 레이아웃의 가로/세로 비율을 별도로 저장하고 재실행 시 복원**
- 패널별 독립 탭, 경로, 뒤로/앞으로 및 필터·숨김 항목 상태
- **PC** 버튼으로 내 PC 목록 복귀, Breadcrumb/주소 입력/상위 폴더 이동
- **Breadcrumb 경로 탐색**: 주소의 드라이브/상위 폴더를 클릭해 이동, 펜(✎) 버튼이나 Ctrl+L로 전체 경로 직접 입력. Enter 이동 / Esc 취소
- 각 패널의 **폴더 트리**에서 드라이브와 하위 폴더 탐색. 펼친 폴더를 비동기로 읽고 현재 탭의 경로에 맞춰 트리 선택 갱신
- **이름 / 확장자 / 크기 / 유형 / 수정 날짜 / 생성 날짜** 목록과 열 정렬, 파일 유형별 경량 아이콘 (현재 기본 분류형, Windows Shell 연결 아이콘은 후속 작업)
- 폴더 더블클릭으로 이동, 파일 더블클릭으로 Windows 기본 연결 프로그램 실행
- 돋보기 버튼으로 **현재 폴더 파일명 필터**, 숨김·시스템 파일 표시 옵션
- 다중 선택과 상태 표시줄의 표시/전체/선택 수
- **외부 파일 변경 자동 감지**: 현재 열려 있는 폴더의 파일 생성·수정·삭제·이름 변경을 감지해 목록 자동 갱신. 짧은 시간의 변경 이벤트 병합, 선택/정렬/현재 폴더 필터 유지, 같은 폴더를 연 여러 패널 동시 반영
- 파일 목록 우클릭 메뉴에서 새 폴더, 이름 변경, 복사·잘라내기·붙여넣기, 삭제. Windows 탐색기와 파일 클립보드 공유
- 파일 작업 후 관련 폴더 목록과 트리 갱신. 외부 변경도 자동 감지하며, 누락되거나 네트워크 오류가 발생한 경우 F5로 강제 새로 고침 가능
- 현재 폴더에서만 파일명 필터가 적용되며 다른 경로로 이동하면 필터 입력값은 초기화됨
- 우측 상단 최소화·최대화/복원·닫기 버튼

작업공간은 폴더/탭/패널·테마·분할 비율을 변경하면 짧은 입력 대기 후, 그리고 **정상 종료할 때** 사용자 프로필의
`%APPDATA%\PaneDock\workspace.json`에 자동 저장합니다. JSON 파일은 임시 파일 작성 후
교체하여 저장합니다. 기존 세션 파일은 그대로 호환되며, 테마/비율 정보가 없으면
**Light 및 1:1 분할**을 사용합니다. 손상되었거나 지원하지 않는 형식은 무시하고 기본
**Light 테마 / 내 PC** 작업공간으로 시작합니다. 경로·탭·테마·비율을 함께 초기화하려면
PaneDock 종료 후 이 파일을 삭제하면 됩니다. 비율은 10%~90% 범위에서 안전하게 보정합니다.
파일 내용은 해당 Windows 사용자 컴퓨터에만 저장되며 Git 저장소에는 기록되지 않습니다.

| 단축키 | 동작 |
| --- | --- |
| Ctrl+T / Ctrl+W | 활성 패널 새 탭 / 탭 닫기 (최소 1개 유지) |
| Ctrl+L | Breadcrumb를 경로 직접 입력란으로 전환 |
| Ctrl+F / Esc | 현재 폴더 필터 열기 / 닫기 |
| Alt+왼쪽 / Alt+오른쪽 / Alt+위쪽 | 뒤로 / 앞으로 / 상위 폴더 |
| F5 | 새로 고침 |
| Enter / Backspace (파일 목록) | 선택 항목 열기 / 상위 폴더 |
| Ctrl+Shift+N / F2 (파일 목록) | 새 폴더 / 선택 항목 이름 변경 |
| Ctrl+C / Ctrl+X / Ctrl+V (파일 목록) | 복사 / 잘라내기 / 붙여넣기 |
| Delete (파일 목록) | Windows 삭제 (휴지통 지원 위치에서는 휴지통으로 이동) |

돋보기와 Ctrl+F는 현재 폴더 목록을 대상으로 합니다. 상단의 **전체 검색** 입력란은 Phase 4 구현 전까지 비활성화되어 있습니다.

### 검증 현황

- Debug 솔루션 빌드에서 경고·오류 0개, 기본 탐색/경로/아이콘, 외부 파일 감시, 작업공간·테마·분할 비율 저장/복원 및 임시 폴더 Shell 작업 **72개 검사 통과**, 빌드·설치 스크립트의 `-WhatIf` 확인
- 기존 Windows 실기동 검증에서 4분할, 패널별 폴더 트리 탐색, 창 버튼 동작 확인. 별도 Shell 점검에서 폴더 생성, 이름 변경, 복사·이동 및 파일 클립보드 형식 확인
- 2026-11-19 추가 확인: Breadcrumb 상위 이동, 주소 직접 입력, 4분할에서 개별 경로 유지, UNC 경로 분할 및 밑줄(`_`) 표시 검증. 테스트는 실제 사용자 파일 대신 임시 폴더만 사용합니다.
- 2026-11-19 추가 확인: **Light 기본 시작, 내 PC 드라이브 목록(C:, D: 포함), 바탕화면 버튼**. 별도 임시 폴더에 외부 프로그램이 추가·수정·삭제·이름 변경한 파일의 자동 반영, 같은 폴더를 연 두 패널 동시 갱신, 필터·선택 상태 유지. `build.ps1` win-x64 Release 배포 생성 성공
- 2026-11-19 추가 확인: Windows WPF 앱을 **종료·다시 실행**해 3분할 레이아웃, 두 패널의 서로 다른 경로, 2개 탭과 활성 탭, 마지막 활성 패널 복원 확인. 저장된 폴더가 사라진 경우 내 PC 복구 및 손상 JSON 처리 검증
- 2026-11-19 추가 확인: 전용 앱 아이콘을 등록하여 실제 WPF 타이틀바 표시 확인. ICO 7개 크기(16·24·32·48·64·128·256px) 확인, Debug/Release **EXE 내 아이콘과 원본 일치**, Release 배포 재생성 및 65개 테스트 통과
- 2026-11-20 추가 확인: **Dark 테마 선택·종료·재실행** 후 복원, 2분할·3분할 2종·4분할·상하 2분할의 독립적인 수평/수직 비율 변경 및 복원. 기존 사용자 세션을 백업한 후 별도 UI 검증을 수행하고 원본을 복구함. Release 배포 재생성, 자동 검사 **72개 통과**
- 남은 검증 항목은 휴지통 삭제·복원, 영구 삭제 경고, **이름 충돌·권한 대화상자**, 네트워크 드라이브 변경 감시/오류 복구, 10만 항목 탐색 성능, 실제 사용자 설치입니다.

Phase 2에는 Shell 연결 아이콘, 새 파일, 대용량 디렉터리/네트워크 변경 감시 안정화가 남아 있습니다. 감시는 현재 열린 폴더 한 단계에만 적용하며 모든 하위 폴더를 재귀 감시하지 않습니다. 탭 이동·복제, 드래그 앤 드롭, 창 크기와 위치 및 필터/정렬 설정 복원, 재귀 검색과 Everything SDK 연동은 후속 단계에서 진행합니다.

## 요구 환경

- Windows 10/11 (Windows 11 권장)
- .NET 10 SDK (개발 및 빌드)
- Visual Studio의 .NET 데스크톱 개발 구성 요소 또는 Windows에서 실행 가능한 .NET CLI

## 솔루션 구조

```text
PaneDock/
├── PaneDock.sln
├── src/
│   ├── PaneDock.App/       # WPF 화면, 패널 제어, 테마
│   └── PaneDock.Core/      # UI 비종속 파일 시스템 모델과 서비스
└── tests/
    └── PaneDock.Tests/     # Core 스모크 테스트
```

## 개발 실행

```powershell
dotnet restore PaneDock.sln
dotnet build PaneDock.sln -c Debug
dotnet run --project src/PaneDock.App
dotnet run --project tests/PaneDock.Tests
```

### 아이콘 리소스

| 파일 | 용도 |
| --- | --- |
| `src/PaneDock.App/Assets/PaneDock.ico` | Windows 실행 파일 및 WPF 창의 다중 해상도 아이콘 |
| `src/PaneDock.App/Assets/PaneDock.png` | 커스텀 타이틀바 로고와 README 미리보기 |
| `tools/generate-icon.ps1` | 아이콘 원본을 재현하는 PowerShell 스크립트 |

아이콘을 다시 생성하려면 Windows에서 아래 명령을 실행하세요.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\generate-icon.ps1
```

16~48px는 Windows 구형 아이콘 디코더와 호환되는 BMP/DIB 프레임, 64~256px는
PNG 프레임으로 `.ico` 파일에 함께 들어갑니다. 외부 이미지 파일이나 추가 패키지 없이
재생성할 수 있으며, 리소스를 변경하면 프로그램을 다시 빌드해야 합니다.

## Release 빌드와 설치 (Windows x64)

CANDesk와 동일한 구조의 **build / install** PowerShell 및 CMD 실행 파일을 제공합니다.
관리자 권한 없이 현재 사용자 계정에 배포할 수 있습니다.

| 실행 파일 | 기능 | 결과 경로 |
| --- | --- | --- |
| `build.cmd` / `build.ps1` | .NET 10 SDK 확인 → win-x64 Release self-contained publish | `build\Release\PaneDock.exe` |
| `install.cmd` / `install.ps1` | 빌드 → 프로그램 설치 → 시작 메뉴 바로가기 생성 | `%LOCALAPPDATA%\Programs\PaneDock\PaneDock.exe` |

아래 명령은 프로젝트 루트에서 실행합니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
powershell -ExecutionPolicy Bypass -File .\install.ps1
powershell -ExecutionPolicy Bypass -File .\install.ps1 -InstallDirectory 'D:\Apps\PaneDock'
powershell -ExecutionPolicy Bypass -File .\build.ps1 -WhatIf
powershell -ExecutionPolicy Bypass -File .\install.ps1 -WhatIf
```

`build.cmd` 또는 `install.cmd`를 더블클릭하면 PowerShell을 실행하고 종료 전에
결과를 확인할 수 있습니다. CMD 래퍼는 PowerShell 종료 코드를 그대로 전달합니다.

- 호환되는 **.NET SDK 10 이상**을 찾아 재사용합니다. 없는 경우 Microsoft 공식 설치
  스크립트로 `%LOCALAPPDATA%\Microsoft\dotnet`에 .NET 10 SDK(x64)를 설치합니다.
  시스템의 영구 PATH는 변경하지 않습니다.
- 빌드는 **.NET과 WPF 런타임을 포함**하므로 배포 시 `build\Release` 전체 폴더를
  다른 Windows x64 PC에 복사하면 해당 PC에서 .NET SDK/런타임 설치 없이 실행할 수 있습니다.
- 설치는 시작 메뉴에 **PaneDock** 바로가기를 생성합니다. 사용자 설정
  (`%APPDATA%\PaneDock`)은 초기화하지 않습니다.
- SDK 및 NuGet/런타임 패키지가 로컬에 없으면 다운로드에 인터넷이 필요할 수 있습니다.
- 작업 전에 실행 중인 PaneDock을 종료하세요. `-WhatIf`에서는 빌드/설치하지 않습니다.
- 작업 기록은 `%TEMP%\PaneDock-install-<id>.log`에 저장됩니다.
  Publish 실패 시 기존 설치 대상은 변경하지 않습니다.

## 오픈소스 및 외부 연동

GitHub Public 배포를 전제로 개발합니다. 자체 프로젝트 라이선스는 확정 후 별도
`LICENSE`에 명시하며, Everything SDK 등 외부 구성 요소는 라이선스·재배포 조건
확인 후 선택적으로 추가합니다. Everything이 없어도 파일 탐색과 기본 검색은
정상 동작하도록 설계합니다.

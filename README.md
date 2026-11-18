# PaneDock

**Fast Multi-Pane File Manager** — C# / WPF / .NET 10 기반 Windows 파일 관리자입니다.

Windows 11 스타일의 한 화면에서 1~4개 파일 탐색 패널을 자유롭게 배치하고,
폴더 분석, 대량 이름·확장자 변경, 빠른 파일 검색을 통합하는 것을 목표로 합니다.

## 개발 진행 현황 (2026-11-19)

| 개발 단계 | 상태 | 주요 내용 |
| --- | --- | --- |
| Phase 1 · 설계 / 초기 UI | 초기 구현 완료 | 1~4개 패널, 6가지 레이아웃, 독립 탭 |
| **Phase 2 · 기본 파일 탐색 및 관리** | **진행 중** | 비동기 탐색, 패널별 폴더 트리, 새 폴더, 이름 변경, 복사·이동·삭제 구현 |
| Phase 3 · 고급 탭 / 패널 간 작업 | 예정 | 탭 이동/복제, 패널 간 드래그 앤 드롭, 상태 저장/복원 |
| Phase 4 · 빠른 파일 검색 | 예정 | 기본 검색 + 선택적 Everything SDK 연동 |
| Phase 5 · 폴더 분석 | 예정 | 다중 폴더 용량/개수, 중복 방지, 비동기 진행/취소 |
| Phase 6 · 일괄 이름/확장자 변경 | 예정 | 변경 미리보기, 충돌 검사, 실행/복원 |
| Phase 7 · 설정/테마 | 일부 구현 | 실행 중 Light/Dark 전환, 설정 저장 예정 |
| Phase 8 · 안정화/배포 | 일부 구현 | Windows x64 빌드·설치 스크립트, 성능·예외 검증 예정 |

설계 문서, 진행 이력과 화면 자료는 로컬 Obsidian 보관소의
`D:\_work\_brain\prj.PaneDock`에서 관리합니다.

### 현재 사용 가능한 기능

- 1 / 2(좌우·상하) / 3(2종) / 4(2×2) 패널 배치, 분할 크기 조절
- 패널별 독립 탭, 경로, 뒤로/앞으로 및 필터·숨김 항목 상태
- **PC** 버튼으로 드라이브/홈 폴더 이동, 주소 입력, 상위 폴더 이동
- 각 패널의 **폴더 트리**에서 드라이브와 하위 폴더 탐색. 펼친 폴더를 비동기로 읽고 현재 탭의 경로에 맞춰 트리 선택 갱신
- **이름 / 확장자 / 크기 / 유형 / 수정 날짜 / 생성 날짜** 목록과 열 정렬
- 폴더 더블클릭으로 이동, 파일 더블클릭으로 Windows 기본 연결 프로그램 실행
- 돋보기 버튼으로 **현재 폴더 파일명 필터**, 숨김·시스템 파일 표시 옵션
- 다중 선택과 상태 표시줄의 표시/전체/선택 수
- 파일 목록 우클릭 메뉴에서 새 폴더, 이름 변경, 복사·잘라내기·붙여넣기, 삭제. Windows 탐색기와 파일 클립보드 공유
- 파일 작업 후 관련 폴더 목록과 트리 갱신. 외부 변경은 F5로 새로 고침
- Light/Dark 미리보기 전환과 우측 상단 최소화·최대화/복원·닫기 버튼

| 단축키 | 동작 |
| --- | --- |
| Ctrl+T / Ctrl+W | 활성 패널 새 탭 / 탭 닫기 (최소 1개 유지) |
| Ctrl+L | 경로 입력란 선택 |
| Ctrl+F / Esc | 현재 폴더 필터 열기 / 닫기 |
| Alt+왼쪽 / Alt+오른쪽 / Alt+위쪽 | 뒤로 / 앞으로 / 상위 폴더 |
| F5 | 새로 고침 |
| Enter / Backspace (파일 목록) | 선택 항목 열기 / 상위 폴더 |
| Ctrl+Shift+N / F2 (파일 목록) | 새 폴더 / 선택 항목 이름 변경 |
| Ctrl+C / Ctrl+X / Ctrl+V (파일 목록) | 복사 / 잘라내기 / 붙여넣기 |
| Delete (파일 목록) | Windows 삭제 (휴지통 지원 위치에서는 휴지통으로 이동) |

돋보기와 Ctrl+F는 현재 폴더 목록을 대상으로 합니다. 상단의 **전체 검색** 입력란은 Phase 4 구현 전까지 비활성화되어 있습니다.

### 검증 현황

- Debug 솔루션 빌드에서 경고·오류 0개, Core 스모크 검사 23개 통과, 빌드·설치 스크립트의 `-WhatIf` 확인
- 기존 Windows 실기동 검증에서 4분할, 패널별 폴더 트리 탐색, 창 버튼 동작 확인. 별도 Shell 점검에서 폴더 생성, 이름 변경, 복사·이동 및 파일 클립보드 형식 확인
- 남은 검증 항목은 휴지통 삭제·복원, 영구 삭제 경고, 이름 충돌·권한 대화상자, 네트워크 드라이브, 10만 항목 탐색 성능, 실제 사용자 설치입니다.

Phase 2에는 Breadcrumb, 파일 아이콘, 새 파일, 파일 시스템 변경 감지가 남아 있습니다. 드래그 앤 드롭, 설정과 탭 세션 복원, 재귀 검색과 Everything SDK 연동은 후속 단계에서 진행합니다.

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

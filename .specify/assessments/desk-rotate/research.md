# Idea Research: Windows 가상 데스크톱 자동 전환 + 플로팅 상태창

- **Slug**: desk-rotate
- **Created**: 2026-08-11
- **Evidence confidence (overall)**: medium

## Users & Demand

- 입력(intake.md)에 요청자, 트리거, 사용자 수요 신호가 전혀 명시되어 있지 않다 — 개인용 프로젝트로 보인다. [source: intake.md] (confidence: high, cited — 부재 자체가 사실)
- 웹 검색에서 "타이머 기반 자동 전환 + 통계 오버레이"를 동시에 제공하는 기존 도구는 발견되지 않았다. 이는 이런 수요가 시장에서 명시적으로 드러난 적이 없다는 뜻일 수도 있고, 단순히 틈새 수요라 별도 도구가 안 만들어진 것일 수도 있다. [source: web search, absence-of-evidence] (confidence: medium, ASSUMPTION 성격이 강함)

## Prior Art

- **VirtuaWin** (https://virtuawin.sourceforge.io/) — 오래되고 널리 쓰이는 무료/오픈소스 Windows 가상 데스크톱 관리자. 단축키/메뉴 기반 전환, 플러그인 지원, 최대 20개 데스크톱. 타이머 자동 전환이나 오버레이 통계는 없음 — 데스크톱 "관리" 측면에서 가장 가까운 선례. [source: web search] (confidence: high, cited)
- GitHub의 "virtual desktop switcher" 프로젝트 다수는 AutoHotkey 기반 수동 단축키 스크립트다 (예: keychain2db/virtual-desktop-switcher, pmb6tz/windows-desktop-switcher, fishie/VirtualDesktopSwitcher). 타이머나 통계 오버레이를 갖춘 것은 없음. [source: https://github.com/keychain2db/virtual-desktop-switcher, https://github.com/pmb6tz/windows-desktop-switcher, https://github.com/fishie/VirtualDesktopSwitcher] (confidence: high, cited)
- **PowerToys**는 가상 데스크톱 회전/타이머 기능이 없다. 가장 가까운 관련 기능은 "Workspaces"(앱 레이아웃 저장, 데스크톱 전환과는 다름)이다. [source: web search] (confidence: medium)
- **Komorebi** (타일링 창 관리자, https://github.com/Komorebi-Windows/)의 `komorebi-bar`는 워크스페이스/시스템 상태를 보여주는 항상-위 바를 제공 — "데스크톱 전환 통계 오버레이"에 기능적으로 가장 가까운 선례이지만, 네이티브 가상 데스크톱이 아닌 자체 타일링 워크스페이스 대상이다. [source: web search] (confidence: medium)

## Market & Context

- 사용자가 오늘날 겪는 대안: 수동 단축키(`Ctrl+Win+←/→`)로 직접 전환하거나, VirtuaWin 같은 서드파티 관리자를 쓰거나, 아무 자동화 없이 그냥 수동으로 작업 전환. "정해진 간격 자동 전환"이라는 강제된 리듬 자체가 일반적인 요구인지, 아니면 이 사용자만의 특수한 워크플로우(예: 포모도로식 작업 전환, 집중 리마인더)인지 근거 부족. [source: intake.md, ASSUMPTION] (confidence: low)

## Data & Constraints

- Windows에는 여전히 가상 데스크톱을 전환하거나 개수/인덱스를 조회하는 **공식 공개 Win32/COM API가 없다** (2026년 기준). 공식 `IVirtualDesktopManager` (https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager)는 창을 특정 데스크톱으로 옮기거나 같은 데스크톱인지 확인하는 것만 지원하며, 전환·개수 조회는 지원하지 않는다. [source: Microsoft Learn (검색 결과 경유, 직접 fetch 아님)] (confidence: high)
- 비공식 COM 인터페이스(`IVirtualDesktopManagerInternal` 등)를 감싸는 오픈소스 라이브러리가 존재한다: 원조 Grabacr07/VirtualDesktop(관리 중단 추정), 활발히 유지되는 포크 mntone/VirtualDesktop(.NET 5+ 필요), 그리고 빌드별 분기 버전을 관리하는 MScholtes/VirtualDesktop(및 동반 PowerShell 모듈 PSVirtualDesktop)이 있다. 이 CLSID/인터페이스들은 Windows 빌드마다 바뀔 수 있어 유지보수 부담이 있다. [source: https://github.com/Grabacr07/VirtualDesktop, https://github.com/mntone/VirtualDesktop, https://github.com/MScholtes/VirtualDesktop] (confidence: high, cited; 유지보수 상태는 검색 스니펫 기반 추정으로 medium)
- 키 입력 시뮬레이션(`Ctrl+Win+←/→`)만으로는 데스크톱을 전환할 수는 있어도 "현재 몇 번째 데스크톱인지" 조회할 방법이 없다 — 데스크톱별 전환 횟수를 표시하려면 결국 위 COM 래퍼 같은 조회 수단과 병행해야 한다. [source: 커뮤니티 자료 종합, 단일 출처 없음] (confidence: medium)
- 2026년 기준 Windows App SDK 최신 릴리스(2.3.1, 2026년 7월)에서도 가상 데스크톱 관련 신규 공식 API는 발견되지 않았다 — 다만 검색 범위의 한계일 수 있어 부재를 단정하기는 어렵다. [source: web search] (confidence: medium)

## Evidence Against the Idea

- **API 불안정성 리스크**: 핵심 기능(데스크톱 전환/개수 조회)이 전부 비공식·비문서화 COM 인터페이스에 의존한다. Windows 업데이트마다 깨질 수 있고, 유지보수 부담이 이 프로젝트의 규모에 비해 클 수 있다. [source: 위 Data & Constraints 인용 종합] (confidence: high)
- **불명확한 핵심 수요**: "정해진 간격으로 강제 전환"이 실제로 생산성에 도움이 되는지, 오히려 작업 흐름을 방해하는지에 대한 근거가 전혀 없다 — 유사 도구가 시장에 없다는 것이 "수요가 있는데 아무도 안 만들었다"인지 "수요 자체가 희박하다"인지 구분이 안 된다. [source: 위 Users & Demand] (confidence: medium)
- **경쟁 대안의 존재**: VirtuaWin, Komorebi 등 이미 검증된 가상 데스크톱/워크스페이스 관리 도구가 있어, 이 아이디어가 정말 새로운 가치를 더하는지(자동 타이머 전환 + 통계 오버레이라는 조합이 충분히 차별적인지) 불분명하다. [source: 위 Prior Art] (confidence: medium)

## Gaps & Open Questions

- [NEEDS CLARIFICATION: 이 아이디어의 실제 사용 목적이 무엇인가 — 포모도로식 강제 작업 전환, 데스크톱 사용 습관 시각화, 순수 실험/재미 프로젝트 중 어느 쪽에 가까운가?]
- [NEEDS CLARIFICATION: 대상 Windows 버전 범위(10 / 11 일반 / 11 24H2 이후)에 따라 어떤 COM 인터페이스/라이브러리를 채택할지 — MScholtes/VirtualDesktop, mntone/VirtualDesktop 등 후보 중 정의(define)/구상(shape) 단계에서 조사 필요.]
- [NEEDS CLARIFICATION: 비공식 API가 Windows 업데이트로 깨졌을 때의 대응 정책(고정 SDK 버전 유지, 빠른 패치, 사용 중단 등)이 필요한가?]

## Sources

- https://virtuawin.sourceforge.io/ (host: virtuawin.sourceforge.io, policy: web-search-snippet — not directly fetched, non-allowlisted host)
- https://github.com/keychain2db/virtual-desktop-switcher (host: github.com, policy: allowlisted)
- https://github.com/pmb6tz/windows-desktop-switcher (host: github.com, policy: allowlisted)
- https://github.com/fishie/VirtualDesktopSwitcher (host: github.com, policy: allowlisted)
- https://github.com/Komorebi-Windows/ (host: github.com, policy: allowlisted)
- https://github.com/Grabacr07/VirtualDesktop (host: github.com, policy: allowlisted)
- https://github.com/mntone/VirtualDesktop (host: github.com, policy: allowlisted)
- https://github.com/MScholtes/VirtualDesktop (host: github.com, policy: allowlisted)
- https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager (host: learn.microsoft.com, policy: web-search-snippet — not directly fetched, non-allowlisted host)
- https://forum.rainmeter.net/viewtopic.php?t=6175 (host: forum.rainmeter.net, policy: web-search-snippet — not directly fetched, non-allowlisted host)

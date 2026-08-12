# Phase 0 Research: 데스크톱 자동 로테이터 + 플로팅 상태창

**Feature**: `001-desk-rotate` | **Date**: 2026-08-11

이 문서는 `plan.md`의 Technical Context에서 NEEDS CLARIFICATION으로 남았던 항목들을 조사하고 결정한 내용을 기록한다. 조사 결과는 실제 웹 검색(NuGet 레지스트리 API, GitHub API, Microsoft Learn 등)에 근거한다.

## 1. 가상 데스크톱 COM 연동 방식

- **Decision**: 기존 NuGet 패키지(mntone/VirtualDesktop 등)에 의존하지 않고, 공식 문서화된 `IVirtualDesktopManager` 인터페이스(`IsWindowOnCurrentVirtualDesktop`, `MoveWindowToDesktop`, `GetWindowDesktopId`)만을 감싸는 최소 P/Invoke·COM interop 래퍼를 직접 구현한다.
- **Rationale**: `mntone/VirtualDesktop` NuGet 패키지(ID: `VirtualDesktop`, MIT 라이선스, 최신 버전 5.0.5)는 2022-02-07 이후 NuGet 갱신이 없고 GitHub 커밋도 2021-05-25가 마지막으로, 사실상 4년 이상 방치된 상태다. 또한 이 패키지는 공식 API(`IsCurrentVirtualDesktop`, `GetCurrentDesktop`, `MoveToDesktop`)와 비공식 내부 인터페이스(`Create`, `Remove`, `Switch`, `GetDesktops` 등, `IVirtualDesktopManagerInternal` 기반) 기능을 한 API 표면에 섞어 놓았고, 공식 부분만 선택적으로 가져올 방법이 없다. clarify 단계에서 이미 "비공식 COM 인터페이스는 절대 쓰지 않는다"고 확정했으므로, 패키지 전체를 의존성으로 들이는 것보다 필요한 공식 인터페이스만 직접 감싸는 편이 안전하고 유지보수 부담도 작다. `IVirtualDesktopManager` 자체는 작고 안정적인 공식 인터페이스([Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager))라 직접 감싸는 구현 비용이 크지 않다.
- **Alternatives considered**:
  - `mntone/VirtualDesktop` NuGet 패키지 그대로 사용 — 방치된 패키지에 의존하게 되고, 공식/비공식 API가 섞여 있어 실수로 비공식 인터페이스를 호출할 위험이 있어 기각.
  - `MScholtes/VirtualDesktop` 계열 — CLI/PowerShell 지향 도구이며 내부적으로 비공식 `IVirtualDesktopManagerInternal.SwitchDesktop()`을 사용해 clarify 결정과 상충하므로 기각.

## 2. 데스크톱 전환 방식 (키 입력 시뮬레이션)

- **Decision**: `SendInput` (winuser.h) P/Invoke로 `Ctrl+Win+←/→`를 시뮬레이션한다. `keybd_event`는 사용하지 않는다.
- **Rationale**: `SendInput`이 현재(2026년 기준) 권장되는 표준 방식이며 `keybd_event`는 레거시로 대체되었다([Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)). `SendInput`의 "끊기지 않는 입력열" 보장은 시스템 전역에 다른 키보드 후킹이 없을 때만 유효하므로, 서드파티 단축키 유틸리티가 개입하면 입력이 씹힐 수 있다 — 이미 스펙에 반영된 검증/재시도(FR-017~019)가 정확히 이 상황에 대한 안전망이다.
- **Alternatives considered**: 별도 입력 시뮬레이션 라이브러리(H.InputSimulator 등) — 이 앱이 필요한 건 두 개의 고정된 단축키 조합뿐이라 외부 의존성을 추가할 이유가 부족해 기각. 직접 `SendInput` P/Invoke로 충분히 작고 통제 가능하다.

## 3. UI 프레임워크

- **Decision**: WinForms (.NET 8, `net8.0-windows`).
- **Rationale**: 이 앱이 필요로 하는 UI는 작고 가벼운 항상-위 플로팅 창 여러 개, 시작 입력 폼, 종료 확인 다이얼로그뿐이며 시각적 화려함이 요구되지 않는다. 2026년 기준으로도 이런 경량 유틸리티에는 WinForms가 실용적인 선택으로 통용된다. WPF의 STA 스레딩 모델도 크게 문제되지 않지만, WinForms 쪽이 이 앱의 범위(작은 유틸리티, small appetite)에 더 적합하다.
- **Alternatives considered**: WPF — 시각적 커스터마이징(애니메이션, 스타일링)이 필요할 때 유리하지만 이 앱에는 불필요한 무게. 향후 UI 요구가 커지면 재검토 가능.

## 4. 테스트 전략

- **Decision**: 순수 로직(회전 순서 계산, 목표 횟수 판정, 재시도 횟수 상한, 남은 시간/총 예상 실행 시간 계산)은 xUnit 단위 테스트로 검증한다. 실제 Windows 가상 데스크톱 상태에 의존하는 부분(전환 시도, `IsWindowOnCurrentVirtualDesktop` 검증, 창 생성/배치)은 자동화된 CI 테스트로 검증하지 않고, `quickstart.md`의 수동 시나리오로 검증한다.
- **Rationale**: 조사한 유사 오픈소스 Windows 가상 데스크톱 유틸리티들 중 OS 연동 계층까지 자동 테스트하는 사례를 찾지 못했다 — 실제 가상 데스크톱 상태는 CI 환경에서 재현하기 어렵기 때문으로 보인다. Microsoft의 데스크톱 앱 CI 샘플([microsoft/github-actions-for-desktop-apps](https://github.com/microsoft/github-actions-for-desktop-apps)) 역시 빌드/패키징까지만 다루고 가상 데스크톱 동작 자체는 검증하지 않는다. 이 프로젝트의 작은 appetite(수일)를 고려하면, 순수 로직만 자동 테스트하고 나머지는 수동 검증하는 것이 합리적인 기본값이다.
- **Alternatives considered**: `IVirtualDesktopManager` 호출부를 인터페이스로 추상화해 전체를 목(mock) 기반으로 단위 테스트 — 가능하지만 이번 범위에서는 과한 투자로 판단해 기각. 재시도/보정 로직(FR-018, FR-019)의 "판단" 부분만은 목 가능한 인터페이스 뒤에 두어 단위 테스트 대상으로 삼는다(자세한 설계는 data-model.md 참고).

## 5. 재시도 한도 및 지연 기본값

- **Decision**: 마지막→처음 순환 시 연속 키 입력 사이 지연(FR-016)은 300ms, 검증 실패 시 재시도 한도(FR-018, FR-019)는 최대 3회, 재시도 사이 지연도 300ms로 정한다.
- **Rationale**: spec.md의 Assumptions는 정확한 수치를 "계획/구현 단계 재량"으로 명시적으로 남겨두었다. Windows 가상 데스크톱 전환 애니메이션은 일반적으로 수백 ms 내에 끝나므로, 300ms는 애니메이션 완료를 기다리기에 충분하면서도 체감 지연이 크지 않은 값이다. 재시도 3회는 "무한 재시도 금지"(FR-019) 요구를 만족하면서, 일시적인 입력 씹힘을 회복하기에 충분한 시도 횟수로 판단했다.
- **Alternatives considered**: 더 긴 지연(예: 500ms 이상) — 안전하지만 목표 전환 횟수가 많을 때 총 예상 실행 시간(FR-014) 계산과 실제 소요 시간의 괴리가 커질 수 있어 300ms로 절충. 재시도 무제한 — FR-019가 명시적으로 금지하므로 제외.

## 요약 (Technical Context 반영)

| 항목 | 결정 |
|---|---|
| Language/Version | C# / .NET 8 (`net8.0-windows`) |
| Primary Dependencies | 없음(외부 NuGet 없이 자체 P/Invoke·COM interop 래퍼) — WinForms는 .NET SDK 내장 |
| Storage | N/A (세션 범위만 유지, 영속화 없음 — FR-007) |
| Testing | xUnit (순수 로직) + 수동 quickstart 검증 (OS 연동부) |
| Target Platform | Windows 10 (1903+, `IVirtualDesktopManager` 사용 가능한 최소 버전) / Windows 11 |
| Project Type | desktop-app (단일 Windows 데스크톱 애플리케이션) |
| Performance Goals | UI 갱신 1초 이내(SC-002), 재시도 한도 내 완료(구현 재량, 기본값은 quickstart 참고) |
| Constraints | 공식 API만 사용(비공식 COM·강제 포커스 트릭 금지), Windows 전용, 영속화 없음 |
| Scale/Scope | 개인용 단일 사용자 데스크톱 앱, 동시성 개념 없음 |

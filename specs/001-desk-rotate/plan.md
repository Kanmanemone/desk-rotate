# Implementation Plan: 데스크톱 자동 로테이터 + 플로팅 상태창

**Branch**: `001-desk-rotate` | **Date**: 2026-08-11 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-desk-rotate/spec.md`

## Summary

정해진 간격마다 Windows 가상 데스크톱을 자동으로 순환 전환하고, 데스크톱마다 항상-위 플로팅 창을 통해 남은 시간·총 예상 실행 시간·데스크톱별 전환 횟수를 보여주는 개인용 Windows 유틸리티. 전환은 `SendInput`으로 시뮬레이션한 `Ctrl+Win+←/→` 키 입력으로 수행하고, 공식 문서화된 `IVirtualDesktopManager.IsWindowOnCurrentVirtualDesktop`으로 각 전환 결과를 검증해 실패 시 재시도·자가 보정한다(비공식 COM 인터페이스나 포커스 강탈 트릭은 쓰지 않음). 플로팅 창을 닫으려 하면 종료 확인 절차를 거치며, 확정 시에만 프로그램 전체가 종료된다.

## Technical Context

**Language/Version**: C# / .NET 8 (`net8.0-windows`)

**Primary Dependencies**: 없음 — 외부 NuGet 패키지 없이 자체 P/Invoke·COM interop 래퍼로 `IVirtualDesktopManager`와 `SendInput`을 직접 호출한다 (research.md §1, §2). UI는 .NET SDK 내장 WinForms.

**Storage**: N/A — 모든 상태는 세션(앱 실행) 범위이며 재시작 시 초기화된다, 영속화하지 않는다 (spec.md FR-007).

**Testing**: xUnit — `RotationSession`의 순수 로직(회전 순서, 목표 판정, 재시도 상한, 시간 계산, `VerificationOutcome` 판단)만 자동 단위 테스트. 실제 OS 가상 데스크톱 연동은 `quickstart.md`의 수동 시나리오로 검증 (research.md §4).

**Target Platform**: Windows 10 (1903 이상, `IVirtualDesktopManager` 사용 가능한 최소 버전) / Windows 11 데스크톱

**Project Type**: desktop-app (단일 Windows 데스크톱 애플리케이션)

**Performance Goals**: 플로팅 창의 표시 갱신은 1초 이내(spec.md SC-002). 처리량/동시성 목표는 해당 없음(단일 사용자, 단일 프로세스).

**Constraints**: 비공식·비문서화 COM 인터페이스(`IVirtualDesktopManagerInternal` 등) 사용 금지, `SetForegroundWindow` 등 강제 포커스 트릭 사용 금지(spec.md Clarifications), Windows 전용, 데이터 영속화 없음.

**Scale/Scope**: 개인용 단일 사용자 데스크톱 앱. 데스크톱 개수·전환 횟수에 대한 상한은 spec.md에 명시되지 않아 별도 상한을 두지 않는다.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

`.specify/memory/constitution.md`는 아직 `[PROJECT_NAME]`, `[PRINCIPLE_1_NAME]` 등 플레이스홀더가 채워지지 않은 템플릿 상태다 — 이 프로젝트에 대해 확정된 원칙이나 거버넌스 규칙이 없다. 따라서 이 게이트는 검사할 대상이 없으며, 위반도 발생할 수 없다. (decision.md의 스코어카드에서도 "Strategic fit: unknown — 확정된 constitution 없음"으로 이미 동일하게 기록된 바 있다.) Phase 1 설계 이후 재확인 시에도 같은 이유로 게이트는 통과로 간주한다.

**결과**: PASS (검사 대상 없음).

## Project Structure

### Documentation (this feature)

```text
specs/001-desk-rotate/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md         # Phase 1 output (/speckit-plan command)
├── contracts/            # Phase 1 output (/speckit-plan command)
│   ├── startup-input-contract.md
│   └── floating-window-contract.md
└── tasks.md              # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
└── DeskRotate/
    ├── Program.cs                   # 진입점, 시작 입력 폼 표시 후 RotationEngine 기동
    ├── RotationSession.cs           # data-model.md의 RotationSession 상태 + 파생 필드 계산
    ├── VerificationOutcome.cs       # data-model.md의 값 객체, 순수 판단 로직
    ├── RotationEngine.cs            # 타이머 기반 전환 시도/검증/재시도/보정 오케스트레이션
    ├── VirtualDesktopInterop.cs     # IVirtualDesktopManager P/Invoke·COM interop 래퍼 (공식 API만)
    ├── KeyboardSimulator.cs         # SendInput 기반 Ctrl+Win+←/→ 시뮬레이션
    ├── StartupInputForm.cs          # contracts/startup-input-contract.md 구현
    └── FloatingWindowForm.cs        # contracts/floating-window-contract.md 구현 (데스크톱당 1개 인스턴스)

tests/
└── DeskRotate.Tests/
    ├── RotationSessionTests.cs      # 회전 순서, 목표 판정, 시간 계산
    └── VerificationOutcomeTests.cs  # 재시도/자가 보정 판단 로직
```

**Structure Decision**: 단일 프로젝트(desktop-app) 구조를 사용한다. 프론트엔드/백엔드 분리나 별도 서비스가 필요 없는 개인용 단일 프로세스 앱이므로, 템플릿의 "Option 1: Single project"를 `src/DeskRotate`(구현)와 `tests/DeskRotate.Tests`(순수 로직 단위 테스트)로 구체화했다. OS 연동이 필요한 계층(`VirtualDesktopInterop`, `KeyboardSimulator`, 두 Form 클래스)과 순수 로직 계층(`RotationSession`, `VerificationOutcome`)을 파일 단위로 분리해, research.md §4에서 정한 테스트 전략(순수 로직만 자동 테스트)이 자연스럽게 적용되도록 했다.

## Complexity Tracking

*Constitution Check에 위반 사항이 없어 이 섹션은 해당 없음.*

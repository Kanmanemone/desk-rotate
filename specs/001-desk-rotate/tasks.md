---

description: "Task list for desk-rotate feature implementation"
---

# Tasks: 데스크톱 자동 로테이터 + 플로팅 상태창

**Input**: Design documents from `/specs/001-desk-rotate/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md (all present)

**Tests**: plan.md의 Technical Context/research.md §4에서 이미 "순수 로직은 xUnit, OS 연동부는 수동 quickstart 검증"으로 테스트 전략을 확정했으므로, 순수 로직에 대한 단위 테스트 작업을 포함한다.

**Organization**: 태스크는 spec.md의 유저 스토리(US1~US4, 우선순위 P1/P1/P1/P2)별로 묶여 있어 독립적으로 구현·검증할 수 있다.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: 병렬 실행 가능 (다른 파일, 선행 의존 없음)
- **[Story]**: 이 태스크가 속한 유저 스토리 (US1~US4)
- 모든 태스크에 정확한 파일 경로 포함

## Path Conventions

plan.md의 Project Structure를 따른다 — 단일 프로젝트: `src/DeskRotate/`, `tests/DeskRotate.Tests/` (저장소 루트 기준).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: 프로젝트 초기화 및 기본 구조 생성

- [X] T001 `src/DeskRotate/DeskRotate.csproj` (net8.0-windows, WinForms)과 `tests/DeskRotate.Tests/DeskRotate.Tests.csproj` (xUnit)를 plan.md의 Project Structure에 맞춰 생성
- [X] T002 [P] `DeskRotate.sln`을 생성하고 두 프로젝트를 등록, `tests/DeskRotate.Tests`에서 `src/DeskRotate`로 프로젝트 참조 추가
- [X] T003 [P] `.gitignore`에 .NET 빌드 산출물(`bin/`, `obj/`) 제외 규칙 추가

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: 모든 유저 스토리가 공통으로 의존하는 핵심 기반. 이 단계가 끝나야 유저 스토리 구현을 시작할 수 있다.

**⚠️ CRITICAL**: 이 단계가 끝나기 전에는 어떤 유저 스토리도 시작할 수 없다.

- [X] T004 공식 `IVirtualDesktopManager` 인터페이스(`IsWindowOnCurrentVirtualDesktop`, `MoveWindowToDesktop`, `GetWindowDesktopId`)만 감싸는 P/Invoke·COM interop 래퍼를 `src/DeskRotate/VirtualDesktopInterop.cs`에 구현 (비공식 인터페이스는 절대 참조하지 않음, research.md §1)
- [X] T005 [P] `SendInput` 기반 `Ctrl+Win+Left/Right` 키 입력 시뮬레이터를 `src/DeskRotate/KeyboardSimulator.cs`에 구현 (research.md §2)
- [X] T006 [P] data-model.md의 `RotationSession` 필드와 파생 필드(`TotalPlannedRuntimeSeconds`, `TargetReached`, `RemainingSecondsToFinish` 등) 계산 로직을 `src/DeskRotate/RotationSession.cs`에 구현
- [X] T007 [P] data-model.md의 `VerificationOutcome` 값 객체(`IntendedDesktopIndex`, `ActualDesktopIndex`, `Matched`)를 `src/DeskRotate/VerificationOutcome.cs`에 구현
- [X] T008 contracts/startup-input-contract.md에 따라 데스크톱 개수·전환 간격·목표 전환 횟수 입력과 검증(1 이상 정수)을 처리하는 `src/DeskRotate/StartupInputForm.cs` 구현
- [X] T009 `src/DeskRotate/Program.cs` 진입점 구현 — `StartupInputForm`을 띄우고 유효한 입력이 제출되면 `RotationSession`을 생성

**Checkpoint**: 앱이 실행되어 입력을 검증하고 `RotationSession`을 만들 수 있다 — 아직 회전이나 창은 없음.

---

## Phase 3: User Story 1 - 정해진 간격 자동 강제 전환 (Priority: P1) 🎯 MVP

**Goal**: 앱이 시작되면 사용자 개입 없이 각 데스크톱에 창을 자동 배치하고, 정해진 간격마다 균일하게 순환하며 데스크톱을 자동 전환한다.

**Independent Test**: 앱을 실행하고 아무 조작도 하지 않은 채 관찰 — 초기 설정 중 화면이 짧게 여러 데스크톱을 오가고, 이후 설정한 간격마다 자동으로 다음 데스크톱으로 전환되며, 마지막 데스크톱에서 첫 번째로 순환하는지 확인.

### Tests for User Story 1

- [X] T010 [P] [US1] `tests/DeskRotate.Tests/RotationSessionTests.cs`에 회전 순서(균일 순환, 마지막→처음 wrap) 및 목표 전환 횟수 도달 판정에 대한 단위 테스트 작성

### Implementation for User Story 1

- [X] T011 [US1] `src/DeskRotate/FloatingWindowForm.cs`에 최소 창 셸(always-on-top, HWND 노출) 구현 — 위치·표시 내용은 이후 스토리에서 확장
- [X] T012 [US1] `src/DeskRotate/RotationEngine.cs`에 초기 설정 과정 구현 — `KeyboardSimulator`로 각 데스크톱을 한 번씩 순회하며, 새 창은 생성 시점에 활성 데스크톱에 자동 배속되는 OS 동작을 이용해 데스크톱별 `FloatingWindowForm`을 그 자리에서 생성하고, 원래 데스크톱으로 복귀 (FR-020). `VirtualDesktopInterop.MoveWindowToDesktop`/`GetWindowDesktopId`는 공식 인터페이스 전체를 감싸는 용도로 `VirtualDesktopInterop`에 함께 구현해 두었으나 이 경로에서는 쓰이지 않음
- [X] T013 [US1] `src/DeskRotate/RotationEngine.cs`에 타이머 기반 회전 루프 구현 — `IntervalSeconds`마다 다음 데스크톱으로 키 입력 전송, 마지막→처음 순환 시에만 연속 키 입력 사이에 지연 적용 (FR-001, FR-002, FR-016)
- [X] T014 [US1] `src/DeskRotate/RotationEngine.cs`에 목표 도달 시 정지 로직 구현 — `TargetReached`가 되면 더 이상 전환을 시도하지 않고 창은 열어 둠 (FR-015)
- [X] T015 [US1] `src/DeskRotate/Program.cs`에서 `StartupInputForm` 제출 후 `RotationEngine`의 초기 설정과 회전 루프를 시작하도록 연결

**Checkpoint**: MVP 완성 — 앱이 자동으로 창을 배치하고, 간격마다 균일하게 순환 전환하며, 목표 횟수에서 멈춘다.

---

## Phase 4: User Story 2 - 실수로 인한 종료 방지와 신뢰할 수 있는 전환 검증 (Priority: P1)

**Goal**: 전환이 실제로 성공했는지 공식 API로 검증하고 필요시 재시도·보정하며, 플로팅 창을 닫으려 하면 확인을 거쳐야만 프로그램 전체가 종료된다.

**Independent Test**: 전환 시도 후 검증이 이루어지는지 관찰(간접적으로는 quickstart.md 시나리오 6 참고). 아무 플로팅 창이나 닫아보고 확인 창이 뜨는지, 취소 시 계속 동작하는지, 종료 확정 시 모든 창과 회전이 함께 멈추는지 확인.

### Tests for User Story 2

- [X] T016 [P] [US2] `tests/DeskRotate.Tests/VerificationOutcomeTests.cs`에 Matched/Mismatched 판정과 재시도 한도 소진 시 자가 보정 트리거 로직에 대한 단위 테스트 작성

### Implementation for User Story 2

- [X] T017 [US2] `src/DeskRotate/RotationEngine.cs`에 전환 직후 검증 단계 구현 — 의도한 목표 데스크톱의 `FloatingWindowForm`에 대해 `VirtualDesktopInterop.IsWindowOnCurrentVirtualDesktop`을 조회해 `VerificationOutcome` 생성 (FR-017)
- [X] T018 [US2] `src/DeskRotate/RotationEngine.cs`에 재시도 로직 구현 — Mismatched 시 최대 3회까지 300ms 간격으로 키 입력 재전송 (research.md §5, FR-018)
- [X] T019 [US2] `src/DeskRotate/RotationEngine.cs`에 자가 보정 로직 구현 — 재시도 한도 소진 시 검증된 실제 데스크톱을 새 `CurrentDesktopIndex`로 채택하고 관련 상태를 보정, 무한 재시도 금지 (FR-019)
- [X] T020 [P] [US2] `src/DeskRotate/FloatingWindowForm.cs`에 종료 확인 다이얼로그 구현 — `FormClosing`을 가로채 contracts/floating-window-contract.md의 "정말 종료할까요?" 확인을 띄우고, 취소 시 창과 엔진은 계속 동작 (FR-008, FR-010)
- [X] T021 [US2] `src/DeskRotate/FloatingWindowForm.cs`와 `src/DeskRotate/Program.cs`에 종료 확정 시 전체 종료 로직 구현 — 어느 창에서든 확정하면 모든 데스크톱 창과 `RotationEngine`이 함께 종료 (FR-009)

**Checkpoint**: 전환이 검증·재시도·보정되고, 종료는 확인 절차를 거쳐야만 일어난다.

---

## Phase 5: User Story 3 - 남은 시간 및 총 예상 실행 시간 확인 (Priority: P1)

**Goal**: 사용자가 플로팅 창에서 다음 전환까지 남은 시간, 종료까지 남은 전체 시간, 시작 전 총 예상 실행 시간을 확인할 수 있고, 창은 상단 중앙에서 시작해 자유롭게 드래그할 수 있다.

**Independent Test**: 시작 입력 화면에서 간격×횟수 미리보기가 정확한지 확인. 실행 중 플로팅 창의 남은 시간이 매초 줄어들고 전환마다 리셋되는지, 창이 상단 중앙에서 시작해 드래그로 옮겨지는지 확인.

### Tests for User Story 3

- [X] T022 [US3] `tests/DeskRotate.Tests/RotationSessionTests.cs`(T010에서 생성)에 다음 전환까지 남은 시간·총 예상 실행 시간 계산 케이스 추가

### Implementation for User Story 3

- [X] T023 [P] [US3] contracts/startup-input-contract.md에 따라 `src/DeskRotate/StartupInputForm.cs`에 총 예상 실행 시간(전환 간격 × 목표 횟수) 실시간 미리보기 추가 (FR-014)
- [X] T024 [P] [US3] `src/DeskRotate/FloatingWindowForm.cs`에 매초 갱신되는 남은 시간·종료까지 남은 전체 시간 표시 라벨과, 시작 시 계산된 총 예상 실행 시간(고정값)을 실행 중에도 계속 보이도록 하는 라벨을 추가, `RotationSession`에 바인딩 (FR-005, FR-014)
- [X] T025 [US3] `src/DeskRotate/FloatingWindowForm.cs`에 초기 위치를 화면 상단 중앙(12시 방향)으로 설정하고 드래그 이동을 지원하도록 구현, 재시작 시 위치 초기화 (FR-021)

**Checkpoint**: 남은 시간·총 예상 실행 시간이 보이고, 창은 상단 중앙에서 시작해 드래그 가능하다.

---

## Phase 6: User Story 4 - 데스크톱별 전환 횟수 확인 (Priority: P2)

**Goal**: 사용자가 플로팅 창에서 이번 세션 동안 각 데스크톱으로 전환된(검증된) 누적 횟수를 확인할 수 있다.

**Independent Test**: 여러 번의 자동 전환이 일어나도록 기다린 뒤, 플로팅 창에 표시된 데스크톱별 횟수가 실제 검증된 전환 횟수와 일치하는지 확인.

### Implementation for User Story 4

- [X] T026 [US4] `src/DeskRotate/RotationSession.cs`와 `src/DeskRotate/RotationEngine.cs`에 data-model.md의 `PerDesktopSwitchCount` 컬렉션을 추가하고, Matched 검증 또는 FR-019 자가 보정 결과일 때만 카운트를 증가 (FR-006 데이터 흐름)
- [X] T027 [P] [US4] `src/DeskRotate/FloatingWindowForm.cs`에 데스크톱별 전환 횟수 목록 표시 추가 (FR-006)

**Checkpoint**: 4개 유저 스토리 모두 독립적으로 동작한다.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: 여러 유저 스토리에 걸친 마무리 작업

- [ ] T028 [P] quickstart.md의 7개 검증 시나리오를 처음부터 끝까지 수동으로 실행하고 결과 기록
  - 2026-08-11 실제 Windows 11 머신(.NET 8 SDK 설치)에서 부분 실행함: 시나리오 1(시작 입력·초기 설정), 2(실제 자동 전환 — 가상 데스크톱이 실제로 바뀌는 것을 확인), 3(남은 시간·총 예상 실행 시간·데스크톱별 횟수 표시), 5(종료 확인 다이얼로그의 취소/확정 양쪽 경로, 다이얼로그가 떠 있는 동안 타이머 유지)는 확인 완료. 이 과정에서 실제 크래시 버그(SendInput 구조체 크기 오류)와 UI 버그(DPI 스케일링으로 시작 버튼이 안 보이던 문제)를 발견해 수정함 — 커밋 05b7d3c 참고.
  - 미확인 상태로 남은 것: 6(검증 실패→재시도 유발), 8(장시간 실행 안정성) — 추가 확인이 필요하면 이어서 진행 가능.
  - 2026-08-11 (Phase 9 구현 후 재검증): 범위 입력(1~2)·간격 5초·목표 3회로 재실행해 시나리오 2(범위 안에서 순환 전환, 데스크톱 1↔2)·4(목표 도달 시 "완료" 최소 보기 문구 및 "전환 완료 — 최종 통계" 상세 보기 문구, 정확한 카운트 데스크톱 1: 1회/데스크톱 2: 2회)·7(테두리 없는 최소 보기가 상단 중앙에 정확히 배치됨, 클릭으로 상세 보기 전환)까지 화면으로 직접 확인. 드래그 이동 자체(마우스 누른 채 이동)는 자동화 도구로는 검증하지 않음 — 클릭 판정(이동 없음)만 확인.
- [X] T029 [P] `src/DeskRotate/VirtualDesktopInterop.cs`와 `src/DeskRotate/KeyboardSimulator.cs`에 "공식 API만 사용" 제약을 명시하는 XML 문서 주석 추가 (향후 유지보수자를 위한 가드레일)
- [X] T030 spec.md Clarifications에서 확정한 제약(비공식 COM 인터페이스 금지, `SetForegroundWindow` 등 강제 포커스 트릭 금지)이 코드 어디에도 위반되지 않았는지 최종 점검

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: 의존 없음 — 바로 시작 가능
- **Foundational (Phase 2)**: Setup 완료 후 진행 — 모든 유저 스토리를 막는 차단 단계
- **User Story 1 (Phase 3)**: Foundational 완료 후 시작 가능, 다른 스토리에 의존하지 않음 (MVP)
- **User Story 2 (Phase 4)**: Foundational 완료 후 시작 가능. `RotationEngine`의 회전 루프(US1의 T013)가 존재해야 검증/재시도를 끼워 넣을 수 있으므로 US1 이후 진행을 권장하지만, 독립적으로 시연 가능한 단위(종료 확인 다이얼로그 T020)는 US1과 병행 가능
- **User Story 3 (Phase 5)**: Foundational 완료 후 시작 가능. `FloatingWindowForm`(US1의 T011)이 있어야 표시 필드를 추가할 수 있으므로 US1 이후 진행을 권장
- **User Story 4 (Phase 6)**: US1(회전 엔진)과 US2(검증 결과)가 만드는 데이터에 의존 — 두 스토리 이후 진행
- **Polish (Phase 7)**: 완료하고자 하는 모든 유저 스토리 이후 진행

### Within Each User Story

- 테스트(있는 경우) 먼저 작성
- 모델/값 객체 → 엔진 로직 → UI 표시 순서
- 스토리 하나가 완결되어야 다음 우선순위로 이동

### Parallel Opportunities

- Setup의 T002, T003은 병렬 가능
- Foundational의 T005, T006, T007은 서로 다른 파일이라 병렬 가능 (T004는 다른 파일이지만 데스크톱 관련 핵심 인터페이스라 먼저 완료해 두는 것을 권장)
- US1의 T010(테스트)은 T011~T015와 병렬 가능
- US2의 T016(테스트), T020(종료 확인 다이얼로그)은 서로 다른 파일 작업과 병렬 가능
- US3의 T023, T024는 서로 다른 파일이라 병렬 가능
- US4의 T027은 T026 완료 후 진행(같은 데이터에 의존하지만 다른 파일이므로 T026 완료 즉시 병렬 착수 가능)

---

## Parallel Example: User Story 1

```bash
# US1 테스트와 구현을 동시에 착수:
Task: "tests/DeskRotate.Tests/RotationSessionTests.cs에 회전 순서·목표 도달 판정 테스트 작성 (T010)"
Task: "src/DeskRotate/FloatingWindowForm.cs에 최소 창 셸 구현 (T011)"
```

## Parallel Example: User Story 2

```bash
Task: "tests/DeskRotate.Tests/VerificationOutcomeTests.cs에 판정 로직 테스트 작성 (T016)"
Task: "src/DeskRotate/FloatingWindowForm.cs에 종료 확인 다이얼로그 구현 (T020)"
```

---

## Implementation Strategy

### MVP First (User Story 1만)

1. Phase 1: Setup 완료
2. Phase 2: Foundational 완료 (필수 — 모든 스토리를 막음)
3. Phase 3: User Story 1 완료
4. **중단하고 검증**: quickstart.md 시나리오 1~2로 User Story 1 단독 검증
5. 필요 시 이 시점에서 데모 가능 (자동 전환은 되지만 아직 신뢰성 보강·시간 표시·카운트 표시는 없음)

### Incremental Delivery

1. Setup + Foundational 완료 → 기반 준비
2. User Story 1 추가 → 독립 검증(quickstart 시나리오 1~2, 4) → MVP 데모
3. User Story 2 추가 → 독립 검증(quickstart 시나리오 5~6) → 신뢰성 확보된 데모
4. User Story 3 추가 → 독립 검증(quickstart 시나리오 3, 7) → 가시성 확보된 데모
5. User Story 4 추가 → 독립 검증(quickstart 시나리오 3) → 전체 기능 완성
6. Polish(Phase 7) → quickstart.md 전체 재검증

---

## Notes

- [P] 태스크 = 서로 다른 파일, 선행 의존 없음
- [Story] 라벨은 각 태스크를 spec.md의 유저 스토리에 연결해 추적 가능하게 한다
- 각 유저 스토리는 독립적으로 완결·검증 가능해야 한다
- 논리적 단위(태스크 또는 스토리)마다 커밋
- 체크포인트마다 멈춰서 스토리를 독립적으로 검증할 것
- 이 프로젝트는 개인용 단일 사용자 데스크톱 앱이므로 팀 병렬 전략(Parallel Team Strategy)은 해당 없음 — 우선순위 순서(P1 → P1 → P1 → P2)대로 순차 진행을 권장

---

## Phase 8: Convergence

- [X] T031 `src/DeskRotate/DeskRotate.csproj`에 `<SupportedOSPlatformVersion>` 속성을 추가해 plan.md Technical Context가 명시한 최소 지원 버전(Windows 10 1903+)을 프로젝트 설정에 반영 per plan.md: Target Platform (partial)

---

## Phase 9: 데스크톱 범위 입력 및 최소/상세 보기 플로팅 창

**Purpose**: 사용자가 직접 요청한 3가지 요구사항(데스크톱 범위 입력, 시작 폼 기본값, 테두리 없는 최소/상세 보기 플로팅 창) 구현. spec.md FR-002·003·004·014·020~027, data-model.md, contracts/ 갱신분에 대응.

- [X] T032 [P] `src/DeskRotate/RotationSession.cs`를 `TotalDesktopCount` 대신 `RangeStart`/`RangeEnd`/파생 `DesktopCount`로 재작성 — `ComputeNextDesktopIndex()`가 절대 데스크톱 번호 기준으로 범위 끝→시작 순환하도록, `PerDesktopSwitchCounts`가 절대 번호로 키잉되도록, 생성자가 `RangeEnd >= RangeStart >= 1`을 검증하도록 변경 (FR-002, FR-003, FR-027, data-model.md)
- [X] T033 `src/DeskRotate/RotationEngine.cs`의 `PerformInitialSetup()`에 초기 탐색 단계 추가 — 실행 시점 현재 데스크톱에서 `RangeStart`까지 먼저 이동한 뒤 기존 범위 순회·창 생성 로직 수행, `AttemptSwitch()`의 wrap 판정을 `RangeEnd`→`RangeStart` 기준으로 변경 (FR-020, FR-022)
- [X] T034 [P] `src/DeskRotate/StartupInputForm.cs`를 데스크톱 개수 입력 대신 순회 시작/끝 번호 입력으로 변경 — 기본값 시작 1/끝 3/간격 300초로 설정, 끝 < 시작 시 시작 거부 (FR-003, FR-026, FR-027, contracts/startup-input-contract.md)
- [X] T035 `src/DeskRotate/FloatingWindowForm.cs`를 테두리 없는(`FormBorderStyle.None`) 창으로 재작성 — 기본 최소 보기(남은 시간 숫자만 큰 글씨로 표시)와 상세 보기(기존 라벨·목록) 두 상태를 두고, 창 본문에서 마우스 이동 거리로 클릭/드래그를 구분해 클릭 시 보기 전환·드래그 시 이동만 수행하도록 구현 (FR-004, FR-021, FR-023, FR-024, FR-025, contracts/floating-window-contract.md)
- [X] T036 [P] `tests/DeskRotate.Tests/RotationSessionTests.cs`에 범위 기반 로직 테스트 추가/수정 — 범위 끝→시작 순환(예: 3~7에서 7 다음은 3), 절대 번호로 키잉된 `PerDesktopSwitchCounts`, 잘못된 범위(끝 < 시작) 생성자 검증
- [X] T037 [P] 실제 Windows 환경에서 빌드·테스트 실행 후, 기본값(범위 1~3, 간격 5분)과 임의 범위(예: 3~5)로 앱을 실행해 초기 탐색·순회·최소/상세 보기 전환·테두리 없는 드래그가 의도대로 동작하는지 수동 확인 (quickstart.md 연계)

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

---

## Phase 10: 전환 검증 타이밍/화면 정지 결함 수정

**Purpose**: 실사용 중 발견된 결함(범위 끝→시작 전환 시 키 입력 1회 초과 발생, 특정 창의 타이머가 0에 멈춰 보임) 수정. spec.md FR-016(확장)·FR-028(신규) 대응.

- [X] T038 `src/DeskRotate/RotationEngine.cs`의 전환·검증·재시도 시퀀스를 `Thread.Sleep` 대신 `async`/`await Task.Delay` 기반으로 재작성 — 마지막 키 입력 이후 검증 전에도 지연을 두어 애니메이션 미완료로 인한 오판을 방지하고, 시퀀스 진행 중에도 매초 모든 창의 화면 갱신이 계속되도록 재진입 방지 플래그(`_switchInProgress`)를 추가 (FR-016, FR-017, FR-028)
  - 2026-08-11 실제 Windows 11 머신에서 재검증: 범위 1~3, 간격 4초, 목표 6회(2바퀴)로 실행 — 6회 전환이 모두 끝난 뒤 상세 보기에서 데스크톱 1·2·3이 각각 정확히 2회씩으로 균등하게 표시됨(키 입력 과다/부족이 있었다면 이렇게 고르게 나오지 않았을 것). 크래시 없음, 프로세스 계속 응답. 총 예상 실행 시간(24초=4초×6)도 정확히 일치.

---

## Phase 11: 사이클 입력·닫기 버튼·표시 옵션·자석 스냅·존재하지 않는 데스크톱 자동 생성

**Purpose**: 사용자가 직접 요청한 4가지(목표 사이클 수 입력, 상세 보기 커스텀 닫기 버튼, 초/사이클 표시 온오프 옵션, 테두리 자석 스냅)와, 이어서 사용자가 지적한 아키텍처 결함(입력한 범위의 데스크톱이 실제로 존재하지 않을 수 있다는 점) 수정을 구현. spec.md FR-013(개정), FR-029~033(신설) 대응.

- [X] T039 [P] `src/DeskRotate/KeyboardSimulator.cs`에 Windows 표준 "새 데스크톱 추가" 단축키(Win+Ctrl+D) 입력을 보내는 `SendCreateDesktopKeystroke()`를 추가 (FR-033)
- [X] T040 `src/DeskRotate/RotationSession.cs`의 생성자 파라미터를 `targetSwitchCount` 대신 `targetCycleCount`로 바꾸고, `TargetSwitchCount = TargetCycleCount * DesktopCount`로 환산해 기존 회전·통계 로직은 그대로 두며, `CurrentCycleNumber`(파생, `min(CompletedSwitchCount / DesktopCount + 1, TargetCycleCount)`)와 표시 옵션 `ShowSecondsUnit`/`ShowCycleNumber`(생성자로 입력받아 세션 동안 유지)를 추가 (FR-013, FR-030, FR-031, data-model.md)
- [X] T041 `src/DeskRotate/RotationEngine.cs`의 `PerformInitialSetup()`(초기 탐색·초기 설정 단계)에서, 데스크톱 전환 키 입력을 보낸 직후마다 직전 위치를 나타내는 창(탐색 단계는 임시 probe 창, 설정 단계는 방금 만든 `FloatingWindowForm`)에 대해 `IsWindowOnCurrentVirtualDesktop`으로 실제 전환 여부를 확인하고, 전환되지 않았으면(그 데스크톱이 없으면) T039의 새 데스크톱 생성 키 입력을 보내 그 자리를 채우도록 구현 — 서로 다른 범위 번호의 창이 같은 실제 데스크톱 위에 겹쳐 생성되는 결함을 방지 (FR-033, Edge Cases)
- [X] T042 [P] `src/DeskRotate/StartupInputForm.cs`의 "목표 총 전환 횟수" 입력을 "목표 사이클 수"(기본값 3)로 바꾸고, "초 단위 표시"(기본 켜짐)·"사이클 번호 표시"(기본 꺼짐) 체크박스 두 개를 추가하며, 총 예상 실행 시간 미리보기 계산을 사이클 수 × 데스크톱 개수 기준으로 갱신 (FR-013, FR-014, FR-026, FR-031, contracts/startup-input-contract.md)
- [X] T043 [P] `src/DeskRotate/FloatingWindowForm.cs`의 상세 보기에 작은 커스텀 닫기(×) 버튼을 추가해 클릭 시 기존 `FormClosing`(FR-008) 확인 절차로 이어지도록 하고, 현재 사이클/목표 사이클 번호 표시를 추가하며, 최소 보기의 남은 시간 표시를 `ShowSecondsUnit`/`ShowCycleNumber`에 따라 조합하는 포맷 함수로 구현 (FR-029, FR-030, FR-031, contracts/floating-window-contract.md)
- [X] T044 [P] `src/DeskRotate/FloatingWindowForm.cs`의 드래그 처리(`OnMouseMove`)에 화면 작업 영역 테두리 근접 시 자석처럼 달라붙는 스냅 로직을 추가 — 임계 거리를 작게 잡아 과도한 스냅을 피함 (FR-032)
- [X] T045 [P] `tests/DeskRotate.Tests/RotationSessionTests.cs`에 목표 사이클 수 → 목표 총 전환 횟수 환산, `CurrentCycleNumber` 계산(사이클 경계·목표 도달 후 캡) 케이스를 추가 — 35개 전체 테스트 통과
- [X] T046 실제 Windows 11 머신에서 빌드·테스트 실행 후 라이브 검증:
  - 시작 폼: 목표 사이클 수 입력과 "초 단위 표시"/"사이클 번호 표시" 체크박스, 총 예상 실행 시간(간격×사이클×데스크톱수) 미리보기가 모두 정상 렌더링됨을 스크린샷으로 확인. 이 과정에서 실제로 발견·수정한 결함 2건: (1) 사이클 설명 라벨이 `AutoSize` 기본값(true) 때문에 폼 밖으로 잘려 보이던 문제 → 짧은 한 줄 문구로 교체, (2) 체크박스 예시 문구가 폭을 넘어 잘리던 문제 → 문구 단축.
  - **범위 데스크톱이 실제로 존재하지 않는 경우(FR-033 핵심 시나리오)**: 범위 6~7·간격 5초·목표 1사이클로 제출 — 초기 탐색·설정 중 여러 새 가상 데스크톱이 자동 생성되며 화면이 전환되는 것을 확인, 크래시 없이 완료되고 플로팅 창이 정상적으로 나타나 카운트다운이 갱신됨. (참고: 이 테스트로 사용자 환경에 새 가상 데스크톱 몇 개가 남았을 수 있음 — 불필요하면 Task View에서 수동 삭제 가능)
  - **최소 보기 표시 형식(FR-031)**: 범위 1~3·간격 4초·목표 2사이클, 두 옵션 모두 켠 상태로 "[1번째] 3초"가 잘리지 않고 완전히 표시됨을 확인. 이 과정에서 실제 발견한 결함: 사이클 번호 접두어로 텍스트가 길어지면 고정 크기(100×70) 최소 보기 창에서 글자가 잘리던 문제 → `RefreshDisplay`에서 `TextRenderer.MeasureText`로 실제 폭을 재서 창을 동적으로 넓히고 가로 중심을 유지하도록 수정.
  - **상세 보기 커스텀 닫기 버튼(FR-029)**: 자식 컨트롤 enumeration으로 실제 닫기(×) 버튼(Win32 Button)을 찾아 `BM_CLICK`으로 클릭 → "정말 종료할까요?" 확인 다이얼로그가 뜨는 것을 확인(다이얼로그 hwnd 및 "예"/"아니요" 버튼 존재로 검증). "아니요" 클릭 시 다이얼로그만 닫히고 3개 창 모두 정상 유지됨을 재확인, 이후 다시 닫기 버튼 → "예" 클릭으로 프로세스가 깨끗하게 종료됨(tasklist로 확인)까지 end-to-end 검증 완료.
  - **사이클 진행 표시(FR-030)**: 상세 보기에서 "사이클: 2 / 2"(목표 도달 후 캡핑됨)가 정확히 표시됨을 자식 컨트롤 텍스트로 확인.
  - **미검증으로 남은 항목**: 테두리 자석 스냅(FR-032)은 코드 리뷰로만 확인했고 실제 드래그 동작으로는 검증하지 못함 — 외부 프로세스에서 좌표 기반 마우스 클릭/드래그 시뮬레이션이 이 머신의 DPI 가상화로 신뢰할 수 없어(스크린샷은 실물 픽셀, `GetWindowRect`/`SetCursorPos`는 호출자 DPI 인식 여부에 따라 다른 좌표계를 반환함을 실측으로 확인) 좌표 기반 드래그 테스트를 보류함. 필요하면 실제 마우스로 직접 확인 권장.

---

## Phase 12: Convergence

- [X] T047 `specs/001-desk-rotate/quickstart.md`를 현재 spec.md와 일치하도록 갱신 per plan.md (partial): 시나리오 1의 "데스크톱 개수 3"·"목표 전환 횟수 6" 표현을 범위(예: 시작 1~끝 3)·목표 사이클 수 기준으로 고치고, 전제 조건에 "미리 데스크톱을 만들어 둘 필요가 이제는 없다(FR-033, 부족하면 자동 생성됨)"는 점을 반영하며, FR-029(상세 보기 커스텀 닫기 버튼)·FR-030(사이클 진행 표시)·FR-031(초 단위/사이클 번호 표시 옵션)·FR-032(테두리 자석 스냅)·FR-033(존재하지 않는 데스크톱 자동 생성)에 대한 수동 검증 시나리오를 추가

---

## Phase 13: 사이클 번호 표시 기본값 변경 및 절대 위치 판별 결함 수정

**Purpose**: 사용자가 직접 요청한 "사이클 번호 표시" 기본값 변경과, 이미 여러 데스크톱이 떠 있는 상태에서 일부만 겹치는 범위를 입력하면 불필요하게 많은 데스크톱을 새로 생성하던 심각한 실사용 버그 수정. spec.md FR-031(개정)·FR-034(신설) 대응.

- [X] T048 `src/DeskRotate/RotationEngine.cs`에 `SeekToActualFirstDesktop()`을 추가하고 `PerformInitialSetup()` 맨 앞에서 호출 — 뒤로(Previous) 계속 이동하며 매번 공식 API로 실제 이동 여부를 확인해, 더 이상 이동하지 않는 지점(=실제 데스크톱 1번)을 스스로 찾아낸 뒤에야 범위 시작까지의 이동 칸 수(FR-022)를 계산하도록 수정 — 실행 시점을 무조건 절대 1번으로 가정하던 기존 로직이 이미 다른 데스크톱들 사이에서 실행했을 때 불필요한 데스크톱을 대량 생성하던 근본 원인 (FR-034, contradicts)
- [X] T049 `src/DeskRotate/StartupInputForm.cs`의 "사이클 번호 표시" 체크박스 기본값과 `src/DeskRotate/RotationSession.cs` 생성자의 `showCycleNumber` 기본 매개변수를 `false`에서 `true`로 변경 (FR-031, partial)
- [X] T050 `tests/DeskRotate.Tests/RotationSessionTests.cs`의 `ShowSecondsUnitAndShowCycleNumber_DefaultToOnAndOff`를 새 기본값(둘 다 켜짐)에 맞게 갱신 — 35개 전체 테스트 통과
- [X] T051 실제 Windows 11 머신에서 사용자가 보고한 정확한 재현 시나리오로 라이브 검증: (1단계) 범위 1~4로 실행해 실제 데스크톱 1~4를 확보한 뒤 목표 도달로 데스크톱 4번에 정지, (2단계) 그 상태에서 새 프로세스를 다시 실행해 범위 2~6 입력 — 결과: 데스크톱 2·3·4·5·6에 대해 정확히 5개의 창만 생성됨(기존 2·3·4번 재사용, 5·6번만 신규 생성)을 Win32 창 목록 조회로 확인. 수정 전 버그였다면 6·7·8·9번처럼 불필요하게 많이 생성됐을 것.

---

## Phase 14: FR-033 이동 판정 오탐 수정 (데스크톱 1→14 점프 재현 결함)

**Purpose**: Phase 13의 FR-034 수정 이후에도 재발한 실사용 버그(데스크톱 1번에서 범위 2~3 입력 시 1→2가 아니라 1→14처럼 엉뚱하게 점프) 수정. spec.md FR-033 개정.

- [X] T052 `src/DeskRotate/RotationEngine.cs`에 `HasSettledOnReference()`를 추가해 전환 키 입력 직후의 이동 여부 판정을 단일 조회에서 이중 확인(시간을 두고 두 번 다 "이동 안 함"이어야 확정)으로 변경하고, `EnsureAdvancedToNextDesktop()`·`SeekToActualFirstDesktop()` 양쪽에 적용 — 단일 조회는 전환 애니메이션·조회 지연을 "그 데스크톱이 없음"으로 오판해 불필요한 데스크톱을 대량 생성하는 원인이었다 (FR-033, contradicts)
- [X] T053 `src/DeskRotate/RotationEngine.cs`의 `CreateDesktopProbe()`가 만드는 참조 창을 1×1·완전 투명(Opacity 0)에서 화면 밖(50×50, 불투명)으로 변경 — DWM이 극단적으로 작은 창의 가상 데스크톱 소속 조회를 건너뛸 가능성을 배제하기 위한 방어적 수정 (FR-033, partial)
- [X] T054 실제 Windows 11 머신에서 사용자가 보고한 정확한 재현 시나리오(데스크톱 1번에서 범위 2~3 입력)로 재검증 — 정확히 데스크톱 2·3번 창 2개만 생성되고 엉뚱한 번호로 점프하지 않음을 확인. 다만 문제를 보고받은 정확한 조건(사용자 환경의 기존 데스크톱 개수·상태)을 동일하게 재현하지는 못했으므로, 이번 수정으로 재발하지 않는지는 사용자의 재확인이 필요함(이중 확인·더 신뢰할 수 있는 참조 창 크기라는 두 가지 방어적 개선이 근본 원인일 가능성이 높다고 판단해 적용).

---

## Phase 15: FR-033 이동 판정 근본 재설계 (Win+Ctrl+D는 항상 목록 끝에 추가되는 특성 반영)

**Purpose**: Phase 14의 이중 확인 수정 이후에도 재발한 실사용 버그(데스크톱 1번에서 범위 2~3 입력 시 1→2→19→20처럼 점프) 근본 수정. spec.md FR-033 재개정.

- [X] T055 `src/DeskRotate/RotationEngine.cs`의 `HasSettledOnReference()`(고정된 두 번 확인)를 `WaitForMovementAway()`로 교체 — 전환 키 입력 후 이동이 감지될 때까지(또는 2초 제한 시간이 다 될 때까지) 100ms 간격으로 반복 조회하고, 제한 시간 내내 단 한 번도 이동이 감지되지 않았을 때에만 "그 데스크톱이 없다"고 판단하도록 변경. Win+Ctrl+D는 현재 위치와 무관하게 항상 전체 데스크톱 목록의 맨 끝에 추가되므로, 고정된 짧은 확인 한두 번으로는 여전히 오판(→ 대량 점프)이 발생할 수 있었던 근본 원인을 해결 (FR-033, contradicts)
- [X] T056 실제 Windows 11 머신에서, 먼저 범위 1~18로 실행해 실제 데스크톱 18개를 정확히(과부족 없이) 확보한 뒤, 그 상태(데스크톱 18번에 정지)에서 범위 1~1로 재실행해 데스크톱 1번으로 위치를 되돌리고, 다시 그 상태에서 범위 2~3으로 재실행 — 사용자가 보고한 정확한 조건(여러 데스크톱이 이미 존재하는 상태에서 데스크톱 1번부터 시작)을 근접 재현해, 정확히 데스크톱 2·3번 창 2개만 생성되고 19·20번으로 점프하지 않음을 확인.

---

## Phase 16: 이동 판정 방식 근본 재설계 (1회용 참조 창 폐기, 플로팅 창 기준으로 통일)

**Purpose**: Phase 15의 폴링 방식 수정 이후에도 재발한 실사용 버그 근본 수정. 진단 로그로 정밀 조사한 결과, FR-033/FR-034가 이동 판정에 써 온 1회용 참조 창(만들고 곧바로 조회한 뒤 버리는 방식) 자체가 이 환경에서 근본적으로 신뢰할 수 없다는 것을 확인 — 창 크기·화면 안/밖 위치·작업표시줄 노출 여부·대기 시간을 모두 바꿔봐도 마찬가지였고, 심지어 매번 새로 만들지 않고 살려 둬도(HWND 재사용 의심 배제) 완전히 해결되지 않았다. 반면 세션 내내 살아있는 플로팅 창(FloatingWindowForm) 기준 조회는 모든 테스트에서 한 번도 어긋난 적이 없었다. spec.md FR-033·FR-034 재개정.

- [X] T057 `src/DeskRotate/RotationEngine.cs`를 재설계: `SeekToActualFirstDesktop()`(판정 기반)을 `SeekToActualFirstDesktopBlindly()`로 교체 — `Ctrl+Win+Left`가 이미 첫 번째 데스크톱에서는 항상 안전한 no-op이라는 Windows 표준 동작을 근거로, 판정 없이 충분히 넉넉한 횟수(40회)만큼 무조건 반복 시도한다 (FR-034, contradicts)
- [X] T058 `src/DeskRotate/RotationEngine.cs`의 `PerformInitialSetup()`을 재설계 — 초기 탐색(FR-022)과 초기 설정(FR-020)을 하나의 순회로 통합해 실제 데스크톱 1번부터 `RangeEnd`까지 순서대로 방문하며, 이동 여부 판정은 1회용 참조 창 대신 직전에 방문해 이미 만들어 둔 `FloatingWindowForm`을 기준으로 한다. `RangeStart` 이전(순회 대상 아님)에 임시로 만든 창은 `RangeStart` 도달 시 정리한다. `CreateDesktopProbe()`와 관련 1회용 참조 창 인프라를 모두 제거해 코드를 단순화 (FR-033, contradicts)
- [X] T059 실제 Windows 11 머신에서 진단용 파일 로그(임시)를 붙여 이동 판정 결과를 직접 관찰 — 1회용 참조 창이 실제 이동에도 불구하고 계속 "이동 안 함"으로, 또는 실제로는 멈춰 있는데 계속 "이동함"으로 나오는 등 일관성 없이 신뢰할 수 없음을 확인. 재설계 이후에는 데스크톱 18개를 실제로 만들어 둔 뒤 데스크톱 4번에서 범위 2~3(뒤로 여러 번 이동 필요)으로, 그리고 데스크톱 1번에서 범위 2~3(단순 경우)으로 각각 재현해 두 경우 모두 정확히 2·3번 창 2개만 생성되고 더 이상 점프하지 않음을 확인. 진단용 로그는 확인 후 제거함.

# Phase 1 Data Model: 데스크톱 자동 로테이터 + 플로팅 상태창

**Feature**: `001-desk-rotate` | **Date**: 2026-08-11

spec.md의 Key Entities를 구현 가능한 형태로 구체화한다. 모든 상태는 세션(앱 실행) 범위이며 영속화되지 않는다(FR-007).

## RotationSession

앱이 실행되어 있는 동안 유지되는 단일 상태 객체. 앱마다 인스턴스는 하나뿐이다.

| 필드 | 타입 | 설명 | 검증/제약 |
|---|---|---|---|
| `RangeStart` | int | 사용자가 시작 시 입력한 순회 범위의 시작 데스크톱 번호(1-based) | 1 이상의 정수 (FR-003) |
| `RangeEnd` | int | 순회 범위의 끝 데스크톱 번호(1-based, 포함) | `RangeEnd >= RangeStart` (FR-003, FR-027) |
| `DesktopCount` | int | `RangeEnd - RangeStart + 1` (계산값) | 파생 필드, 순환 계산에만 내부적으로 사용 |
| `IntervalSeconds` | int | 전환 간격(초 단위로 내부 저장, 입력은 UI 재량) | 1 이상의 정수 (FR-011), 기본값 300(5분) (FR-026) |
| `TargetCycleCount` | int | 사용자가 입력한 목표 사이클 수 | 1 이상의 정수 (FR-013), 기본값 3 |
| `TargetSwitchCount` | int | 목표 총 전환 횟수 — `TargetCycleCount * DesktopCount` (계산값) | 파생 필드, 기존 회전·통계 로직은 그대로 이 값을 사용 (FR-013) |
| `TotalPlannedRuntimeSeconds` | int | `IntervalSeconds * TargetSwitchCount` (계산값) | 파생 필드, 저장 시 재계산 (FR-014) |
| `CurrentDesktopIndex` | int | 검증으로 확인된 현재 데스크톱의 절대 번호 (`RangeStart`..`RangeEnd`) | 초기 설정(범위 시작까지 탐색, FR-022) 완료 시 `RangeStart`로 설정 |
| `CompletedSwitchCount` | int | 검증까지 완료된 누적 전환 횟수 | 0에서 시작, `TargetSwitchCount` 도달 시 더 이상 증가하지 않음 |
| `CurrentCycleNumber` | int | 현재 진행 중인 사이클 번호(1-based) — `min(CompletedSwitchCount / DesktopCount + 1, TargetCycleCount)` | 파생 필드 (FR-030) |
| `TargetReached` | bool | `CompletedSwitchCount >= TargetSwitchCount` | 파생 필드 |
| `RemainingSecondsToNextSwitch` | int | 다음 전환까지 남은 시간 | `TargetReached`면 의미 없음(표시 안 함) |
| `RemainingSecondsToFinish` | int | 프로그램 종료까지 남은 전체 시간 | `TotalPlannedRuntimeSeconds`에서 경과 시간을 뺀 값(파생) |
| `LastVerification` | `VerificationOutcome` | 가장 최근 전환 시도의 검증 결과 | 아래 값 객체 참고 |
| `RetryCount` | int | 진행 중인 전환 시도의 재시도 횟수 | 0..RetryLimit(3), 성공하거나 한도 도달 시 0으로 리셋 |
| `ShowSecondsUnit` | bool | 표시 옵션 — 최소 보기 숫자 뒤에 "초"를 붙일지 | 시작 입력 폼에서 설정, 기본값 켜짐 (FR-031) |
| `ShowCycleNumber` | bool | 표시 옵션 — 최소 보기 앞에 "[N번째] "를 붙일지 | 시작 입력 폼에서 설정, 기본값 켜짐 (FR-031) |
| `IsPaused` | bool | 사용자가 일시정지를 요청했는지 여부 (FR-035) | 기본값 꺼짐. 켜져 있으면 남은 시간 카운트다운과 새 전환 시도가 모두 멈춘다(진행 중이던 검증·재시도 시퀀스는 예외) |

`CurrentDesktopIndex`와 `PerDesktopSwitchCounts`의 키는 모두 **절대 데스크톱 번호**(`RangeStart`..`RangeEnd`)를 사용한다 — 범위가 3~7이면 "데스크톱 3"처럼 사용자가 입력한 번호 그대로 표시되어야 하므로, 내부적으로 1부터 다시 세는 상대 인덱스를 쓰지 않는다.

**상태 전이**: `Idle(대기)` → `SwitchAttempted(전환 시도)` → `Verifying(검증 중)` → (`Verified/일치` → `Idle`, `Mismatched/불일치` → `Retrying(재시도)` → `Verifying` 반복, 한도 도달 시 `SelfCorrected(자가 보정)` → `Idle`) → (`TargetSwitchCount` 도달 시 `Completed(완료)`로 전이, 이후 전환 중단).

## VerificationOutcome (값 객체)

| 필드 | 타입 | 설명 |
|---|---|---|
| `IntendedDesktopIndex` | int | 전환하려고 시도했던 목표 데스크톱 번호 |
| `ActualDesktopIndex` | int | `IsWindowOnCurrentVirtualDesktop` 조회로 확인된 실제 데스크톱 번호 |
| `Matched` | bool | `IntendedDesktopIndex == ActualDesktopIndex` |

FR-017(검증), FR-018(재시도), FR-019(자가 보정)의 판단 로직은 이 값 객체를 입력으로 받는 순수 함수로 구현해 단위 테스트 대상으로 삼는다(research.md §4의 테스트 전략과 연결).

## PerDesktopSwitchCount

| 필드 | 타입 | 설명 | 검증/제약 |
|---|---|---|---|
| `DesktopIndex` | int | 절대 데스크톱 번호 (`RangeStart`..`RangeEnd`) | RotationSession의 범위 내 |
| `Count` | int | 이번 세션 동안 이 데스크톱으로 검증된 전환 횟수 | 0에서 시작, 음수 불가 |

`RotationSession`에 종속된 컬렉션(`DesktopCount`개의 항목, 키는 `RangeStart`..`RangeEnd`). 카운트 증가는 오직 `VerificationOutcome.Matched == true`(또는 FR-019의 자가 보정 결과)일 때만 일어난다 — 검증되지 않은 전환은 세지 않는다.

## PerDesktopFloatingWindow

| 필드 | 타입 | 설명 | 검증/제약 |
|---|---|---|---|
| `DesktopIndex` | int | 이 창이 속한 절대 데스크톱 번호 | `RangeStart`..`RangeEnd`, 데스크톱당 정확히 1개 |
| `WindowHandle` | HWND(불투명 핸들) | `IsWindowOnCurrentVirtualDesktop` 조회 대상 | 앱 시작 시 초기 설정 과정(FR-020, FR-022)에서 생성 |
| `Position` | (X, Y) | 현재 화면 좌표 | 초기값은 화면 상단 중앙(FR-021), 이후 사용자가 창 본문을 드래그해 변경 가능하며 세션 내에서만 유지 |
| `ViewMode` | `Minimal` \| `Detailed` | 현재 표시 모드 (FR-023, FR-024) | 항상 `Minimal`로 시작, 클릭 시 토글, 세션 내에서만 유지 |
| `IsClosing` | bool | 종료 확인 다이얼로그가 떠 있는 상태인지 | 확인 창이 떠 있는 동안에도 `RotationSession`은 계속 갱신됨(FR-010, Assumptions) |

**클릭 vs 드래그 구분**(FR-025): 마우스 버튼을 누른 시점의 좌표를 기억해 두고, 뗄 때까지의 최대 이동 거리가 임계값(구현 재량, 일반적인 OS 드래그 임계값 수준) 이하이면 클릭(→ `ViewMode` 토글)으로, 초과하면 드래그(→ `Position` 갱신, `ViewMode`는 그대로)로 처리한다.

**테두리 자석 스냅**(FR-032): 드래그로 `Position`을 갱신할 때마다, 창의 현재 화면 대비 위치가 소속 화면(`Screen`)의 작업 영역 테두리(상/하/좌/우)에서 임계 거리(구현 재량, 작게 — "거의 닿을 수준") 이내이면 해당 축의 좌표를 테두리 값으로 고정(snap)한다. 임계 거리를 벗어나면 스냅 없이 커서를 그대로 따라간다.

**상세 보기 닫기 버튼**(FR-029): `ViewMode = Detailed`일 때만 작은 커스텀 닫기(×) 컨트롤을 표시하며, 클릭 시 일반 닫기 시도와 동일한 `IsClosing` → 확인 다이얼로그 경로로 이어진다. 최소 보기에는 표시하지 않는다.

**상세 보기 일시정지 버튼**(FR-035): `ViewMode = Detailed`일 때만 표시되며, 클릭하면 `RotationSession.IsPaused`를 토글한다. 세션은 앱 전체에 하나뿐이므로, 어느 데스크톱의 창에서 토글하든 다음 갱신 때 모든 창에 동일하게 반영된다. `TargetReached`이면 비활성화된다.

**관계**: `RotationSession` 1 — N `PerDesktopFloatingWindow` (N = `DesktopCount`), 1 — N `PerDesktopSwitchCount` (N = `DesktopCount`). 각 `PerDesktopFloatingWindow`는 정확히 하나의 절대 데스크톱 번호에 대응하며, `RotationSession`의 파생 필드(남은 시간, 총 전환 횟수, 데스크톱별 카운트)를 동일하게 표시한다(창마다 내용은 같음, 위치와 `ViewMode`만 창별로 독립적).

## 생명주기 요약

1. 앱 시작 → 사용자가 `RangeStart`, `RangeEnd`, `IntervalSeconds`, `TargetCycleCount`, `ShowSecondsUnit`, `ShowCycleNumber` 입력 (spec.md FR-003, FR-011, FR-013, FR-031; 기본값은 FR-026). `TargetSwitchCount`는 `TargetCycleCount * DesktopCount`로 즉시 환산된다.
2. 절대 위치 판별(FR-034): 실행 시점의 데스크톱이 전체 중 몇 번째인지 공식 API로 알 수 없고, 1회용 참조 창으로 이동 여부를 판정하는 방식은 실사용 환경에서 신뢰할 수 없는 것으로 확인됐다 — 그래서 판정에 의존하지 않고, `Ctrl+Win+Left`가 이미 첫 번째 데스크톱에서는 항상 안전한 no-op이라는 Windows 표준 동작을 이용해 충분히 넉넉한 횟수만큼 무조건 뒤로 이동시켜 실제 데스크톱 1번에 확실히 도달한다.
3. 초기 탐색(FR-022)과 초기 설정(FR-020)을 하나의 순회로 통합 수행: 실제 데스크톱 1번부터 `RangeEnd`까지 순서대로 한 번씩 방문하며 매번 `PerDesktopFloatingWindow`를 생성한다. 이동 여부 확인은 1회용 참조 창이 아니라 직전에 방문해 이미 만들어 둔(신뢰성이 검증된) 플로팅 창을 기준으로 한다 — 전환되지 않았으면(그 데스크톱이 없으면) 새 데스크톱을 생성해 채운다(FR-033). `RangeStart` 이전(순회 대상 아님)에 임시로 만든 창은 `RangeStart` 도달 시 정리하고, `RangeStart`부터 `RangeEnd`까지 만든 창만 유지한다. 완료 후 `RangeStart`로 복귀.
4. 반복: 간격 경과 → 전환 시도(범위 끝에서 시작으로 순환) → 검증 → (일치: 카운트 증가·`Idle` 복귀 / 불일치: 재시도 최대 3회 → 그래도 불일치면 자가 보정) → `TargetSwitchCount` 도달 시 `Completed`.
5. 사용자가 아무 플로팅 창이나 클릭하면 그 창만 `ViewMode`가 토글된다(다른 창에는 영향 없음).
6. 어느 창이든 닫기 시도 시 확인 다이얼로그 → 확정 시 전체 종료, 취소 시 `RotationSession`은 계속.

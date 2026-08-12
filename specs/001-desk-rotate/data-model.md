# Phase 1 Data Model: 데스크톱 자동 로테이터 + 플로팅 상태창

**Feature**: `001-desk-rotate` | **Date**: 2026-08-11

spec.md의 Key Entities를 구현 가능한 형태로 구체화한다. 모든 상태는 세션(앱 실행) 범위이며 영속화되지 않는다(FR-007).

## RotationSession

앱이 실행되어 있는 동안 유지되는 단일 상태 객체. 앱마다 인스턴스는 하나뿐이다.

| 필드 | 타입 | 설명 | 검증/제약 |
|---|---|---|---|
| `TotalDesktopCount` | int | 사용자가 시작 시 입력한 가상 데스크톱 총 개수 | 1 이상의 정수 (FR-003) |
| `IntervalSeconds` | int | 전환 간격(초 단위로 내부 저장, 입력은 UI 재량) | 1 이상의 정수 (FR-011) |
| `TargetSwitchCount` | int | 목표 총 전환 횟수 | 1 이상의 정수 (FR-013) |
| `TotalPlannedRuntimeSeconds` | int | `IntervalSeconds * TargetSwitchCount` (계산값) | 파생 필드, 저장 시 재계산 (FR-014) |
| `CurrentDesktopIndex` | int | 검증으로 확인된 현재 데스크톱 번호 (1..TotalDesktopCount) | 초기값은 앱 시작 시 최초 검증 결과 |
| `CompletedSwitchCount` | int | 검증까지 완료된 누적 전환 횟수 | 0에서 시작, `TargetSwitchCount` 도달 시 더 이상 증가하지 않음 |
| `TargetReached` | bool | `CompletedSwitchCount >= TargetSwitchCount` | 파생 필드 |
| `RemainingSecondsToNextSwitch` | int | 다음 전환까지 남은 시간 | `TargetReached`면 의미 없음(표시 안 함) |
| `RemainingSecondsToFinish` | int | 프로그램 종료까지 남은 전체 시간 | `TotalPlannedRuntimeSeconds`에서 경과 시간을 뺀 값(파생) |
| `LastVerification` | `VerificationOutcome` | 가장 최근 전환 시도의 검증 결과 | 아래 값 객체 참고 |
| `RetryCount` | int | 진행 중인 전환 시도의 재시도 횟수 | 0..RetryLimit(3), 성공하거나 한도 도달 시 0으로 리셋 |

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
| `DesktopIndex` | int | 데스크톱 번호 (1..TotalDesktopCount) | RotationSession.TotalDesktopCount 범위 내 |
| `Count` | int | 이번 세션 동안 이 데스크톱으로 검증된 전환 횟수 | 0에서 시작, 음수 불가 |

`RotationSession`에 종속된 컬렉션(`TotalDesktopCount`개의 항목, 인덱스 1..N). 카운트 증가는 오직 `VerificationOutcome.Matched == true`(또는 FR-019의 자가 보정 결과)일 때만 일어난다 — 검증되지 않은 전환은 세지 않는다.

## PerDesktopFloatingWindow

| 필드 | 타입 | 설명 | 검증/제약 |
|---|---|---|---|
| `DesktopIndex` | int | 이 창이 속한 데스크톱 번호 | 1..TotalDesktopCount, 데스크톱당 정확히 1개 |
| `WindowHandle` | HWND(불투명 핸들) | `IsWindowOnCurrentVirtualDesktop` 조회 대상 | 앱 시작 시 초기 설정 과정(FR-020)에서 생성 |
| `Position` | (X, Y) | 현재 화면 좌표 | 초기값은 화면 상단 중앙(FR-021), 이후 사용자 드래그로 변경 가능하며 세션 내에서만 유지 |
| `IsClosing` | bool | 종료 확인 다이얼로그가 떠 있는 상태인지 | 확인 창이 떠 있는 동안에도 `RotationSession`은 계속 갱신됨(FR-010, Assumptions) |

**관계**: `RotationSession` 1 — N `PerDesktopFloatingWindow` (N = `TotalDesktopCount`), 1 — N `PerDesktopSwitchCount` (N = `TotalDesktopCount`). 각 `PerDesktopFloatingWindow`는 정확히 하나의 데스크톱 번호에 대응하며, `RotationSession`의 파생 필드(남은 시간, 총 전환 횟수, 데스크톱별 카운트)를 동일하게 표시한다(창마다 내용은 같음, 위치만 다름).

## 생명주기 요약

1. 앱 시작 → 사용자가 `TotalDesktopCount`, `IntervalSeconds`, `TargetSwitchCount` 입력 (spec.md FR-003, FR-011, FR-013).
2. 초기 설정 과정(FR-020): 각 데스크톱을 자동 순회하며 `PerDesktopFloatingWindow` N개 생성, 원래 데스크톱으로 복귀.
3. 반복: 간격 경과 → 전환 시도 → 검증 → (일치: 카운트 증가·`Idle` 복귀 / 불일치: 재시도 최대 3회 → 그래도 불일치면 자가 보정) → `TargetSwitchCount` 도달 시 `Completed`.
4. 어느 창이든 닫기 시도 시 확인 다이얼로그 → 확정 시 전체 종료, 취소 시 `RotationSession`은 계속.

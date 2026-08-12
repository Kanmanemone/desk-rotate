# Specification Quality Checklist: 데스크톱 자동 로테이터 + 플로팅 상태창

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-11
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- Items marked incomplete require spec updates before `/speckit-clarify` or `/speckit-plan`
- 모든 항목 통과. FR-009(전환 간격 설정 방식)는 사용자 확인을 거쳐 "실행 시 직접 입력"으로 확정함.
- 2026-08-11 clarify 세션: 목표 전환 횟수 도달 시 동작(FR-013)을 사용자 확인을 거쳐 "전환만 멈추고 창은 열어둔 채 최종 통계 표시"로 확정하고, 총 전환 횟수 입력(FR-011)·총 예상 실행 시간 계산/표시(FR-012)를 새로 추가함. 재검증 결과 전 항목 통과 유지.
- 2026-08-11 clarify 세션 (2차): 전환 결과 검증(공식 API) 도입 여부는 "도입하지 않음"으로 확정(현행 유지). 마지막→처음 순환 시 필요한 연속 키 입력의 씹힘 위험을 사용자가 지적해, 핑퐁 방식 대신 균일 순회(순환) 방식을 유지하되 입력 사이 지연(FR-014)으로 완화하는 것으로 확정. 정확한 지연 시간(밀리초 등)은 이 스펙에서 수치화하지 않고 구현/계획 단계 재량으로 남김 — Requirement Completeness 체크는 "지연을 둬야 한다"는 요구 자체는 테스트 가능하므로 통과로 판단.
- 2026-08-11 clarify 세션 (3차, 대규모 재설계): 사용자가 "제대로 작동하지 않으면 쓸모없는 프로그램"이라는 우려를 제기해 재조사 후 아키텍처를 크게 바꿈 — (1) 2차 세션에서 "검증 도입 안 함"으로 정했던 결정을 뒤집고, 데스크톱마다 숨겨진 마커 창 + 공식 IsWindowOnCurrentVirtualDesktop 조회로 전환 검증 및 재시도/보정을 도입, (2) 상태 표시 창과 무관하게 엔진이 계속 동작하도록 시스템 트레이 상주 구조를 채택, (3) 마커 창이 Alt+Tab/작업표시줄/Task View에 노출되지 않도록 하는 요구사항 추가.
- 2026-08-11 clarify 세션 (4차, 트레이→확인창 방식으로 재전환): 사용자가 "트레이 상주만으로는 검증이 안 되는 것 아니냐"고 재확인해, 트레이는 창 독립 동작만 보장하고 검증에는 여전히 데스크톱별 창이 필요함을 설명. 이어 사용자가 hidden 창 + 트레이 상주 대신 "일반 플로팅 창 + 닫을 때 종료 확인" 방식을 제안해 채택 — 트레이 상주와 hidden 마커 창 요구사항을 모두 제거하고, 데스크톱마다 보이는 플로팅 창(표시+검증 겸용)을 두고 닫으려 하면 확인 창을 띄운 뒤 확정 시 프로그램 전체가 종료되는 구조로 최종 확정(FR-004, FR-008~FR-010, FR-017~FR-019). User Story 2를 이 흐름에 맞게 재작성. Requirements/Edge Cases/Key Entities/Success Criteria/Assumptions 전반을 갱신. 재검증 결과 전 항목 통과 유지.
- 2026-08-11 clarify 세션 (5차): 두 가지 후속 질문 해소 — (1) 시작 시 데스크톱별 창을 사용자가 수동으로 옮길 필요는 없으며, 앱이 각 데스크톱을 자동 순회하며 창을 생성·배치함(FR-020, User Story 1 시나리오 5). (2) 세션 도중 데스크톱 순서 변경/추가/삭제는 시작 시 개수 불일치와 같은 원칙으로 범위 밖으로 명시하고, 기존 검증/재시도/보정(FR-017~019)을 안전망으로 문서화. Edge Cases와 Assumptions에 반영. 재검증 결과 전 항목 통과 유지.
- 2026-08-11 clarify 세션 (6차): 사용자가 직접 지정한 UI 요구사항 반영 — 플로팅 창의 초기 위치를 화면 상단 중앙(12시 방향)으로 고정하고, 이후 사용자가 자유롭게 드래그로 옮길 수 있도록 함(FR-021, User Story 3 시나리오 5·6). 재시작 시 위치가 초기 위치로 돌아간다는 가정을 Assumptions에 추가. 재검증 결과 전 항목 통과 유지.

# 결정: Windows 가상 데스크톱 자동 전환 + 상태 표시

- **Slug**: desk-rotate
- **Decided**: 2026-08-11
- **Verdict**: go
- **Artifacts reviewed**: intake.md, research.md, problem.md, concept.md

## 스코어카드

| 기준 | 평가 | 근거 |
|-----------|--------|---------------|
| Problem validity | adequate | problem.md의 핵심 미해결 항목("제안자/계기")이 decide 단계 진행 중 사용자 확인으로 해소됨 — 동기는 "집중력/포모도로식 강제 전환"으로, 잘 알려진 생산성 패턴과 일치. 다만 단일 사용자(제안자 본인)의 자기보고 외 외부 검증은 없음(개인용 도구 특성상 당연함). |
| Evidence strength | adequate | research.md 자체 평가가 "medium"(=adequate). 기술적 실현 가능성(비공식 API 부재, 대안 라이브러리)은 잘 인용되어 있으나, 수요 측 증거는 근본적으로 자기보고 수준. |
| Value vs. inaction | adequate | problem.md는 방치 시 비용을 "낮음"으로 평가했으나, 동기가 포모도로식 강제 전환으로 확인되면서 "강제성 자체가 가치"라는 점이 분명해짐 — 사용자가 직접 확인. |
| Feasibility / appetite | strong | concept.md의 추천 Option A는 small 예산으로, research.md가 지목한 핵심 리스크(비공식 COM 의존)를 아예 피하는 설계. |
| Strategic fit | unknown | 이 저장소의 constitution.md가 아직 템플릿 placeholder 상태([PROJECT_NAME] 등 미채움) — 비교할 공식 전략/원칙이 없음. 개인/탐색성 프로젝트라 이 자체가 진행을 막는 사유는 아님. |
| Risk posture | adequate | 가장 큰 리스크(Windows 업데이트에 따른 비공식 API 붕괴)는 Option A 선택으로 설계상 회피됨. 남은 리스크(수동 개입 시 카운트 어긋남)는 concept.md에 "검증이 필요한 가정"으로 명시적으로 인지·기록되어 있고, small 예산 범위 내에서 감당 가능한 수준.

## 판정 & 근거

**go**. problem validity와 evidence strength가 모두 adequate 이상이고(둘 다 필수 요건), concept.md에서 명확한 추천 옵션(Option A)이 이미 나와 있어 go의 세 가지 필수 조건을 충족한다. 결정적으로, 이번 decide 단계에서 사용자에게 직접 확인한 결과 problem.md의 가장 큰 공백이던 "왜 만들고 싶은가"와 "정말 강제 전환을 원하는가"가 모두 해소되었다: 사용자는 포모도로식 집중력 강제 전환을 원하며, 알림이 아닌 실제 자동 강제 전환을 명시적으로 선택했다. 이는 concept.md의 Option A/B 전제와 정확히 일치한다. Strategic fit은 unknown이지만, 이 저장소에 아직 확정된 constitution이 없고 이 프로젝트가 개인용 도구라는 점을 고려하면 go를 막을 사유가 아니다. Value vs. inaction과 risk posture는 "강제성 자체가 핵심 가치"라는 확인된 동기 덕분에 애매함이 줄었다고 판단해 adequate로 평가했다.

## Go — `/speckit-specify`로 인계

- **문제**: 여러 가상 데스크톱을 작업 맥락으로 나눠 쓰는 사용자가, 포모도로식 집중력 관리를 위해 정해진 간격으로 강제 전환되기를 원하지만, 지금은 그 전환을 스스로 챙겨야 하고 남은 시간·전환 이력에 대한 가시성도 없다.
- **채택 접근**: concept.md Option A — `Ctrl+Win+←/→` 키 입력 시뮬레이션 기반 최소 로테이터. Windows 비공식 COM 인터페이스에 의존하지 않음. 앱이 자체적으로 전환 횟수를 카운트(OS에 실제 값을 조회하지 않음). 작은 항상-위 플로팅 창에 다음 전환까지 남은 시간과, 앱이 자체 집계한 데스크톱별 전환 횟수를 표시.
- **범위 안 / 범위 밖**:
  - 범위 안: 정해진 간격 자동 강제 전환, 남은 시간 표시, 데스크톱별 전환 횟수(자체 추정치) 표시.
  - 범위 밖(concept.md 그대로 인계): OS 검증 기반 정확한 전환 횟수, 재시작 후 영속화, 시스템 트레이 상주/Windows 자동 시작, 일시정지·재개 등 런타임 제어 UI(최소 버전은 실행/종료만), 데스크톱 생성·이름 변경·삭제 등 데스크톱 자체 관리, Windows 외 플랫폼 지원.
- **성공 지표**: 정성적 — 별도 수동 리마인더 없이 며칠 이상 실제로 계속 켜두고 쓰는지, 남은 시간·전환 횟수를 즉시 확인할 수 있는지. 구체적 수치 기준은 specify 단계에서 필요 시 다듬는다.
- **이어지는 미해결 질문**:
  - [NEEDS CLARIFICATION: 전환 대상 범위 — 존재하는 모든 가상 데스크톱을 순환하는가, 사용자가 지정한 일부만인가?]
  - [NEEDS CLARIFICATION: 사용자가 실행 시 "총 데스크톱 개수"를 어떻게 입력/설정하는가?]
  - [NEEDS CLARIFICATION: 플로팅 창의 정확한 표시 항목과 UX(크기, 위치, 클릭 통과 여부, 항상-위 정도, 테마)는?]
  - [NEEDS CLARIFICATION: 사용자가 앱 밖에서 수동으로 데스크톱을 전환해 카운트가 실제와 어긋났을 때 — 그냥 어긋난 채로 둘지, 경고를 표시할지?]
  - [NEEDS CLARIFICATION: `Ctrl+Win+←/→` 키 입력 시뮬레이션이 다른 프로그램에 의해 가로채지는 환경에서의 대응(무대응/오류 표시 등)?]

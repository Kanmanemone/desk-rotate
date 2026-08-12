# 아이디어 인테이크: Windows 가상 데스크톱 자동 전환 + 플로팅 상태창

- **Slug**: desk-rotate
- **Created**: 2026-08-11
- **Source**: pasted text
- **Type**: new-capability

## 아이디어 (원문 그대로)

> Windows의 Virtual Desktop를 정해진 간격으로 이동하는 프로그램 만들거야. 그리고 Floating Window로 남은 시간 및 Desktop 별 전환 횟수 등을 표시하게 만들거야

## 재진술

Windows에서 정해진 간격으로 가상 데스크톱을 자동으로 전환하고, 다음 전환까지 남은 시간과 데스크톱별 전환 횟수를 보여주는 플로팅(항상 보이는) 창을 갖춘 프로그램을 만드는 제안이다.

## 배경 & 맥락

- **제안자**: [NEEDS CLARIFICATION: 입력에서 요청자나 이해관계자가 특정되지 않음]
- **계기**: [NEEDS CLARIFICATION: 이 아이디어를 촉발한 사건, 불편함, 동기가 명시되지 않음 — 개인 워크플로우 개선인지, 데모/실험인지 불명확]

## 1차 미확인 사항

- [NEEDS CLARIFICATION: 전환 간격은 고정값인가, 사용자가 설정 가능한가 (설정 가능하다면 어떤 UI/설정 방식인가)?]
- [NEEDS CLARIFICATION: 전환 범위/순서는 어떻게 되는가 — 존재하는 모든 가상 데스크톱을 순차 순환하는가, 사용자가 선택한 일부만인가, 특정 고정 개수인가?]
- [NEEDS CLARIFICATION: Windows는 가상 데스크톱 관리를 위한 공식 공개 API가 없다(Win11 22H2 이전 기준). 비공식 COM 인터페이스(IVirtualDesktopManager 계열)를 쓸 것인지, `Ctrl+Win+화살표` 키 입력을 시뮬레이션할 것인지, 아니면 Windows 11의 신규 Virtual Desktop API를 쓸 것인지 — 대상 Windows 버전도 명시되지 않음.]
- [NEEDS CLARIFICATION: 플로팅 창에 "남은 시간"과 "데스크톱별 전환 횟수" 외에 정확히 어떤 항목이 표시되어야 하는가 — 예: 현재 데스크톱 이름/번호, 일시정지/재개 컨트롤, 총 경과 시간 등?]
- [NEEDS CLARIFICATION: 실행 중 회전 시작/중지/일시정지/재설정은 사용자가 어떻게 조작하는가?]
- [NEEDS CLARIFICATION: 데스크톱별 전환 횟수와 설정값은 앱 재시작 후에도 유지되어야 하는가, 아니면 세션마다 초기화되는가?]
- [NEEDS CLARIFICATION: Windows 시작 시 자동 실행, 단일 인스턴스 강제, 시스템 트레이 상주 등의 요구사항이 있는가?]
- [NEEDS CLARIFICATION: 플로팅 창의 시각적/UX 제약은 무엇인가 — 항상 위(always-on-top), 클릭 통과, 위치 드래그 가능 여부, 투명도, 테마 등?]

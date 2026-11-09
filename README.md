# K-OPT 2026

Windows와 macOS를 대상으로 하는 교육용 선형·정수 최적화 데스크톱 프로그램입니다. `.opt` 파일을 UTF-8/CP949로 열고 문법을 진단한 뒤 HiGHS로 LP/MIP를 계산합니다. 2변수 모델은 실행가능영역과 최적점을 그래프로 확인할 수 있습니다.

## 내려받아 실행하기

사용하는 컴퓨터에 맞는 릴리즈 파일을 내려받아 압축을 해제합니다.

| 컴퓨터 | 릴리즈 대상 | 실행 파일 |
|---|---|---|
| Windows 64비트 | `win-x64` ZIP | `Kopt2026.App.exe` |
| M 시리즈 Mac | `osx-arm64` TAR.GZ | `K-OPT 2026.app` |
| Intel Mac | `osx-x64` TAR.GZ | `K-OPT 2026.app` |

**Mac 사용자 안내:** [Mac 실행 오류 해결 매뉴얼](kanban-board-docs/public/manual/K-OPT_2026_Mac_실행오류_해결_매뉴얼.html)을 참고하세요. HTML 파일을 내려받아 브라우저에서 열 수 있습니다.

## 시작하기

- [실행·릴리즈 생성 안내](kanban-board-docs/public/manual/02_공통앱_실행과배포.md)

- [간단 사용 설명서](kanban-board-docs/public/manual/01_K-OPT_2026_간단사용설명서_0.2.md)
- [안내·이용 조건·제3자 라이선스](kanban-board-docs/public/legal/THIRD-PARTY-NOTICES.txt)
- [제3자 구성요소 정보](kanban-board-docs/public/legal/THIRD_PARTY_NOTICES.md)
- [공개 문서 목록](kanban-board-docs/public/00_문서안내_v0.3.md)

K-OPT 2026의 재배포·수정·파생 프로그램 작성 시 출처를 표시해야 합니다. 원본 K-OPT와의 관계와 상세 조건은 위 고지 원문을 확인하세요.

## 개발 실행

.NET 10 SDK를 설치한 뒤 저장소 루트에서 실행합니다.

```sh
dotnet run --project kanban-board/src/Kopt2026.App
```


## 주요 기능

- **모델 작성 및 검증**: 목적함수·제약식·정수변수 조건을 입력하고 문법 오류를 확인합니다.
- **선형·정수 최적화**: HiGHS를 사용해 선형계획(LP)과 혼합정수계획(MIP) 모델의 해를 계산합니다.
- **결과 조회**: 목적값, 변수별 해, 제약식의 계산 결과와 충족 여부를 확인합니다.
- **민감도 및 복수 최적해 분석**: 연속형 최적 LP에서 목적계수나 제약식 우변을 한 항목씩 변경할 때의 허용 범위를 확인합니다. 최적해의 유일성과 변수별 최적 범위·대표해도 분석합니다.
- **결과 시각화**: 2변수 모델의 실행가능영역과 최적해를 그래프로 확인하고, 다변수 모델의 변수값을 막대그래프로 비교합니다.
- **파일 호환 및 내보내기**: `.opt` 모델을 불러오고 저장하며, 구형 K-OPT 문법을 가져옵니다. 계산 결과를 TXT·CSV·JSON으로 내보냅니다.

## 공통 앱 미리보기

0.3.4-preview는 Windows와 Mac의 화면·계산·파일 처리를 하나로 통합한 미리보기입니다. Mac의 실제 입력기·네이티브 창 동작은 별도 검증이 필요합니다.

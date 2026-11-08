# K-OPT 2026

Windows용 교육용 선형·정수 최적화 데스크톱 프로그램입니다. `.opt` 파일을 UTF-8/CP949로 열고 문법을 진단한 뒤 HiGHS로 LP/MIP를 계산합니다. 2변수 모델은 실행가능영역과 최적점을 그래프로 확인할 수 있습니다.

## 시작하기

- [간단 사용 설명서](kanban-board-docs/public/manual/01_K-OPT_2026_간단사용설명서_0.1.md)
- [안내·이용 조건·제3자 라이선스](kanban-board-docs/public/legal/THIRD-PARTY-NOTICES.txt)
- [제3자 구성요소 정보](kanban-board-docs/public/legal/THIRD_PARTY_NOTICES.md)
- [공개 문서 목록](kanban-board-docs/public/00_문서안내_v0.2.md)

K-OPT 2026의 재배포·수정·파생 프로그램 작성 시 출처를 표시해야 합니다. 원본 K-OPT와의 관계와 상세 조건은 위 고지 원문을 확인하세요.

## 개발 실행

Windows와 .NET 10 SDK가 필요합니다. 저장소 루트에서 실행합니다.

```powershell
dotnet restore kanban-board/Kopt2026.slnx
dotnet build kanban-board/Kopt2026.slnx -c Release
dotnet test kanban-board/Kopt2026.slnx -c Release --no-build
dotnet run --project kanban-board/src/Kopt2026.App/Kopt2026.App.csproj
```

## 주요 기능과 단축키

- F5: 실행, Shift+F5: 취소, Ctrl+Shift+B: 검증, Ctrl+O / Ctrl+S: 열기 / 저장.
- 목적식은 `MAX/MIN`과 `ST` 사이에 여러 줄로 작성할 수 있습니다. 목적식 독립 상수항도 지원합니다.
- 2변수 그래프에서 실행가능영역·제약 경계·목적함수·최적점과 X/Y절편을 확인합니다. 절편 라벨은 겹침을 피하도록 배치되며 오른쪽 요약에서도 확인할 수 있습니다.
- `설정 → 그래프에 X·Y절편 표시`로 절편을 켜고 끕니다. 변수가 3개 이상이면 스크롤 가능한 변수값 막대그래프를 표시합니다.
- 목적값 카드와 `복수 최적해` 탭에서 판정·변수별 범위·최대 10개의 대표해를 확인합니다. 대표해는 전체 열거가 아니며 기본 10초 분석으로 증명하지 못하면 판정 미완료로 표시합니다.
- 연속형 최적 LP의 `민감도 분석` 탭에서 목적계수와 우변의 허용 범위를 확인합니다. 범위는 한 번에 하나의 값을 바꾸는 기준이며 MIP에는 제공하지 않습니다.
- `설정 → 결과 표 항목을 한글로 표시`로 결과 용어를 전환합니다.
- `설정 → 소수 자릿수`에서 0~15자리, 기본 6자리를 선택합니다. 숫자는 천 단위 구분기호와 함께 표시하고 불필요한 끝 0은 생략합니다. 설정은 다음 실행에도 유지됩니다.
- 소수 자릿수 설정은 표시를 반올림하며 계산값의 정밀도는 유지합니다. 문서를 변경하면 이전 결과를 비웁니다.

새 문서는 UTF-8(BOM 없음)로 원자적으로 저장합니다. 레거시 CP949 문서는 자동 감지하고 구형 문법 가져오기는 첫 저장을 다른 이름으로 유도해 원본을 보호합니다.

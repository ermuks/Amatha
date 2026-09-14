# 아맛다보고서 (Amaranth10API)

현재 버전은 `Directory.Build.props` 기준 **1.3.0**입니다.

TEIA Amaranth 10 ERP에 로그인해, **작성하지 않은 출장·휴일근무 보고서**를 찾아 알려 주고, 보고서 작성 화면을 열어 초안을 채워 주는 Windows 데스크톱 앱입니다. 배포용으로 `Installer` 프로젝트(`아맛다보고서Install.exe`)가 본 프로그램 zip을 내장해 설치합니다. 실행 중 GitHub(`ermuks/Amatha`)에 더 새 버전이 있으면 창 아래에서 설치기를 받아 올립니다.

표시 이름(어셈블리명·제품명)은 **아맛다보고서**, 코드 루트 네임스페이스와 프로젝트 파일명은 `Amaranth10API`입니다.

---

## 1. 이 프로그램이 하는 일

사내 ERP(`https://erp.teia.co.kr`)의 근태 일정과 전자결재 문서(신청서·보고서)를 조회한 뒤, 아래를 비교합니다.

| 대상 | 신청서 | 대응하는 보고서 | 판정 |
| --- | --- | --- | --- |
| 출장 | `출장신청서` (FormId 40) | 양식명 또는 제목에 **출장&휴일근무보고서** | 출장 기간이 해당 날짜를 덮으면 작성됨 |
| 휴일근무 | `휴일근무신청서` (FormId 43) | 같은 보고서 안의 **휴일근무** 행 | 근무일이 일치하면 작성됨 |

근태 캘린더에 아직 안 올라온 **예정 출장·휴일근무**도 신청서 기간으로 카드에 올립니다. 제목·양식명에 `취소신청`/`상신취소`가 있으면 그 날짜는 미작성에서 뺍니다.

작성되지 않은 구간은 대시보드에 카드로 보여 주고, 카드를 누르면 ERP의 보고서 작성 팝업을 WebView2로 연 다음 기간·구분·휴일근무 시각을 자동 입력합니다.

브라우저로 ERP에 들어가 목록을 훑는 일을 줄이는 것이 목적입니다. 결재 상신 자체는 ERP 화면에서 사용자가 마무리합니다.

---

## 2. 기술 스택과 실행 환경

| 항목 | 내용 |
| --- | --- |
| UI | WPF (`UseWPF`) |
| 트레이·화면 좌표·설치 경로 선택 | Windows Forms (`UseWindowsForms`) |
| 대상 프레임워크 | .NET Framework 4.8 (`net48`) |
| 플랫폼 | x64 |
| 언어 | C# latest, nullable 활성, implicit usings |
| HTTP | `System.Net.Http` + `CookieContainer` |
| JSON | `System.Text.Json` 8.0.5 |
| 내장 브라우저 | Microsoft.Web.WebView2 1.0.3179.45 |
| Windows 토스트·AUMID | Microsoft.Windows.SDK.Contracts 10.0.22621.2428 |
| DPI | `app.manifest`에서 PerMonitorV2 |
| 아이콘 | `Assets/AppIcon.ico` |
| 버전 | `Directory.Build.props` → `AppVersion` |

출력 형식은 `WinExe`입니다. 콘솔이 아니라 창(또는 트레이)으로 실행됩니다.

WebView2 Runtime(Edge 기반)이 없으면 보고서 작성 창과 사용자 매뉴얼 창을 열 수 없습니다. 로그인·목록 조회는 HTTP만으로 동작합니다.

`Helpers/IsExternalInit.cs`는 net48에서 `init` 접근자를 쓰기 위한 폴리필입니다.

한 PC에서 본 프로그램은 **한 프로세스만** 돕니다. 두 번째 실행은 기존 창을 앞으로 당기고 바로 종료합니다. `--autostart`로 켜진 두 번째 인스턴스는 창을 건드리지 않고 끝냅니다.

---

## 3. 디렉터리 구조

```
Amaranth10API/
├── Directory.Build.props     앱·설치기 공통 버전 (1.3.0)
├── AppVersion.cs             표시용 버전 문자열·비교용 Version
├── Amaranth10API.csproj      본 프로그램, 매뉴얼 복사
├── app.manifest              DPI / Windows 10 호환
├── App.xaml / App.xaml.cs    테마, 단일 인스턴스, 공유 Http 클라이언트
├── MainWindow.xaml(.cs)      셸: 프레임, 사이드 메뉴, 환경설정, 트레이, 업데이트 링크
├── Models/
│   ├── Models.cs             세션·일정·결재·미작성 구간 모델
│   └── AppSettings.cs        환경설정 (INotifyPropertyChanged)
├── ViewModels/
│   ├── ObservableObject.cs   속성 변경 통지 기반
│   └── ViewModels.cs         Login / Dashboard / MissingPeriod
├── Services/
│   ├── AmaranthClient.cs     ERP 로그인·조회·파싱
│   └── UpdateChecker.cs      GitHub 버전 확인·설치기 다운로드
├── Views/
│   ├── LoginPage             로그인
│   ├── DashboardPage         미작성 목록
│   ├── ReportBrowserWindow   WebView2 보고서 작성
│   ├── ManualWindow          WebView2 사용자 매뉴얼
│   ├── TrayMenuWindow        트레이 우클릭 메뉴
│   └── ConfirmDialog         종료 확인 (UserControl)
├── Helpers/                  설정·자격 증명·알림·초안 입력·결재 상태
├── docs/
│   └── 사용매뉴얼.html       빌드 시 ReadMe.html 로 복사
├── dist/                     설치기 빌드 후 복사되는 배포 파일
├── Assets/AppIcon.ico
└── Installer/                아맛다보고서Install (관리자 설치기)
```

본 프로그램 csproj는 `Installer/**`를 컴파일에서 제외합니다. 설치기는 별도 프로젝트입니다.

빌드 산출물(`bin/`, `obj/`)과 게시 폴더는 `.gitignore`에 있습니다.

---

## 4. 아키텍처

전형적인 **View – ViewModel – Service** 구조입니다. DI 컨테이너는 없고, 앱 전역 싱글톤으로 상태를 공유합니다.

```
App (Application)
 ├── Client          AmaranthClient  (쿠키·세션 HTTP)
 ├── StartedFromWindowsStartup  (--autostart 여부)
 └── 단일 인스턴스 Mutex / Activate 이벤트

AppRuntime.Current
 ├── Settings        SettingsStore (settings.json)
 ├── Session         로그인 후 AmaranthSession
 ├── LoginId/Password  재로그인·자동 로그인용 메모리 보관
 └── Dashboard       현재 DashboardViewModel

CredentialStore      %AppData%\아맛다보고서\login.bin  (DPAPI)
SettingsStore        %AppData%\아맛다보고서\settings.json
```

- **View**는 입력·클릭만 처리하고, 목록 상태와 미작성 계산은 ViewModel에 둡니다.
- **AmaranthClient**가 ERP API 호출, HTML/텍스트 파싱, 미작성 기간 그룹화를 담당합니다.
- **AppRuntime**이 설정 저장, 자동 로그인, 주기적 새로고침, 네트워크 재연결 시 재로그인을 묶습니다.
- **UpdateChecker**가 GitHub의 `Directory.Build.props`·릴리스와 현재 `AppVersion.Current`를 비교합니다.

시작 순서 (`App.OnStartup`):

1. 인자 `--autostart`가 있으면 Windows 시작 프로그램에서 켠 것으로 표시합니다.
2. Mutex `Local\Amaranth10.아맛다보고서`로 이미 실행 중인지 봅니다. 있으면(일반 실행만) `Activate` 이벤트로 기존 창을 띄우고 `Shutdown`합니다.
3. TLS 1.2를 켭니다.
4. `WindowsNotification.Initialize()`로 AUMID `Teia.AmattahReport`를 등록하고 시작 메뉴 바로가기를 만듭니다.
5. `AppRuntime.Current`를 만들어 설정을 읽고, Windows 시작 등록을 설정값에 맞춥니다.
6. `StartupUri="MainWindow.xaml"`로 메인 창을 엽니다. 창이 뜨면 `UpdateChecker.CheckAsync`를 백그라운드로 돌립니다.

---

## 5. 화면과 사용자 흐름

### 5.1 로그인 (`Views/LoginPage`)

아이디·비밀번호를 `AmaranthClient.LoginAsync`에 넘깁니다. 성공하면 `AppRuntime.RememberLogin`으로 세션을 기억하고 대시보드로 이동합니다.

- Enter 키로 로그인할 수 있습니다.
- 진행 중에는 입력란과 버튼이 비활성화됩니다.
- 실패 메시지는 API `resultMsg`를 그대로 보여 줍니다.

환경설정에서 **자동 로그인**이 켜져 있으면, 메인 창 로드 시 `TryRestoreSessionAsync`가 `%AppData%`의 암호를 풀어 다시 로그인합니다.

- 일반 실행: 최대 3회, 2초 간격
- `--autostart`(부팅 직후): 최대 12회, 10초 간격  
  부팅 직후 네트워크가 아직 없을 때를 대비합니다.

### 5.2 대시보드 (`Views/DashboardPage`)

로딩 중에는 진행 문구(출장보고서 → 근태 일정 → 신청서)를 보여 줍니다.

완료 후 두 목록을 그립니다.

1. **출장 보고서 미작성**
2. **휴일근무 보고서 미작성**

둘 다 없으면 “지금은 비어 있습니다” 카드를 보여 줍니다.

각 카드는 `MissingPeriodViewModel`입니다.

- 근태명(국내출장/해외출장/휴일근무)
- 기간 문자열, 일수
- 신청서의 프로젝트 코드(있으면, `26-1029. 프로젝트명` 형태)
- 겹치는 신청서 제목(있으면 Hint)
- 오늘이 기간 **앞**이면 “출장 전 · 알림을 보내지 않습니다” (휴일근무도 동일 패턴)
- 오늘이 기간 **안**이면 `IsOngoing` — 초록 톤 카드, “현재 출장 중 · …” / “휴일근무 중 · …”
- 연결된 신청서가 **진행**(코드 30)이면 “결재가 아직 종결되지 않았습니다”를 덧붙입니다
- **알림(`ShouldNotify`)은 기간이 지난 뒤에만** 켭니다
- 클릭 시 `ReportBrowserWindow.OpenDraft`으로 작성 창을 엽니다

작성 창을 닫으면 대시보드를 조용히 다시 불러와 목록을 갱신합니다.

### 5.3 메인 셸 (`MainWindow`)

로그인 전에는 왼쪽 패널이 숨겨집니다. 대시보드에 들어가면 햄버거 메뉴가 나타납니다. 창 아래 상태 줄 오른쪽은 `ver 1.3.0` (`AppVersion.FooterLabel`), 왼쪽은 새 버전이 있을 때만 “새 버전이 있습니다. (v…)” 링크입니다.

| 메뉴 | 위치 | 동작 |
| --- | --- | --- |
| 사용자 매뉴얼 | 위 | `ManualWindow`로 `ReadMe.html` 표시 |
| 업데이트 확인 | 위 | GitHub 버전을 바로 확인. 최신이면 안내, 새 버전이면 하단 링크 |
| 환경설정 | 아래 | 설정 오버레이. 닫을 때 새로고침 타이머를 다시 잡습니다. |
| 로그아웃 | 아래 | 저장 로그인을 지우고 로그인 화면으로 |
| 종료하기 | 아래 | 확인 대화상자 후 실제 종료 |

창을 닫기(X)하면 종료하지 않고 **트레이로 숨깁니다**. 완전히 끄려면 메뉴 또는 트레이에서 종료를 고르고 확인해야 합니다.

Windows 시작 + **트레이로 시작**이 켜져 있으면, 첫 렌더 후 창을 숨기고 작업 표시줄에도 올리지 않습니다.

### 5.4 보고서 작성 창 (`ReportBrowserWindow`)

WebView2로 ERP 팝업 URL을 엽니다.

```
https://erp.teia.co.kr/#/popup?MicroModuleCode=eap&formId=208&callComp=UBAP001&popupUUID={guid}
```

`formId=208`이 출장&휴일근무보고서 양식입니다.

로그인 쿠키(`oAuthToken`, `signKey`, `BIZCUBE_*`)를 WebView2 쿠키 매니저에 넣어, 별도 웹 로그인 없이 작성 화면이 열리게 합니다. 프로필은 `%LocalAppData%\아맛다보고서\WebView2`에 둡니다.

ERP가 새 창을 요청하면 자식 `ReportBrowserWindow`를 만들고 `NewWindow`에 연결합니다.

주소창은 읽기 전용입니다. 복사만 가능합니다.

### 5.5 사용자 매뉴얼 (`Views/ManualWindow`)

빌드 후 실행 폴더의 `ReadMe.html`(원본 `docs/사용매뉴얼.html`)과 `images/`를 WebView2로 엽니다. 가상 호스트 `app.manual`에 폴더를 매핑합니다. 프로필은 `%LocalAppData%\아맛다보고서\WebView2Manual`입니다.

파일이 없으면 안내 메시지박스를 띄웁니다.

### 5.6 트레이 메뉴·확인 대화상자

트레이 왼쪽 클릭: 창을 다시 보여 줍니다.  
오른쪽 클릭: `TrayMenuWindow` — “GUI 보이기”, “종료하기”. 작업 표시줄 근처·커서 위치에 맞추고, 포커스를 잃으면 닫습니다.

`ConfirmDialog`는 별도 Window가 아니라 메인 창 위의 **UserControl 오버레이**입니다. Esc·바깥 클릭은 취소, Enter는 확인입니다. 메인 창이 아니면 작은 모달 창으로 떨어집니다.

### 5.7 업데이트 링크

시작할 때와 이후 **1시간마다** GitHub 버전을 봅니다. 햄버거 메뉴의 **업데이트 확인**은 바로 한 번 더 봅니다. 새 버전이 있으면 상태 줄 왼쪽 링크가 보이고, 버튼으로 확인했는데 이미 최신이면 안내만 띄웁니다. 링크를 누르면 확인 후 설치기를 받아 `runas`로 실행하고 본 프로그램을 종료합니다. 관리자 확인을 취소하거나 다운로드가 실패하면 링크를 되돌립니다. 백그라운드 확인이 실패해도 앱은 그대로 씁니다.

---

## 6. ERP 연동 (`Services/AmaranthClient`)

서버는 `https://erp.teia.co.kr`, 그룹 시퀀스는 `gcmsAmaranth35867`입니다. HttpClient는 쿠키·GZip을 쓰고 User-Agent를 브라우저처럼 맞춥니다.

### 6.1 로그인

1. `GET /get_token/?url=/gw/gw050A02` — 임시 토큰·시각
2. SHA256(`token + cur_date + transactionId + path`)을 Base64로 서명
3. `POST /gw/gw050A02`  
   - 먼저 `loginType=checkLoginId`로 아이디 확인 (아이디는 Base64)  
   - 이어서 `loginId`/`password`/`groupSeq` 등 폼으로 실제 로그인 (`isPlainText=true`)
4. `resultCode == 200`이면 `sessionInfo`에서 `auth_a_token`, `hash_key`, 사원·회사·부서 정보를 꺼내 `AmaranthSession`을 만듭니다.
5. 쿠키 `oAuthToken`, `signKey`, `BIZCUBE_AT`, `BIZCUBE_HK`, `BIZCUBE_TYPE=WEB`를 넣습니다.

이후 API는 HMAC-SHA256입니다.

```
Wehago-Sign = HMACSHA256(hashKey, authToken + transactionId + timestamp + requestPath)
Authorization: Bearer {authToken}
```

`Menu-Code`는 화면마다 다릅니다 (`HPD0110` 근태, `UBA` 결재).

### 6.2 대시보드 데이터 로드 순서

`LoadDashboardAsync(session, 오늘, settings)`:

1. **출장&휴일근무보고서** — 설정한 보고서 월 범위
2. **근태 일정** — 신청서 범위와 보고서 범위의 합집합
3. **신청서** — 출장신청서 + 휴일근무신청서, 설정한 신청서 월 범위

결재 목록은 `/eap/eap105A04`를 페이지 크기 100으로 끝까지 돌립니다. 필터는 `eaBoxId=1000900`(완료함 계열), `periodPicker=ACTION_TIME`입니다.

- 보고서: 양식명 또는 제목에 `출장&휴일근무보고서`. **반려**(상태명 `반려` 또는 코드 `100`)는 건너뜁니다
- 출장신청서: `FormId == 40` 또는 양식명에 `출장신청서` (보고서 문서는 제외). 반려는 건너뜁니다
- 휴일근무신청서: `FormId == 43` 또는 양식명/제목에 `휴일근무신청서`. 반려는 건너뜁니다

상세는 `/eap/eap111A04` (`bindType=V`)입니다. 본문은 `contentsWord`(텍스트)와 `docContents`(HTML)입니다.

근태는 `/human/attendapplication/at00001`입니다. 승인 상태 `0,1,4,5`와 다수의 `linkAtCd`를 넘깁니다.

### 6.3 문서 본문 파싱

ERP 양식이 고정 JSON이 아니라 HTML·워드 텍스트라, 정규식으로 필드를 뽑습니다.

**출장신청서**

- `출장기간 yyyy-MM-dd ~ yyyy-MM-dd`와 시각
- 본문에 기간이 없으면 제목의 `(M-D ~ M-D)` 형태를 보조로 씁니다
- `프로젝트코드` 라벨 옆 칸(HTML) 또는 같은 라벨 뒤 텍스트

**휴일근무신청서**

- 근무일 목록 `HolidayWorkDay`(날짜·시작/종료 시각)
- 기간은 근무일 min/max
- 출장신청서와 같은 방식으로 프로젝트 코드를 읽습니다

**출장&휴일근무보고서**

- “출장 기간” 구간의 년/월/일, 또는 HTML `<option selected>` 6개
- 문서 연도와 기간 연도가 어긋나면, 문서일 기준으로 ± 보정을 시도합니다
- 휴일근무 행: 날짜 + 시간 구간, 보상 유형(`휴일근무수당`/`대체휴무`), 대체휴무일 텍스트

파싱이 실패하면 해당 문서는 기간이 비어 미작성으로 남을 수 있습니다.

---

## 7. 미작성 보고서 판정

근태 캘린더와 신청서를 합쳐 날짜 집합을 만든 뒤, 보고서가 없는 날만 남깁니다. 취소로 상쇄된 날과 **반려 문서**는 뺍니다. 결재 상태 판정은 `Helpers/ApprovalStatus.cs`입니다 (`반려`/`100`, `진행`/`30`, `종결`/`90`).

### 출장

대상 날짜:

- 근태명이 `해외출장` 또는 `국내출장`인 날 (제목/양식에 취소 표시가 없는 것)
- **종결된** 출장신청서가 덮는 날 (캘린더에 아직 없어도 포함). 제목에 `해외`가 있으면 해외출장으로 표시. 진행 중인 신청서만으로는 날짜를 넣지 않습니다

그 날짜가 어떤 **반려가 아닌** 보고서의 `TripStartDate`~`TripEndDate`에도 안 들어가면 미작성입니다. 연속된 같은 이름 날짜를 하나의 `MissingReportPeriod`로 묶고, 겹치는 출장신청서를 `RelatedApplication`에 붙입니다. 종결 문서가 있으면 진행 문서보다 먼저 고릅니다.

### 휴일근무

대상 날짜:

- 근태명이 `휴일근무`인 날
- **종결된** 휴일근무신청서의 `WorkDays`/`CoveredDates`(없으면 신청 기간)

보고서 `HolidayWorks`에 같은 날짜가 없으면 미작성입니다. 겹치는 휴일근무신청서를 카드 Hint·초안 시각에 씁니다.

### 취소

제목 또는 양식명에 `취소신청` / `상신취소`가 있으면 `IsCancellation`입니다. 같은 날짜를 덮는 **종결** 신청서 중 **가장 늦은 문서가 취소**이면 그 날은 미작성에서 제외합니다.

### 결재 상태

| 상태 | 이름 | 코드 | 동작 |
| --- | --- | --- | --- |
| 반려 | `반려` | `100` | 신청서·보고서 모두 없는 것으로 취급 |
| 진행 | `진행` | `30` | 상세는 읽되, 예정 날짜 집합에는 넣지 않음. 카드에 종결 대기 문구 |
| 종결 | `종결` | `90` | 예정 출장·휴일근무 날짜와 취소 상쇄에 사용 |

### 알림에서 빼는 경우

`ShouldNotify`는 **오늘이 기간 종료일보다 이후일 때만** true입니다. 예정·진행 중 카드는 보이되 트레이 알림 건수에는 넣지 않습니다.

---

## 8. 보고서 초안 자동 입력 (`Helpers/ReportDraftFiller`)

카드의 `ReportDraftFill`을 JSON으로 만든 뒤, WebView2에 스크립트를 넣습니다. 최대 약 25초 동안 DOM(iframe 포함)을 찾아 채웁니다. 채울 기간은 **미작성 구간 날짜**입니다 (신청서 전체 기간이 아님).

출장 카드:

- 출장기간 년/월/일
- 출장일수
- 구분: 1일이면 당일, 그 이상이면 숙박
- 출장구분: 항상 “프로젝트”

휴일근무 카드:

- 휴일근무 표의 날짜·요일
- 시작~종료 시각은 신청서 `WorkDays`의 해당 일(없으면 신청서 공통 시각). 시각이 없으면 시간 칸은 비워 둡니다
- 근무 시간에 따라 일수 0.5 또는 1

프로젝트 코드 칸은 자동 입력하지 않습니다. 리치 텍스트 에디터(`.tox`, TinyMCE 등)는 건드리지 않습니다. 업무 내용 본문은 사용자가 씁니다. 스크립트 실패는 창을 닫지 않습니다.

---

## 9. 데이터 모델 (`Models/Models.cs`)

| 형식 | 역할 |
| --- | --- |
| `AmaranthSession` | 토큰, 해시 키, 사원/회사/부서 |
| `WorkSchedule` | 하루 근태 + 출장 부가 정보 |
| `ApprovalDocumentSummary` / `Page` | 결재 목록 한 페이지 (`DocumentStatus`, `DocumentStatusCode`) |
| `BusinessTripDocument` | 출장·휴일근무 신청서 상세 (`WorkDays`, `CoveredDates`, `IsCancellation`) |
| `HolidayWorkDay` | 휴일근무신청서의 하루 시각 |
| `BusinessTripReport` | 보고서 상세 + `HolidayWorks` |
| `HolidayWorkEntry` | 보고서 안 휴일근무 한 행 |
| `MissingReportPeriod` | 미작성 연속 구간 |
| `ReportDraftFill` / `ReportHolidayDayFill` | WebView2 자동 입력 페이로드 |

`AmaranthClient`는 `Schedules`, `BusinessTripDocuments`, `HolidayWorkDocuments`, `BusinessTripReports`를 멤버로 들고, 로드할 때마다 비운 뒤 다시 채웁니다.

---

## 10. 환경설정

`AppSettings`는 속성 변경 시 `settings.json`에 바로 저장됩니다.

| 설정 | 기본 | 의미 |
| --- | --- | --- |
| `NotificationsEnabled` | false | 기간이 지난 미작성 건이 있으면 트레이 풍선 알림 |
| `NotificationIntervalSeconds` | 3600 | 백그라운드 재조회 주기 (타이머는 최소 10초) |
| `ShowGuiOnNotification` | false | 알림 때 메인 창을 앞으로 |
| `KeepSessionOnReconnect` | false | 네트워크가 다시 붙으면 메모리의 ID/PW로 재로그인 |
| `AutoLoginEnabled` | false | DPAPI로 로그인 저장, 시작 시 복원 |
| `RunOnWindowsStartup` | true | 시작 폴더에 `--autostart` 바로가기 |
| `StartInTray` | false | 시작 프로그램으로 켜졌을 때만 트레이로 시작 |
| `ApplicationMonthsBefore/After` | 3 / 1 | 신청서 조회 월 범위 |
| `ReportMonthsBefore/After` | 3 / 1 | 보고서 조회 월 범위 |

월 범위는 “이번 달 1일”을 기준으로 이전 N개월 초 ~ 이후 M개월 말일입니다.

숫자 칸은 숫자만 입력·붙여넣기됩니다.

자동 로그인을 끄면 `CredentialStore.Delete()`로 저장 로그인을 지웁니다.

---

## 11. 버전

앱과 설치기가 같은 `Directory.Build.props`를 씁니다.

```
PATCH(세 번째): 수정/버그 수정
MINOR(두 번째): 기능 추가
MAJOR(첫 번째): 대규모 변경
```

`AppVersion`은 `AssemblyInformationalVersion`을 읽고(`+` git 해시는 자름), 없으면 `Major.Minor.Build`를 씁니다. `Current`는 비교용 `System.Version`입니다.

- 본 프로그램 하단: `ver 1.3.0`
- 설치기 첫 버튼: `아맛다보고서 1.3.0 설치`

버전을 올릴 때는 `Directory.Build.props`만 고치면 됩니다. GitHub `main`의 같은 파일과 최신 릴리스 태그가 업데이트 확인의 기준입니다.

---

## 12. 자동 업데이트 (`Services/UpdateChecker`)

저장소는 GitHub `ermuks/Amatha`, 브랜치 `main`입니다. 확인 제한 시간은 12초입니다.

최신 버전은 둘 중 큰 쪽입니다.

1. `GET https://api.github.com/repos/ermuks/Amatha/releases/latest`의 `tag_name` (앞의 `v`는 무시)
2. `raw.githubusercontent.com/.../Directory.Build.props`의 `<Version>` 값

현재 `AppVersion.Current`보다 크지 않으면 링크를 숨깁니다.

설치기 주소 우선순위:

1. 최신 릴리스가 이긴 경우 그 릴리스의 `*Install.exe` / `아맛다보고서Install.exe` 에셋
2. `https://github.com/ermuks/Amatha/raw/main/dist/아맛다보고서Install.exe`
3. Git LFS용 `media.githubusercontent.com/.../dist/아맛다보고서Install.exe`

받은 파일이 64KB 미만이거나 Git LFS 포인터(`version https://git-lfs.github.com/spec/v1`)이면 다음 URL을 시도합니다. 성공하면 `%TEMP%\아맛다보고서Install.exe`에 두고 `Verb=runas`로 실행합니다.

User-Agent는 `AmathaBogoso/{버전}`입니다.

---

## 13. 설치기 (`Installer`)

프로젝트 `Installer/Amaranth10Installer.csproj`, 출력 이름은 `아맛다보고서Install.exe`입니다. `app.manifest`에서 **관리자 권한**을 요청합니다. 기본 설치 경로가 `C:\Program Files (x64)\AmathaBogoso\`이기 때문입니다.

빌드 전에 본 프로그램을 같은 Configuration으로 먼저 빌드해야 합니다. `PreparePayload`가 `bin\$(Configuration)\net48\`의 `아맛다보고서.exe`와 매뉴얼·의존 dll을 zip으로 묶어 `payload.zip` 임베디드 리소스로 넣습니다 (pdb/xml/zip 제외).

설치 흐름:

1. 환영 화면 — `아맛다보고서 {버전} 설치`
2. 경로 입력·폴더 찾아보기
3. 진행률 — 실행 중인 `아맛다보고서` 프로세스를 닫고 zip을 풉니다 (zip-slip 방지로 대상 폴더 밖 경로는 거부)
4. 완료 — “아맛다 보고서 실행하기”가 기본 체크. explorer로 exe를 켭니다

화면 톤은 본 프로그램과 달리 큰 하늘색 버튼·맑은 고딕입니다.

빌드가 끝나면 `CopyInstallerToDist`가 `dist/아맛다보고서Install.exe`로 복사합니다. 인앱 업데이트가 GitHub `dist/`에서 이 파일을 받습니다.

---

## 14. 로컬 저장 위치

| 경로 | 내용 |
| --- | --- |
| `%AppData%\아맛다보고서\settings.json` | 환경설정 |
| `%AppData%\아맛다보고서\login.bin` | DPAPI(`CurrentUser`)로 막은 아이디·비밀번호. 엔트로피 문자열 `Teia.AmattahReport.Login` |
| `%LocalAppData%\아맛다보고서\WebView2` | 보고서 작성 WebView2 프로필 |
| `%LocalAppData%\아맛다보고서\WebView2Manual` | 매뉴얼 WebView2 프로필 |
| `%AppData%\Microsoft\Windows\Start Menu\Programs\아맛다보고서.lnk` | 토스트 AUMID용 바로가기 |
| `%AppData%\Microsoft\Windows\Start Menu\Programs\Startup\아맛다보고서.lnk` | Windows 시작 시 실행 (`--autostart`) |
| `C:\Program Files (x64)\AmathaBogoso\` | 설치기 기본 설치 폴더 |
| `%TEMP%\아맛다보고서Install.exe` | 인앱 업데이트가 받은 설치기 |

비밀번호는 현재 Windows 사용자만 풀 수 있습니다. 다른 계정·다른 PC로는 복호화되지 않습니다. 예전 Run 레지스트리 값은 시작 등록 시 지웁니다.

---

## 15. 알림·백그라운드 새로고침

`NotificationsEnabled`이고 로그인한 동안 `DispatcherTimer`가 `Dashboard.ReloadAsync(silent: true)`를 돌립니다. 이미 로딩 중이면 건너뜁니다.

기간이 지난 미작성이 있으면 `WindowsNotification.ShowMissingReports`가 트레이 풍선을 띄웁니다. 제목은 “작성하지 않은 보고서가 있습니다”, 본문은 `출장 N건 · 휴일근무 M건`입니다. 풍선 클릭은 메인 창을 엽니다. Toast XML 경로는 예비용입니다.

네트워크가 끊겼다가 다시 붙고 `KeepSessionOnReconnect`가 켜져 있으면 UI 스레드에서 재로그인한 뒤 대시보드를 다시 초기화합니다.

---

## 16. UI 톤

`App.xaml`에 남색(`#3E5270`)·종이색·안개색 브러시와 버튼·입력란 스타일이 있습니다. 근태 종류별 강조색은 `SchedulePalette`입니다 (출장 남색, 휴일근무 로즈, 연차 세이지 등). 변환기는 `BoolToVisibilityConverter`입니다.

로그인 화면은 그라데이션과 원형 장식 위의 카드입니다. 대시보드 카드는 왼쪽 색 막대와 큰 일수 숫자입니다. 설치기는 별도 팔레트(큰 액센트 버튼)를 씁니다.

---

## 17. 파일별 역할

| 파일 | 역할 |
| --- | --- |
| `Directory.Build.props` | 공통 버전 1.3.0 |
| `AppVersion.cs` | 하단·설치 버튼 문자열, `Current` |
| `App.xaml.cs` | 단일 인스턴스, TLS, 알림 초기화, 공유 Client, `--autostart` |
| `MainWindow.xaml.cs` | 탐색, 트레이, 자동 로그인, 사이드 메뉴, 설정, 업데이트 링크 |
| `Services/AmaranthClient.cs` | ERP HTTP, 서명, 파싱, 미작성 계산 |
| `Services/UpdateChecker.cs` | GitHub 버전 확인, 설치기 다운로드 |
| `Helpers/ApprovalStatus.cs` | 반려·진행·종결 판정 |
| `ViewModels/ViewModels.cs` | 로그인·대시보드 상태, 알림 트리거, 초안 Fill 구성 |
| `Helpers/AppRuntime.cs` | 설정 연동, 세션, 타이머, 재연결 로그인 |
| `Helpers/CredentialStore.cs` | DPAPI 로그인 파일 |
| `Helpers/SettingsStore.cs` | settings.json |
| `Helpers/ReportDraftFiller.cs` | WebView2 주입 스크립트 |
| `Helpers/WindowsNotification.cs` | AUMID, 바로가기, 풍선/토스트 |
| `Helpers/StartupRegistration.cs` | 시작 폴더 바로가기 |
| `Helpers/ScreenPlacement.cs` | 다중 모니터 DIP 좌표, 포커스 |
| `Views/ReportBrowserWindow.xaml.cs` | 쿠키 주입, 팝업, 초안 입력, 닫힌 뒤 새로고침 |
| `Views/ManualWindow.xaml.cs` | 로컬 HTML 매뉴얼 |
| `docs/사용매뉴얼.html` | 빌드 시 `ReadMe.html`로 복사 |
| `dist/아맛다보고서Install.exe` | GitHub에서 받는 설치기 사본 |
| `Installer/*` | 관리자 설치기, payload.zip 임베드 |

---

## 18. 실행·배포 시 알아 둘 점

- Windows 10 이상, x64, .NET Framework 4.8, **WebView2 Runtime**이 필요합니다.
- 배포: 본 프로그램을 Release 빌드한 뒤 `Installer`를 Release 빌드하면 `아맛다보고서Install.exe`와 `dist/` 사본이 나갑니다. 설치 시 관리자 권한이 필요합니다. 인앱 업데이트를 쓰려면 GitHub `main`에 `Directory.Build.props`와 `dist/아맛다보고서Install.exe`를 올려 두어야 합니다.
- 이미 실행 중이면 설치기가 프로세스를 닫은 다음 파일을 덮어씁니다.
- ERP 주소·그룹 시퀀스·양식 ID는 코드 상수입니다. 서버가 바뀌면 `AmaranthClient`를 고쳐야 합니다.
- ERP HTML 양식이 바뀌면 기간·휴일근무 파싱과 자동 입력이 깨질 수 있습니다.
- 이 앱은 조회·초안 입력까지입니다. 결재선·첨부·상신은 ERP WebView에서 사용자가 합니다.
- 창을 닫아도 프로세스는 트레이에 남습니다. 완전히 끄려면 종료 확인을 거쳐야 합니다.
- 두 번째 exe를 켜면 기존 창만 앞으로 나옵니다.

Visual Studio에서 `Properties/launchSettings.json`의 프로필 이름 **아맛다보고서**로 본 프로그램을 디버그할 수 있습니다.

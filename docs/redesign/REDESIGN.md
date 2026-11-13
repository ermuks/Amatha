# 아맛다보고서 v1.14.0 디자인 개편 기록

작성일: 2026-11-13 · 대상 버전: **1.14.0** (작업 도중 `Directory.Build.props`가 이미 1.14.0으로 올라가 있어 버전은 더 올리지 않았습니다. 이 개편을 별도 릴리스로 내려면 1.15.0으로 올려 주세요.)

이 문서는 이번 디자인 개편에서 **무엇을, 왜, 어떻게** 바꿨는지 정리한 기록입니다.
화면 시안은 `docs/redesign/png/`(이미지)와 `docs/redesign/mockups/`(브라우저로 여는 HTML)에 있습니다.

> ⚠️ 이 작업은 빌드·테스트를 돌릴 수 없는 환경(Linux, .NET Framework 4.8 WPF 빌드 불가)에서 진행했습니다.
> XAML은 XML 문법 검사까지만 통과했습니다. **Visual Studio에서 Release 빌드와 `Tests` 실행을 꼭 한 번 해 주세요.** (맨 아래 "확인 순서" 참고)

---

## 1. 방향

| 항목 | 이전 | 이후 |
|---|---|---|
| 분위기 | 네이비 + 차가운 회청색 | 따뜻한 회색 바탕 + 흰색 패널 + 보라 포인트 1색 |
| 폰트 | Segoe UI (한글은 맑은 고딕으로 대체됨) | **IBM Plex Sans KR** 임베드 (400/500/600/700) |
| 메뉴 | 40px 햄버거 → 펼치면 240px 오버레이 | **232px 고정 사이드바**, 현재 화면 표시, 건수 배지 |
| 창 크기 | 900×760 (최소 680×520) | **1200×780 (최소 980×620)** |
| 미작성 목록 | 카드 나열 (왼쪽 6px 색 막대) | 인사말 · 상태 배너 · 요약 4칸 · 섹션별 표 형태 목록 · 상태 칩 |
| 메일 | 기본 DataGrid | 정돈된 표, 안 읽음 점, 검색 + 필터(전체/안 읽음/첨부파일) |
| 메일 본문 | 정보 나열 | 보낸 사람 아바타, 구분선, 확장자 색 배지가 있는 첨부 카드 |
| 환경설정 | 체크박스 목록 + 닫기 버튼 | 카드 4개(알림/시작/세션/조회 범위) + 토글 스위치, **바로 저장** |

목표는 "미니멀한 사무용 UI인데 너무 딱딱하지는 않게"입니다. 장식은 줄이고, 바탕을 웜 그레이로 바꾸고, 인사말과 부드러운 문장으로 사무적인 느낌을 덜었습니다.

---

## 2. 디자인 토큰

모든 값은 `App.xaml`의 리소스로 들어 있습니다. 단위는 WPF DIP(=CSS px).

### 2.1 색

| 용도 | 리소스 키 | 값 |
|---|---|---|
| 창 바탕 / 사이드바 | `BgBrush` (`CanvasBrush`, `MistBrush`) | `#F1F0EC` |
| 콘텐츠 패널, 카드 | `SurfaceBrush` (`PaperBrush`) | `#FFFFFF` |
| 표 머리, 행 hover | `Surface2Brush` | `#FAFAF8` |
| 검색 상자 | `FieldBrush` | `#F5F4F0` |
| 사이드 메뉴 hover | `HoverBrush` | `#E8E7E2` |
| 기본 선 | `LineBrush` | `#E6E4DE` |
| 행 구분선 | `LineSoftBrush` | `#EFEDE8` |
| 버튼/입력 테두리 | `LineStrongBrush` | `#E0DED8` |
| 제목 글자 | `InkBrush` | `#1C1B19` |
| 본문 글자 | `Ink2Brush` | `#2E2D2A` |
| 메뉴 글자 | `Ink3Brush` | `#4A4843` |
| 보조 글자 | `SubBrush` (`NavyMutedBrush`) | `#5F5D57` |
| 캡션 글자 | `MutedInkBrush` | `#6E6B64` |
| 화살표 등 옅은 아이콘 | `FaintBrush` | `#9A978F` |
| 포인트 | `AccentFillBrush` (`NavyBrush`) | `#5847C9` |
| 포인트 hover | `AccentHoverBrush` | `#4A3AB5` |
| 포인트 옅은 바탕 (배너, 선택 행) | `AccentSoftBrush` | `#F2F0FB` |
| 포인트 타일 (출장 아이콘, 아바타) | `AccentTileBrush` | `#EEECFA` |
| 어두운 버튼 (메일 필터 선택) | `DarkBrush` | `#1C1B19` |

상태 색 (칩·타일·첨부 배지 공통)

| 의미 | 바탕 | 글자 |
|---|---|---|
| 예정 (파랑) | `#E8F0FE` | `#1E4FB8` |
| 진행 중 / 휴일근무 (주황) | `#FFF1E0` | `#9A4A00` |
| 작성 필요 / 오류 (빨강) | `#FDECEC` | `#B42318` |
| 완료 / 대체휴가 (초록) | `#E7F5EC` | `#17663A` |
| 기타 (회색) | `#EDEDEA` | `#4A4843` |

> 기존 키(`NavyBrush`, `InkBrush`, `LineBrush` …)는 지우지 않고 **새 색으로 다시 연결**했습니다. 손대지 않은 화면도 자연스럽게 새 색을 따라갑니다.

글자 대비: 모든 본문/캡션 색은 흰 바탕에서 4.5:1 이상입니다 (가장 옅은 `#6E6B64`가 약 5.2:1).

### 2.2 글꼴

- `AppFont` = `pack://application:,,,/Assets/Fonts/#IBM Plex Sans KR`
- 파일: `Assets/Fonts/IBMPlexSansKR-{Regular,Medium,SemiBold,Bold}.ttf` (SIL OFL 1.1, `Assets/Fonts/OFL.txt`) — csproj에 `<Resource Include="Assets\Fonts\*.ttf" />` 추가
- 창에 `TextOptions.TextFormattingMode="Display"`, `UseLayoutRounding="True"`를 켜서 작은 한글이 흐려지지 않게 했습니다.

| 쓰임 | 크기 / 굵기 |
|---|---|
| 화면 인사말 · 환경설정 제목 | 26 / Bold |
| 메일 제목(본문), 로그인 제목 | 22 / Bold |
| 메일 목록 제목 | 20 / Bold |
| 섹션 제목 | 17 / SemiBold |
| 요약 숫자 | 28 / SemiBold |
| 목록 항목 제목 | 14 / SemiBold |
| 기본 글자 | 14 / Regular |
| 버튼, 보조 글자 | 13 / Medium |
| 캡션, 메타 정보 | 12 / Regular |
| 배지 숫자 | 11 / SemiBold |

### 2.3 모양

- 모서리: 콘텐츠 패널 16 · 카드/목록/배너 12 · 버튼/입력 10 · 작은 버튼 8~9 · 칩/알약 끝까지 둥글게
- 간격: 패널 안쪽 40(좌우) × 32(위) · 섹션 사이 24~28 · 섹션 제목 아래 12
- 그림자: 선택된 사이드 메뉴(아주 옅게), 확인 팝업(Blur 40, Opacity 0.18), 트레이 메뉴, 로딩 카드만

### 2.4 아이콘

이모지/글자 아이콘(☰ 등) 대신 24×24 그리드의 선 아이콘을 `App.xaml`에 `StreamGeometry`로 넣었습니다 (`IconList`, `IconMail`, `IconBriefcase`, `IconCalendar`, `IconSun`, `IconAlert`, `IconTrash`, `IconCheck`, `IconPaperclip`, `IconRefresh` …, 전체 목록은 `png/11_icons.png`).

```xml
<Viewbox Width="18" Height="18">
    <Path Style="{StaticResource IconPath}" Data="{StaticResource IconMail}" Stroke="{StaticResource InkBrush}" />
</Viewbox>
```

버튼 스타일(`SideMenuButtonStyle`, `IconTextButtonStyle`, `GhostIconButtonStyle`)은 `Tag`에 아이콘 Geometry를 넣으면 같이 그려 줍니다.

---

## 3. 공통 스타일 (App.xaml)

| 키 | 설명 |
|---|---|
| `SideMenuButtonStyle` | 사이드 메뉴 버튼. 높이 40, 좌우 12, 아이콘 18 + 간격 10, 14/Medium `#4A4843`, hover `#E8E7E2` |
| `SideNavRadioStyle` | 위 스타일의 RadioButton 버전(이동 메뉴). 선택 시 흰 바탕 + 옅은 그림자 + SemiBold + 아이콘 보라 |
| `BadgeStyle` / `BadgeTextStyle` | 메뉴 오른쪽 건수 배지. 20×20 이상, 보라 바탕, 11/SemiBold 흰 글자 |
| `QuietButtonStyle` | **보조 버튼으로 바뀜**: 흰 바탕 + `#E0DED8` 테두리, 라운드 10, 13/Medium. (메일 테스트가 이 스타일만 따로 파싱하므로 다른 리소스를 참조하지 않게 유지) |
| `IconTextButtonStyle` | 아이콘 + 글자 보조 버튼 (새로고침) |
| `PrimaryButtonStyle` | 보라 버튼, 높이 44, 라운드 12, 15/SemiBold, hover `#4A3AB5` |
| `SoftButtonStyle` | 배너 안의 흰 버튼 (보라 글자) |
| `GhostIconButtonStyle` | 목록 행의 작은 버튼(삭제). hover 시 빨간 바탕/글자 |
| `CardButtonStyle` | 미작성 목록의 한 행 버튼. 흰 바탕, hover `#FAFAF8` |
| `FieldStyle` / `PasswordFieldStyle` | 입력 상자. 높이 44, 흰 바탕, 테두리 `#E0DED8`, 포커스 시 보라 테두리 + 3px 옅은 보라 링 |
| `SwitchStyle` | 환경설정 토글 스위치 (36×20, 켜짐 보라). `Content`가 왼쪽 라벨 |
| `BoxCheckStyle` | 16px 체크박스 (미작성 목록 "2시간 동안 알리지 않음", 업데이트 팝업) |
| `ChipStyle`, `CountPillStyle`, `SlimProgressStyle` | 상태 칩, 섹션 건수 알약, 4px 진행 막대 |

---

## 4. 화면별 변경

### 4.1 메인 창 · 사이드바 (`MainWindow.xaml`, `.cs`)

- 햄버거 메뉴와 펼침 애니메이션을 없애고 **232px 고정 사이드바**로 바꿨습니다. 안쪽 여백 좌18 · 위22 · 우14 · 아래16.
  - 위: 로고(32px 보라 사각형) + "아맛다보고서" 16/Bold
  - 이동 메뉴(RadioButton): **미작성 목록**(건수 배지), **메일**(안 읽은 메일 배지), 사용자 매뉴얼
  - 아래: 업데이트 확인, **환경설정**(RadioButton), 로그아웃, 종료하기, 그리고 업데이트 링크/버전
- 아래쪽 상태 바(버전·업데이트 링크)는 사이드바 아래로 옮겼습니다. 이름(`UpdateLink`, `VersionText`)은 그대로라 업데이트 로직은 바뀌지 않았습니다.
- 오른쪽 콘텐츠는 `ContentPanel`(흰색, 테두리 1, 라운드 16, 바깥 여백 위12·오른12·아래12)에 담습니다.
  로그인 화면에서는 `ApplyLoginChrome()`이 패널을 없애 창 전체를 바탕색으로, 로그인 후 `ApplyShellChrome()`이 패널을 씌웁니다.
- 환경설정은 오버레이가 아니라 **사이드바 메뉴 하나**가 됐습니다. 닫기 버튼 대신, 다른 메뉴로 이동할 때 `LeaveSettings()`가 입력 중인 값을 반영하고 새로고침 타이머를 다시 시작합니다(예전 닫기 버튼과 같은 처리).
- 새로 추가: `SetActiveNav()`, `UpdateDashboardBadge()`, `UpdateMailBadge()`, `OpenSettings()`(미작성 목록 배너의 "알림 설정" 버튼용).
- 지운 것: `Hamburger_Click`, `ExpandMenu`, `CollapseMenu`, `AnimatePanel`, `IsInside`, `Window_PreviewMouseDown/PreviewKeyDown/Deactivated`(메뉴 접기 전용이었음).

### 4.2 미작성 목록 (`Views/DashboardPage.xaml`, `.cs`, `ViewModels/ViewModels.cs`)

위에서부터:

1. **머리말** — 오늘 날짜(13 캡션) / "안녕하세요, {이름}님"(26 Bold) / "{부서} · 작성하지 않은 출장·휴일근무 보고서를 모아 보여드려요."(14). 오른쪽 아래에 "방금 전 동기화"(30초마다 갱신)와 새로고침 버튼.
2. **배너** (라운드 12, 안쪽 18×16) — 상황에 따라 문구와 색이 바뀝니다.
   - 기한 지난 보고서가 있음: 보라 배경 · 종 아이콘 · "작성할 보고서가 N건 있어요."
   - 대체휴가 요청서만 문제: "대체휴가 요청서를 확인해 주세요."
   - 항목은 있지만 기한 전: "기한이 지난 보고서는 없어요."
   - 비어 있음: 초록 배경 · 체크 아이콘 · "모든 보고서를 작성했어요."
3. **요약 4칸** — 작성 필요(기한 지난 출장+휴일근무, 0이 아니면 보라 숫자) / 출장 보고서 / 휴일근무 보고서 / 대체휴가 요청서.
4. **섹션 3개** — 제목 17 SemiBold + 건수 알약. 항목이 없으면 점선 상자에 초록 체크와 기존 안내 문구.
5. **목록 행** (흰 바탕, hover `#FAFAF8`, 행 사이 `#EFEDE8` 선)
   - 1줄: 유형 타일 36 | 이름 + 신청서 제목 | 기간 + 프로젝트 코드 | 상태 칩 | 일수 | ›
   - 2줄: 상태 설명(예: "출장 전 · 알림을 보내지 않습니다") ··· "2시간 동안 알리지 않음" 체크박스 · 휴지통 "삭제"
   - 예전 왼쪽 6px 색 막대(`AccentBrush`)는 화면에서 빠졌습니다. 속성은 남겨 뒀습니다.

뷰모델에 추가한 것 (기존 속성·동작은 그대로):

| 클래스 | 추가 | 내용 |
|---|---|---|
| `DashboardViewModel` | `TodayText`, `GreetingText`, `SubtitleText` | 세션의 `EmployeeName`, `DepartmentName` 사용 |
| | `LastSyncedText`, `RefreshRelativeTimes()` | 마지막 조회 시각 기준 "방금 전/N분 전/HH:mm 동기화" |
| | `OverdueReportCount`, `TotalCount` | 요약 칸 · 사이드바 배지 |
| | `BannerKind`, `BannerTitle`, `BannerMessage` | 배너 상태 |
| `MissingPeriodViewModel` | `TileKind` | `trip` · `holiday` · `substitute` · `error` → 타일 색/아이콘 |
| | `Stage`, `ChipText` | `overdue`(작성 필요, 빨강) · `ongoing`(진행 중, 주황) · `upcoming`(예정, 파랑) · `problem`(시간 오류/요청서 없음, 빨강) |
| | `DayCountVisibility` | 대체휴가 항목은 일수를 숨김 (`DayCountText`를 빈 값으로 바꾸고 상태는 칩으로 표시) |

### 4.3 메일 목록 (`Views/MailView.xaml`, `.cs`, `Models/MailModels.cs`)

- 바깥 여백 좌우 32 · 위 28. 제목 "메일" 20 Bold + "전체 N건 · 안 읽은 메일 N건", 오른쪽 새로고침.
- **검색 상자**(280×38, 회색 바탕, 돋보기, 안내 문구) + **필터 알약**(전체/안 읽음/첨부파일, 선택 시 검정). 현재 페이지(최대 100건) 안에서만 거릅니다(`ApplyFilter()`, 서버 조회 없음).
- 표: 테두리 1 · 라운드 12 · 머리 40px(`#FAFAF8`, 12/Medium) · 행 52px · hover `#FAFAF8` · 선택 `#F2F0FB`.
  - 열: 안 읽음 점(36) | 보낸 사람(160) | 제목(*) | 첨부 클립 아이콘(56) | 받은 시각(150, 12 캡션)
  - 안 읽은 메일은 점 + SemiBold. "상태/첨부" 글자 열은 점·아이콘으로 바뀌었습니다.
- 받은 시각은 `InboxMail.DisplayTime`으로 "오후 3:20 / 어제 / 11월 7일 / 2025.11.07"처럼 보여 줍니다. 해석할 수 없으면 원래 값을 그대로 씁니다(원래 값은 툴팁).
- 불러온 뒤 `UnreadCountChanged` 이벤트로 사이드바 "메일" 배지를 갱신합니다.

### 4.4 메일 본문 (`Views/MailBodyView.xaml`, `.cs`, `Helpers/MailBodyHtml.cs`)

- 위에서부터: "← 메일 목록" → 제목 22 Bold → 보낸 사람 줄(38px 원형 아바타에 첫 글자, 보낸 사람 14 SemiBold, 받는 사람 12 캡션, 오른쪽 날짜) → 본문(WebView2) → 첨부파일.
- "보낸 사람  " 접두어는 빼고 아바타로 대신했습니다(`Initial()` 추가).
- 불러오기 상태 줄(문구 + 다시 불러오기)은 **문구가 있을 때만** 보입니다.
- 첨부파일은 본문 아래로 옮겼고, 2열 카드(테두리 1, 라운드 12)로 바꿨습니다. 확장자 배지 색: PDF 빨강 / XLS·XLSX·CSV 초록 / HWP·HWPX·DOC·DOCX 파랑 / 그 밖 회색 (`MailAttachment.Extension`, `ExtensionKind` 추가). "전체 다운로드"는 "모두 다운로드"로 문구만 바꿨습니다.
- 일반 텍스트 메일의 본문 글꼴을 15px / 줄 간격 1.8 / `#2E2D2A`로 맞췄습니다 (HTML 메일은 원본 그대로).

### 4.5 로그인 (`Views/LoginPage.xaml`, `.cs`)

- 그라데이션과 원형 장식을 없애고 바탕색 위에 흰 카드(420폭, 안쪽 40, 라운드 16) 하나만 둡니다.
- 로고 → "다시 만나서 반가워요"(22 Bold) → 안내 문장 → 아이디/비밀번호(라벨 13 SemiBold, 입력 44) → 보라 로그인 버튼(44) → 상태 문구. 카드 아래에 버전.

### 4.7 메일 2단 화면 (1.15.0)

목록으로 돌아가지 않고 다른 메일을 바로 열 수 있도록 메일 화면을 **왼쪽 목록 + 오른쪽 본문**으로 바꿨습니다.

- 왼쪽 목록: 기본 380px, 가운데 구분선을 끌어 300~560px로 조절(`PaneSplitter`). 바깥 여백 좌24 · 위24 · 우14 · 아래16.
  - 검색 상자는 칸 폭에 맞게 늘어나고, 필터 알약은 그 아래 줄로 내려갔습니다.
  - 표 머리 행을 없애고 한 행을 두 줄로: 1줄 보낸 사람 ··· 받은 시각, 2줄 제목 ··· 클립 아이콘. 행 높이 64.
  - `MailGrid`는 **2열**(안 읽음 점 22 | 메일 *). 하단 안내 문구("메일을 누르면…")는 빼고 페이지 버튼만 오른쪽에 둠.
  - 선택 행 `#F2F0FB`, hover `#FAFAF8`. (전에는 DataGrid의 `RowBackground`가 행 스타일을 덮어 둘 다 보이지 않았음 → `RowBackground` 제거)
- 오른쪽 본문: 아무것도 고르지 않으면 `EmptyBodyPanel`("메일을 선택해 주세요") 안내.
  - 클릭·Enter는 바로 열고, ↑↓ 키로 선택이 바뀌면 180ms 멈춘 뒤 엽니다(빠르게 넘길 때 메일마다 서버를 부르지 않게).
  - **같은 `MailBodyView`·같은 WebView2를 다시 씁니다**(`ShowMail`). 이전 조회는 취소하고, 늦게 온 결과는 버림(`_loadVersion`). 새 본문이 뜰 때까지 이전 본문은 가림.
  - 위쪽 "← 메일 목록" 버튼은 제목 오른쪽 **"닫기"**로 바뀜(이름 `BackButton` 그대로). 누르면 본문을 닫고 안내로 돌아가며 목록 선택은 유지.
  - 첨부파일 카드는 본문 칸이 좁을 수 있어 한 줄에 하나씩.
  - **첨부파일을 받는 동안 목록을 잠급니다**(`DownloadingChanged`). 다른 메일로 넘어가 다운로드가 취소되는 일을 막기 위함.
- WebView2가 겹치는 화면을 가리는 문제: 환경설정·확인 팝업이 뜰 때 `MainWindow.UpdateContentVisibility()`가 `MailHost`를 숨기는 기존 처리로 해결됨(바뀐 것 없음).

### 4.8 토스트 알림 (1.15.0)

확인만 받던 메시지 상자(`MessageBox.Show`, 9곳)를 토스트 알림으로 바꿨습니다. 예/아니오를 묻는 확인 팝업(`ShowConfirm`)과 설치 프로그램의 메시지 상자는 그대로입니다.

- 쓰는 법: `Toast.Info/Success/Warning/Error(owner, 메시지, 제목)` (`Helpers/Toast.cs`). 새 코드에서도 `MessageBox` 대신 이것을 씁니다.
- 모양 (`Views/ToastWindow.xaml`): 어두운 카드 `#1C1B19`, 폭 380, 라운드 12, 그림자. 왼쪽 22px 원형 아이콘(안내 보라 `#5847C9` / 완료 초록 `#2E9E5B` / 주의 주황 `#D97706` / 오류 빨강 `#D9372B`), 제목 13 SemiBold 흰색 + 내용 13 `#D9D6CE`(최대 240px, 넘치면 …), 오른쪽 위 24px **x 버튼**, 아래 3px 남은 시간 막대(아이콘 색).
- 위치: 창 오른쪽 아래(간격 12). 여러 개면 새 알림이 아래, 이전 알림이 위로 쌓임. 창을 옮기거나 크기를 바꾸면 따라감(최대화 시 모니터 작업 영역 기준).
- **항상 맨 위**: 메인 창이 소유한 별도 창이라 메일 본문(WebView2)을 포함한 창 안 모든 화면 위에 보임. 눌러도 포커스를 빼앗지 않음(`WS_EX_NOACTIVATE`), Alt+Tab·작업 표시줄에 안 나옴. 메인 창이 숨어 있으면(트레이) 화면 오른쪽 아래에 `Topmost`로 띄움.
- **표시 시간**: 처음 6초. 공백을 뺀 글자가 40자를 넘으면 12자마다 1초씩 늘어나 **최대 20초**(`ToastWindow.DurationFor`). 마우스를 올려 두면 멈추고, 떼면 이어서 줄어듦.
- 동작: 나타날 때 220ms 동안 흐려짐→선명 + 아래에서 10px 올라옴. **x를 누르거나 시간이 다 되면 바로** 180ms 동안 흐려지며 8px 올라가고 창을 닫음.

### 4.9 앱 아이콘 (1.15.1)

`Assets/AppIcon.ico` 하나를 바꾸면 실행 파일·모든 창·트레이(`ExtractAssociatedIcon`)·설치 프로그램 아이콘이 함께 바뀝니다.

- 원본: `docs/redesign/icon/source.png` (512px, 투명 바탕 캐릭터)
- **작은 크기 16~48px** (제목 표시줄·작업 표시줄·트레이): 얼굴 클로즈업 — 원본에서 (86,-18)-(408,304) 영역. 아래쪽 팔·몸통 선(y≥308)은 빼서 가장자리에 점이 남지 않게 함
- **큰 크기 64~256px** (바탕 화면·탐색기·설치 프로그램): 캐릭터 전체
- 작을수록 검은 선을 조금 굵게(1024 기준 반경 16px=4 … 48px=0). 더 굵게 하면 두 눈이 붙어 보임
- 다시 만들기: `python docs/redesign/icon/make_icon.py docs/redesign/icon/source.png Assets/AppIcon.ico 미리보기.png` (크기별 결과: `icon/sizes-preview.png`)

### 4.10 사용자 매뉴얼 (1.16.0)

- **같은 창에서 보기**: 사이드바 "사용자 매뉴얼"이 이동 메뉴(`NavManual`, RadioButton)가 되고, 별도 `ManualWindow` 대신 `Views/ManualView`(WebView2)를 메인 창 `ManualHost`에 띄웁니다. 메일처럼 환경설정·확인 팝업이 뜨면 숨기고(`UpdateContentVisibility`), 다른 메뉴에 갔다 와도 읽던 위치를 유지, 로그아웃 시 해제. 매뉴얼 밖 링크는 기본 브라우저로. WebView2는 둥근 모서리로 잘리지 않아 위·아래 16px를 띄움.
- **매뉴얼 디자인**: `docs/사용매뉴얼.html`을 새 팔레트(흰 바탕·보라 `#5847C9`·IBM Plex Sans KR)로 바꾸고 목차 알약을 추가. 메일 2단 화면·토스트 알림·바로 저장되는 환경설정·사이드바 업데이트 안내에 맞게 글을 고침. 섹션: 1 로그인 · 2 미작성 목록 · 3 보고서 작성 · 4 메일 · 5 메뉴·환경설정 · 6 트레이·종료 · 7 알림 · 8 업데이트.
- **새 화면 그림**: `01-login`, `02-dashboard`, `03-application`(신청서 보기 창, 1.16.1), `04-mail`(옛 `04-menu` 삭제), `05-settings`, `06-tray`, `07-exit`. (1.16.1에서 ERP 보고서 작성 화면 `03-report`는 뺌 — 이 프로그램에는 보고서 작성 기능이 없음)
  - 찍는 법: `Tests\bin\Release\net48\Amaranth10API.Tests.exe --manual-shots <폴더>` — 실제 창을 띄우되 **자동 로그인을 끄고 ERP 연결을 가짜 응답으로 바꿔** 가상 데이터(김테이아 · 디지털트윈팀 등)만 씀. PrintWindow로 WebView2까지 찍고, 요소 위치를 `rects.txt`로 남김.
  - 넣는 법: `python docs/tools/update_manual_shots.py <폴더>` — PNG를 `docs/images/`로 복사하고, 매뉴얼의 `shot:이름` 표시 사이를 그림 + 빨간 번호 상자 + 설명 목록으로 다시 씀(번호 위치는 `rects.txt`에서 계산하므로 화면이 바뀌어도 따라감).
- 함께 고친 것: 로그인 카드의 안내 문장이 한 글자만 다음 줄로 넘어가던 문제(줄바꿈 위치 지정).

### 4.11 앱 안 로고 · 문구 (1.16.1)

- 사이드바(34px)와 로그인 카드(38px)의 보라 사각형 + 문서 아이콘을 **캐릭터 얼굴**(앱 아이콘 작은 크기와 같은 구도)로 바꿈. `Assets/Logo.png`(128px, 흰 바탕)를 테두리 `#E6E4DE`·라운드 9~10 타일의 `ImageBrush`로 채움. 클로즈업이 잘린 가장자리가 타일 모서리와 맞아 프로필 사진처럼 보임.
  - 경로는 XAML 기준 상대 경로(`Assets/Logo.png`, `../Assets/Logo.png`). `/`로 시작하면 실행 중인 exe 기준이 되어 테스트·화면 찍기에서 리소스를 못 찾음.
- **보고서 작성 기능은 없음**: 배너 "항목을 누르면 신청서를 열고 보고서 작성을 시작할 수 있어요" → "항목을 누르면 연결된 신청서를 볼 수 있어요." 매뉴얼 3장은 "신청서 확인"으로 바꾸고, 보고서는 ERP에서 직접 작성·상신한다고 안내.
- 매뉴얼 그림의 가상 사용자 이름: **김테이아** (`teia.kim@example.com`).

### 4.12 홈 · 사이드바 캐릭터 (1.17.0)

- **홈** (`Views/HomeView`, `HomeHost`, 사이드바 맨 위 `NavHome`): 로그인하면 처음 보이는 요약 화면. 다른 화면에서 볼 수 있는 것을 짧게만 보여 줌. 폭은 최대 720, 왼쪽 정렬, 아래는 앞으로 늘어날 카드를 위해 비워 둠.
  - 인사말(날짜 · "안녕하세요, {이름}님")
  - **미작성 보고서** 카드: 전체 건수(미작성 목록의 `DashboardViewModel`을 그대로 읽음, 처음 불러오기 전엔 "-") + "지금 작성해야 할 보고서 N건을 포함해요." 누르면 미작성 목록.
  - **내 메일** 카드: 머리에 "안 읽음 N" 알약, 최근 5건(안 읽음 점 · 제목 · 보낸 사람). 머리는 메일 화면, 줄은 메일 화면에서 그 메일을 바로 엶(`MailView.OpenMailFromOutside`, 목록을 불러오는 중이면 끝난 뒤 엶). 홈에 다시 오면 1분 지난 경우 새로 불러옴. 불러온 안 읽음 수로 사이드바 메일 배지를 로그인 직후부터 채움.
  - 미작성 목록(`DashboardPage`)은 알림·새로고침을 맡으므로 로그인 때 예전처럼 뒤에서 만들고, 화면만 홈을 먼저 보여 줌(`NavigateToDashboard` → `ShowHome`).
- **사이드바**: 맨 위 로고(캐릭터 타일 + "아맛다보고서")를 **홈** 메뉴로 바꿈. 배경에 캐릭터 전신(`Assets/SidebarCharacter.png`, 선만 있는 투명 PNG, 728px)을 **불투명도 0.06**으로 356px 크기로 깔고 왼쪽 아래(`Canvas.Left=4`, `Canvas.Bottom=-72`)에 둬 발끝은 창 아래로 잘림. 사이드바 `ClipToBounds`, 클릭은 통과(`IsHitTestVisible=False`).
- 로그인 카드의 로고 타일(`Assets/Logo.png`)은 그대로.
- 매뉴얼: "2 홈" 장 추가(그림 `02-home`), 뒤 장 번호를 하나씩 밀어 9장.

### 4.6 그 밖

- `ConfirmDialog.xaml`: 흰 카드 라운드 16, 부드러운 그림자, 버튼 높이 40 · 최소폭 88 · 간격 8. "조용히 업데이트하기"는 새 체크박스 스타일.
- `TrayMenuWindow.xaml`: 아이콘이 있는 메뉴(GUI 보이기 / 종료하기는 빨강), 라운드 12.
- `ReportBrowserWindow.xaml`, `ManualWindow.xaml`: 색과 글꼴만 새 팔레트로.
- `Directory.Build.props`: 손대지 않음 (작업 중 이미 1.14.0으로 바뀌어 있었음).

---

## 5. 기존 테스트와의 약속 (일부러 바꾸지 않은 것)

`Tests/`가 화면 요소 이름과 구조에 기대고 있어서 다음은 그대로 지켰습니다.

| 테스트 | 지킨 조건 |
|---|---|
| `DashboardLayout` | 페이지 안 체크박스는 항목당 1개, 문구 "2시간 동안 알리지 않음" · 삭제 버튼 `Content` = "삭제" |
| `CheckBoxRouting` | 체크박스 Click은 `NotificationSnooze_Click`이 막아 행 클릭으로 번지지 않음 |
| `MailTests.Layout` / `Smoke` | (1.15.0) `MailGrid`는 2열 `DataGrid`, 메일 열 폭 > 250, 목록(300~560) 오른쪽에 본문 칸, 선택 행 강조, 다운로드 중 목록 잠금 |
| `Smoke` | `MailView`, `MailBodyView`는 **`QuietButtonStyle` 외의 앱 리소스를 쓰지 않음** (그래서 두 파일은 색을 직접 적음) |
| `EmbeddedNavigation` | `MailHost`, `BodyHost`, `BackButton`, `EmailList_Click`, `ShowDashboard`, `Settings_Click`, `NavigateToLogin` 유지 |
| `MailBodyTests` | `AttachmentList`, `AttachmentPanel`, `DownloadAllButton`, `CancelDownloadButton`, `DownloadStatusText` 이름 유지 |
| `UpdateDialog` | 체크박스 1개가 "예" 버튼 왼쪽, `DialogCard` 폭 조정 유지 |
| `TrayWindow` | `_updateTimer`, `_forceClose`, `HideToTrayAfterFirstRender` 유지 |

---

## 6. 시안과 다르게 한 부분 / 남은 일

- ~~메일 2단 화면은 이번에 하지 않았습니다.~~ → **1.15.0에서 2단 화면으로 바꿨습니다.** 아래 "4.7 메일 2단 화면" 참고.
- 첨부파일별 "저장됨" 표시는 하지 않았습니다. 저장 결과는 아래 상태 문구로 알려 줍니다. (하려면 `MailAttachment`를 감싸는 알림 가능한 뷰모델이 필요합니다.)
- 사이드바 "메일" 배지는 **메일 화면을 한 번 연 뒤**부터 보입니다. 로그인 직후 바로 보이게 하려면 로그인 후 1건만 조회하는 호출을 추가해야 합니다(서버 요청이 하나 늘어남).
- `docs/images/`의 사용 매뉴얼 스크린샷은 옛 화면입니다. 새 빌드로 다시 찍어야 합니다.
- 화면 폭이 좁을 때(최소 980) 미작성 목록의 기간 문구가 길면 말줄임(…)이 됩니다. 툴팁은 신청서 제목에만 있습니다.

---

## 7. 확인 순서

1. Visual Studio에서 솔루션을 **Release로 빌드**합니다. (폰트가 Resource로 들어갔는지: 실행 후 글꼴이 IBM Plex Sans KR인지 확인)
2. `Tests` 프로젝트를 실행해 기존 테스트가 모두 PASS인지 봅니다. 렌더링 미리보기는 `test-data/dashboard-preview.png`, `mail-preview.png`, `update-dialog.png`로 나옵니다.
3. 직접 확인할 것:
   - 로그인 화면 → 로그인 → 사이드바·흰 패널이 나오는지
   - 미작성 목록: 배너 문구/색, 요약 숫자, 행 클릭 시 신청서 열림, 체크박스·삭제가 행 클릭을 일으키지 않는지
   - 메일: 검색·필터, 안 읽음 점, 본문 열기/뒤로, 첨부 다운로드·모두 다운로드·취소
   - 환경설정: 토글/숫자 입력이 저장되는지, 다른 메뉴로 갔다가 돌아와도 값이 유지되는지
   - 업데이트 팝업, 트레이 메뉴
4. 문제가 없으면 `.cursor/rules`대로 설치 프로젝트를 빌드하고 커밋·푸시합니다. (이번 작업에서는 **커밋·푸시하지 않았습니다**.)

---

## 8. 바뀐 파일 목록

| 파일 | 변경 |
|---|---|
| `App.xaml` | 색·글꼴·아이콘 토큰, 공통 스타일 전면 교체 (기존 키 유지) |
| `MainWindow.xaml` / `.cs` | 고정 사이드바, 콘텐츠 패널, 환경설정 화면, 배지·메뉴 상태 |
| `Views/DashboardPage.xaml` / `.cs` | 미작성 목록 전면 개편, 새로고침·알림 설정 버튼, 상대 시간 타이머 |
| `ViewModels/ViewModels.cs` | 머리말·배너·요약·타일·칩용 속성 추가 |
| `Views/MailView.xaml` / `.cs` | 표 스타일, 검색·필터, 안 읽은 메일 이벤트 |
| `Views/MailBodyView.xaml` / `.cs` | 머리말·아바타·첨부 카드 |
| `Models/MailModels.cs` | `DisplayTime`, `FormatReceivedAt`, `Extension`, `ExtensionKind` |
| `Helpers/MailBodyHtml.cs` | 텍스트 메일 글꼴/색 |
| `Views/LoginPage.xaml` / `.cs` | 로그인 카드 개편, 버전 표시 |
| `Views/ConfirmDialog.xaml`, `Views/TrayMenuWindow.xaml` | 새 스타일 |
| `Views/ReportBrowserWindow.xaml`, `Views/ManualWindow.xaml` | 색·글꼴만 |
| `Amaranth10API.csproj` | 폰트 Resource 추가 |
| `Assets/Fonts/*` (새 파일) | IBM Plex Sans KR 4종 + OFL 라이선스 |
| `docs/redesign/*` (새 파일) | 이 문서, 시안 PNG, 시안 HTML |

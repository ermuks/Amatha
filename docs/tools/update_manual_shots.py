"""사용 매뉴얼의 화면 그림과 빨간 번호 표시를 새로 채웁니다.

1) 화면 찍기 (테스트 프로젝트 Release 빌드 후, 실제 계정·ERP를 쓰지 않는 가상 데이터로 창을 띄워 찍음)
     Tests\\bin\\Release\\net48\\Amaranth10API.Tests.exe --manual-shots <폴더>
2) 이 스크립트 실행
     python docs/tools/update_manual_shots.py <폴더>
   → <폴더>의 PNG를 docs/images/ 로 복사하고, docs/사용매뉴얼.html 안
     <!-- shot:이름 --> ... <!-- /shot --> 사이를 그림 + 번호 표시 + 설명 목록으로 다시 씁니다.
   번호 위치는 <폴더>/rects.txt (찍을 때 기록한 요소 위치)에서 가져오므로 화면이 바뀌어도 맞게 따라갑니다.
"""
import html
import re
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MANUAL = ROOT / "docs" / "사용매뉴얼.html"
IMAGES = ROOT / "docs" / "images"

# 그림마다: (rects.txt의 요소 이름, 설명) — 순서대로 ①②③…
SPECS = {
    "01-login": {
        "alt": "로그인 화면",
        "marks": [("id", "<b>아이디</b> — ERP 아이디를 입력합니다."),
                  ("password", "<b>비밀번호</b> — ERP 비밀번호를 입력합니다."),
                  ("button", "<b>로그인</b>을 누릅니다. Enter 키로도 됩니다.")],
    },
    "02-home": {
        "alt": "홈",
        "marks": [("nav-home", "<b>홈</b> — 왼쪽 메뉴 맨 위. 언제든 이 요약 화면으로 돌아옵니다."),
                  ("report", "<b>미작성 보고서</b> — 작성하지 않은 보고서·요청서 수. 누르면 미작성 목록으로 갑니다."),
                  ("mail-header", "<b>내 메일</b> — 안 읽은 메일 수. 누르면 메일 화면으로 갑니다."),
                  ("mail-list", "<b>최근 메일</b> — 제목과 보낸 사람. 한 줄을 누르면 메일 화면에서 그 메일이 바로 열립니다.")],
    },
    "02-dashboard": {
        "alt": "미작성 목록",
        "marks": [("banner", "<b>안내 배너</b> — 지금 작성해야 할 보고서 수를 알려 줍니다. <b>알림 설정</b>으로 환경설정을 엽니다."),
                  ("row0", "<b>항목</b>을 누르면 연결된 신청서가 <b>신청서 보기</b> 창에 열립니다. 오른쪽 칩이 상태(작성 필요 · 진행 중 · 예정)입니다."),
                  ("snooze1", "<b>2시간 동안 알리지 않음</b> — 이 항목의 알림만 잠시 멈춥니다."),
                  ("delete0", "<b>삭제</b> — 목록과 알림에서 영구 제외합니다 (ERP 문서는 그대로)."),
                  ("refresh", "<b>새로고침</b> — ERP에서 다시 조회합니다.")],
    },
    "02-dashboard-bottom": {
        "alt": "미작성 목록 아래쪽 (교육 보고서)",
        "marks": [("education-row", "<b>교육 보고서</b> — 교육 종류(사외교육·영어교육 …)와 교육명, 교육 기간. 누르면 교육 신청서가 열립니다.")],
    },
    "03-application": {
        "alt": "신청서 보기 창",
        "marks": [("address", "<b>신청서 제목</b> — 어떤 신청서를 보고 있는지 표시합니다."),
                  ("content", "<b>신청서 원문</b> — ERP에 저장된 내용을 그대로 보여 줍니다. 보기 전용이라 여기서 고치거나 상신할 수는 없습니다.")],
    },
    "04-mail": {
        "alt": "메일 화면 (왼쪽 목록, 오른쪽 본문)",
        "marks": [("list", "<b>메일 목록</b> — 검색과 필터(전체 · 안 읽음 · 첨부파일), 보라색 점은 안 읽은 메일입니다."),
                  ("body", "<b>본문</b> — 고른 메일이 바로 여기 열립니다. ↑ ↓ 키로 다음 메일로 넘어갑니다."),
                  ("attachments", "<b>첨부파일</b> — 파일별 <b>다운로드</b> 또는 <b>모두 다운로드</b>."),
                  ("close", "<b>닫기</b> — 본문을 닫습니다. 목록 선택은 그대로 남습니다.")],
    },
    "05-settings": {
        "alt": "환경설정",
        "marks": [("card-알림", "<b>알림</b> — Windows 알림, 확인 간격(초), 알림이 오면 창 띄우기."),
                  ("card-시작", "<b>시작</b> — Windows 시작 시 자동 실행, 트레이로 시작."),
                  ("card-세션", "<b>세션</b> — 네트워크가 다시 연결돼도 로그인 유지, <b>자동 로그인</b>."),
                  ("card-조회 범위", "<b>조회 범위</b> — 신청서·보고서를 이번 달 기준 몇 달 전후까지 볼지 정합니다.")],
    },
    "06-tray": {
        "alt": "트레이 메뉴",
        "small": True,
        "marks": [("item-GUI_보이기", "<b>GUI 보이기</b> — 창을 다시 엽니다."),
                  ("item-종료하기", "<b>종료하기</b> — 프로그램을 끕니다.")],
    },
    "07-exit": {
        "alt": "종료 확인",
        "marks": [("yes", "<b>예</b>를 누르면 완전히 꺼집니다. <b>아니오</b>나 바깥을 누르면 취소됩니다.")],
    },
}

MARK = "#e03131"


def read_rects(folder: Path):
    sizes, rects = {}, {}
    for line in (folder / "rects.txt").read_text(encoding="utf-8-sig").splitlines():
        parts = line.split()
        if not parts:
            continue
        if parts[0] == "image":
            sizes[parts[1]] = (int(parts[2]), int(parts[3]))
        elif parts[0] == "rect" and parts[-1] != "missing":
            image, values = parts[1], parts[-4:]
            name = " ".join(parts[2:-4])
            rects[(image, name)] = tuple(float(v) for v in values)
    return sizes, rects


def figure(name: str, spec, size, rects) -> str:
    w, h = size
    pad = 4
    shapes = []
    legend = []
    for index, (key, text) in enumerate(spec["marks"], start=1):
        if (name, key) not in rects:
            raise SystemExit(f"{name}: rects.txt에 '{key}' 위치가 없습니다.")
        x, y, rw, rh = rects[(name, key)]
        x, y, rw, rh = x - pad, y - pad, rw + pad * 2, rh + pad * 2
        cx, cy = max(14, x), max(14, y)
        shapes.append(f'<rect x="{x:.0f}" y="{y:.0f}" width="{rw:.0f}" height="{rh:.0f}" rx="10" fill="none" stroke="{MARK}" stroke-width="3" />')
        shapes.append(f'<circle cx="{cx:.0f}" cy="{cy:.0f}" r="13" fill="{MARK}" stroke="#fff" stroke-width="2" />')
        shapes.append(f'<text x="{cx:.0f}" y="{cy + 5:.0f}" text-anchor="middle" fill="#fff" font-size="14" font-weight="700" '
                      f'font-family="IBM Plex Sans KR, Malgun Gothic, sans-serif">{index}</text>')
        legend.append(f'<li><span class="dot">{index}</span><span>{text}</span></li>')

    cls = "shot small" if spec.get("small") else "shot"
    svg = (f'<svg viewBox="0 0 {w} {h}" preserveAspectRatio="none" aria-hidden="true">'
           + "".join(shapes) + "</svg>")
    return (f'<figure><div class="{cls}"><img src="images/{name}.png" alt="{html.escape(spec["alt"])}" width="{w}" height="{h}" />'
            f"{svg}</div><ol class=\"legend\">{''.join(legend)}</ol></figure>")


def main():
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    folder = Path(sys.argv[1])
    sizes, rects = read_rects(folder)
    raw = MANUAL.read_bytes()
    newline = "\r\n" if b"\r\n" in raw else "\n"  # 원래 줄바꿈 유지 (read_text는 \r\n을 \n으로 바꿔 버림)
    text = raw.decode("utf-8").replace("\r\n", "\n")
    for name, spec in SPECS.items():
        shutil.copyfile(folder / f"{name}.png", IMAGES / f"{name}.png")
        block = figure(name, spec, sizes[name], rects)
        pattern = re.compile(r"<!-- shot:" + re.escape(name) + r" -->.*?<!-- /shot -->", re.S)
        if not pattern.search(text):
            raise SystemExit(f"매뉴얼에 '{name}' 표시가 없습니다.")
        text = pattern.sub(lambda _: f"<!-- shot:{name} -->{block}<!-- /shot -->", text)
        print("updated", name, sizes[name])
    MANUAL.write_text(text.replace("\r\n", "\n").replace("\n", newline), encoding="utf-8", newline="")


if __name__ == "__main__":
    main()

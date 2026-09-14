# Microsoft Store 제출용 스토어 목록 문안 / Store listing copy

Partner Center → 제품 → 스토어 등록 정보(Store listings)에 그대로 붙여넣는 문안입니다.
언어별 등록 정보가 나뉘므로 en-US 와 ko-KR 를 각각 추가하세요. 앱 UI 자체는 영어만 지원합니다.

---

## en-US

### Short description (최대 1,000자)
See how much of your AI coding plan is left, right on the Windows 11 widget board. AI Usage Widget shows the 5-hour and weekly quotas for Codex and Claude Code with reset times and progress bars, and refreshes itself while it is on screen.

### Description (최대 10,000자)
AI Usage Widget puts your AI coding subscription limits on the Windows 11 widget board, so you can check what is left before you start a long session instead of finding out mid-task.

Pin it with Win + W, or open the desktop window if you prefer a standalone view. Both show the same cards: the service name and plan, the quota the provider reports, when it resets in your local time, and a progress bar for each window.

**Supported services**
- Codex - 5-hour and weekly Codex limits (these are the Codex limits, not general ChatGPT chat limits)
- Claude Code - 5-hour and weekly limits, plus one extra line for a per-model weekly quota

Show or hide each service independently; hidden services stop being queried.

**Connecting**
There is nothing to connect. The app uses the sign-in the tools on your PC already have.
- Claude Code - reads the credential file the Claude Code CLI keeps at `%USERPROFILE%\.claude\.credentials.json`, and never writes to it. If the widget reports the sign-in as expired, open Claude Code once and the CLI refreshes it.
- Codex - asks the Codex CLI itself over its local app-server interface. The app finds `codex.exe` from a global install, the standalone installer, or the ChatGPT extension for VS Code, and uses the most recently updated one. If you have never signed in, run `codex login` once.

This app never asks for a password and has no sign-in screen of its own.

**Built for the widget board**
- Small, medium, and large widget sizes
- Follows the Windows light and dark theme automatically
- Refreshes on screen every 30 seconds; the desktop preview every 15 seconds
- Provider queries are rate-limited per service and back off on HTTP 429, so your account is not hammered
- Sample mode shows the layout with placeholder numbers and makes no network calls

**Privacy**
Nothing is sent to the developer. There is no analytics, no advertising, no tracking, and no server operated by this app. Requests go directly to each provider. The app stores no credential of its own: existing CLI credential files are read but never modified.

**Requirements**
Windows 11 22H2 or later, x64, and the Windows widget board. Each service you want to see needs its CLI installed and signed in: Claude Code for Claude, the Codex CLI for Codex. The app interface is English only.

**Note**
AI Usage Widget is an independent tool from Mossworm. It is not affiliated with, endorsed by, or sponsored by OpenAI or Anthropic. All product names are trademarks of their respective owners. Usage numbers are what each provider reports and may lag or change without notice.

### Product features (항목당 최대 200자, 최대 20개)
- Codex and Claude Code quotas on one widget
- 5-hour and weekly windows with reset times in your local time
- An extra per-model weekly line for Claude Code
- No sign-in step: it reuses the Claude Code and Codex CLI logins already on your PC
- Small, medium, and large widget sizes, plus a standalone desktop window
- Automatic Windows light and dark theme
- No analytics, no tracking, no data sent to the developer

### Search terms (항목당 최대 30자, 최대 7개)
- ai usage
- claude code usage
- codex usage
- token limit widget
- ai quota

### What's new in this version
Initial Microsoft Store release. Weekly per-model quota line on the Claude card, refreshed screenshots, and Store packaging.

### Screenshot captions
- Status view with every connected service and its remaining quota
- Settings view for connecting services and choosing what to show
- The widget pinned to the Windows 11 widget board in dark mode

---

## ko-KR

### 간단한 설명
Windows 11 위젯 보드에서 AI 코딩 플랜의 남은 한도를 바로 확인하세요. Codex, Claude Code의 5시간·주간 사용량과 리셋 시각을 진행 막대로 보여주고, 화면에 떠 있는 동안 스스로 갱신합니다.

### 설명
AI Usage Widget은 AI 코딩 구독의 남은 한도를 Windows 11 위젯 보드에 올려 둡니다. 작업 도중에 한도가 떨어진 것을 알게 되는 대신, 긴 작업을 시작하기 전에 미리 확인할 수 있습니다.

Win + W로 위젯을 고정하거나, 별도 창을 선호하면 데스크톱 앱을 실행하세요. 두 화면 모두 서비스 이름과 플랜, 제공자가 보고한 사용량, 현지 시간 기준 리셋 시각, 창별 진행 막대를 같은 형식으로 보여줍니다.

**지원 서비스**
- Codex - 5시간·주간 Codex 한도 (일반 ChatGPT 대화 한도가 아니라 Codex 한도입니다)
- Claude Code - 5시간·주간 한도에 모델별 주간 한도 한 줄 추가

서비스별로 표시를 켜고 끌 수 있으며, 끈 서비스는 조회도 하지 않습니다.

**연결 방법**
따로 연결할 것이 없습니다. PC에 이미 설치된 도구의 로그인을 그대로 씁니다.
- Claude Code - Claude Code CLI가 관리하는 `%USERPROFILE%\.claude\.credentials.json`을 읽기만 하며, 수정하지 않습니다. 로그인이 만료되었다고 표시되면 Claude Code를 한 번 실행하면 CLI가 갱신합니다.
- Codex - Codex CLI의 로컬 app-server 인터페이스로 직접 물어봅니다. 전역 설치본, 단독 설치본, VS Code용 ChatGPT 확장에서 `codex.exe`를 찾아 가장 최근에 갱신된 것을 사용합니다. 로그인한 적이 없다면 `codex login`을 한 번 실행하세요.

이 앱은 비밀번호를 요구하지 않으며 자체 로그인 화면도 없습니다.

**위젯 보드에 맞춘 설계**
- 작음·보통·큼 위젯 크기 지원
- Windows 라이트·다크 테마 자동 적용
- 위젯은 30초, 데스크톱 미리보기는 15초마다 화면 갱신
- 서비스별 조회 간격 제한과 HTTP 429 백오프로 계정에 과도한 요청을 보내지 않음
- 샘플 모드는 네트워크 조회 없이 예시 수치로 레이아웃만 보여줌

**개인정보**
개발자에게 전송되는 데이터가 없습니다. 분석 도구, 광고, 추적, 자체 서버가 없으며 요청은 각 제공자에게 직접 전달됩니다. 앱이 자체적으로 저장하는 자격 증명은 없으며, 기존 CLI 자격 증명 파일은 읽기만 하고 수정하지 않습니다.

**요구 사항**
Windows 11 22H2 이상, x64, Windows 위젯 보드. 보려는 서비스마다 해당 CLI가 설치·로그인되어 있어야 합니다(Claude는 Claude Code, Codex는 Codex CLI). 앱 화면은 영어만 지원합니다.

**안내**
AI Usage Widget은 Mossworm이 만든 독립 도구이며 OpenAI, Anthropic과 제휴하거나 후원받지 않았습니다. 모든 제품명은 각 소유자의 상표입니다. 표시되는 수치는 각 제공자가 보고한 값이며 지연되거나 예고 없이 바뀔 수 있습니다.

### 제품 기능
- Codex, Claude Code 한도를 위젯 하나에서 확인
- 5시간·주간 창과 현지 시간 리셋 시각 표시
- Claude Code는 모델별 주간 한도 한 줄 추가
- 별도 로그인 단계 없음: PC의 Claude Code·Codex CLI 로그인을 그대로 사용
- 작음·보통·큼 위젯 크기와 별도 데스크톱 창
- Windows 라이트·다크 테마 자동 전환
- 분석·추적 없음, 개발자에게 전송되는 데이터 없음

### 검색어
- ai 사용량
- 클로드 코드
- 코덱스 사용량
- 위젯
- ai 한도

### 이 버전의 새로운 기능
Microsoft Store 첫 배포. Claude 카드에 모델별 주간 한도 줄 추가, 스크린샷 갱신, 스토어 패키징.

### 스크린샷 설명
- 연결된 모든 서비스와 남은 한도를 보여주는 Status 화면
- 서비스 연결과 표시 항목을 고르는 Setting 화면
- 다크 모드 Windows 11 위젯 보드에 고정한 모습

# Microsoft Store 제출용 스토어 목록 문안 / Store listing copy

Partner Center → 제품 → 스토어 등록 정보(Store listings)에 그대로 붙여넣는 문안입니다.
언어별 등록 정보가 나뉘므로 en-US 와 ko-KR 를 각각 추가하세요. 앱 UI 자체는 영어만 지원합니다.

---

## en-US

### Short description (최대 1,000자)
See how much of your AI coding plan is left, right on the Windows 11 widget board. AI Usage Widget shows the 5-hour and weekly quotas for Codex, Claude Code, OpenCode, and Command Code with reset times and progress bars, and refreshes itself while it is on screen.

### Description (최대 10,000자)
AI Usage Widget puts your AI coding subscription limits on the Windows 11 widget board, so you can check what is left before you start a long session instead of finding out mid-task.

Pin it with Win + W, or open the desktop window if you prefer a standalone view. Both show the same cards: the service name and plan, the quota the provider reports, when it resets in your local time, and a progress bar for each window.

**Supported services**
- Codex - 5-hour and weekly Codex limits (these are the Codex limits, not general ChatGPT chat limits)
- Claude Code - 5-hour and weekly limits, plus one extra line for a per-model weekly quota
- OpenCode - rolling 5-hour and weekly usage for the Go subscription
- Command Code - 5-hour and weekly usage with the plan name normalized

Show or hide each service independently; hidden services stop being queried.

**Connecting**
- Claude Code needs no CLI install. The app opens the official Claude sign-in page in your browser, you paste back the code it shows, and you are connected. Tokens are refreshed automatically afterward.
- Codex connects automatically if the Codex CLI is installed and already signed in. Otherwise use the connect button in Settings, which opens the official sign-in page in your browser.
- OpenCode and Command Code use the API key their own CLI already stored, or an environment variable.

You always type your password on the provider's own web page, never in this app.

**Built for the widget board**
- Small, medium, and large widget sizes
- Follows the Windows light and dark theme automatically
- Refreshes on screen every 30 seconds; the desktop preview every 15 seconds
- Provider queries are rate-limited per service and back off on HTTP 429, so your account is not hammered
- Sample mode shows the layout with placeholder numbers and makes no network calls

**Privacy**
Nothing is sent to the developer. There is no analytics, no advertising, no tracking, and no server operated by this app. Requests go directly to the provider you connected. Claude tokens are stored on your PC encrypted with Windows DPAPI, and existing CLI credential files are read but never modified.

**Requirements**
Windows 11 22H2 or later, x64, and the Windows widget board. The app interface is English only.

**Note**
AI Usage Widget is an independent tool from Mossworm. It is not affiliated with, endorsed by, or sponsored by OpenAI, Anthropic, OpenCode, or Command Code. All product names are trademarks of their respective owners. Usage numbers are what each provider reports and may lag or change without notice.

### Product features (항목당 최대 200자, 최대 20개)
- Codex, Claude Code, OpenCode, and Command Code quotas on one widget
- 5-hour and weekly windows with reset times in your local time
- An extra per-model weekly line for Claude Code
- Sign in to Claude Code without installing any CLI
- Small, medium, and large widget sizes, plus a standalone desktop window
- Automatic Windows light and dark theme
- No analytics, no tracking, no data sent to the developer

### Search terms (항목당 최대 30자, 최대 7개)
- ai usage
- claude code usage
- codex usage
- token limit widget
- ai quota
- opencode
- command code

### What's new in this version
Initial Microsoft Store release. Weekly per-model quota line on the Claude card, refreshed screenshots, and Store packaging.

### Screenshot captions
- Status view with every connected service and its remaining quota
- Settings view for connecting services and choosing what to show
- The widget pinned to the Windows 11 widget board in dark mode

---

## ko-KR

### 간단한 설명
Windows 11 위젯 보드에서 AI 코딩 플랜의 남은 한도를 바로 확인하세요. Codex, Claude Code, OpenCode, Command Code의 5시간·주간 사용량과 리셋 시각을 진행 막대로 보여주고, 화면에 떠 있는 동안 스스로 갱신합니다.

### 설명
AI Usage Widget은 AI 코딩 구독의 남은 한도를 Windows 11 위젯 보드에 올려 둡니다. 작업 도중에 한도가 떨어진 것을 알게 되는 대신, 긴 작업을 시작하기 전에 미리 확인할 수 있습니다.

Win + W로 위젯을 고정하거나, 별도 창을 선호하면 데스크톱 앱을 실행하세요. 두 화면 모두 서비스 이름과 플랜, 제공자가 보고한 사용량, 현지 시간 기준 리셋 시각, 창별 진행 막대를 같은 형식으로 보여줍니다.

**지원 서비스**
- Codex - 5시간·주간 Codex 한도 (일반 ChatGPT 대화 한도가 아니라 Codex 한도입니다)
- Claude Code - 5시간·주간 한도에 모델별 주간 한도 한 줄 추가
- OpenCode - Go 구독의 rolling 5시간·주간 사용량
- Command Code - 5시간·주간 사용량과 정규화된 플랜 표기

서비스별로 표시를 켜고 끌 수 있으며, 끈 서비스는 조회도 하지 않습니다.

**연결 방법**
- Claude Code는 CLI 설치가 필요 없습니다. 앱이 공식 Claude 로그인 페이지를 브라우저로 열고, 표시된 코드를 붙여넣으면 연결이 끝납니다. 이후 토큰은 자동으로 갱신됩니다.
- Codex는 Codex CLI가 설치되어 있고 로그인되어 있으면 자동으로 연결됩니다. 아니면 Setting의 연결 버튼으로 공식 로그인 페이지를 열어 로그인하세요.
- OpenCode와 Command Code는 각 CLI가 이미 저장해 둔 API 키 또는 환경 변수를 사용합니다.

비밀번호는 항상 각 제공자의 웹 페이지에서 직접 입력하며, 이 앱에는 입력하지 않습니다.

**위젯 보드에 맞춘 설계**
- 작음·보통·큼 위젯 크기 지원
- Windows 라이트·다크 테마 자동 적용
- 위젯은 30초, 데스크톱 미리보기는 15초마다 화면 갱신
- 서비스별 조회 간격 제한과 HTTP 429 백오프로 계정에 과도한 요청을 보내지 않음
- 샘플 모드는 네트워크 조회 없이 예시 수치로 레이아웃만 보여줌

**개인정보**
개발자에게 전송되는 데이터가 없습니다. 분석 도구, 광고, 추적, 자체 서버가 없으며 요청은 이용자가 연결한 제공자에게 직접 전달됩니다. Claude 토큰은 Windows DPAPI로 암호화해 이 PC에만 저장하고, 기존 CLI 자격 증명 파일은 읽기만 하고 수정하지 않습니다.

**요구 사항**
Windows 11 22H2 이상, x64, Windows 위젯 보드. 앱 화면은 영어만 지원합니다.

**안내**
AI Usage Widget은 Mossworm이 만든 독립 도구이며 OpenAI, Anthropic, OpenCode, Command Code와 제휴하거나 후원받지 않았습니다. 모든 제품명은 각 소유자의 상표입니다. 표시되는 수치는 각 제공자가 보고한 값이며 지연되거나 예고 없이 바뀔 수 있습니다.

### 제품 기능
- Codex, Claude Code, OpenCode, Command Code 한도를 위젯 하나에서 확인
- 5시간·주간 창과 현지 시간 리셋 시각 표시
- Claude Code는 모델별 주간 한도 한 줄 추가
- CLI 설치 없이 Claude Code 로그인
- 작음·보통·큼 위젯 크기와 별도 데스크톱 창
- Windows 라이트·다크 테마 자동 전환
- 분석·추적 없음, 개발자에게 전송되는 데이터 없음

### 검색어
- ai 사용량
- 클로드 코드
- 코덱스 사용량
- 위젯
- ai 한도
- opencode
- command code

### 이 버전의 새로운 기능
Microsoft Store 첫 배포. Claude 카드에 모델별 주간 한도 줄 추가, 스크린샷 갱신, 스토어 패키징.

### 스크린샷 설명
- 연결된 모든 서비스와 남은 한도를 보여주는 Status 화면
- 서비스 연결과 표시 항목을 고르는 Setting 화면
- 다크 모드 Windows 11 위젯 보드에 고정한 모습

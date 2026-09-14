# AI Usage Widget 개인정보 처리방침 / Privacy Policy

최종 수정일 / Last updated: 2026-09-14
게시자 / Publisher: Mossworm

---

## 한국어

### 1. 요약
AI Usage Widget(이하 "앱")은 이용자의 개인정보를 **수집·전송·판매하지 않습니다.** 개발자는 이용자의 데이터에 접근할 수 없습니다. 앱에는 분석 도구, 광고, 추적 기술, 원격 로깅, 자체 서버가 없습니다.

### 2. 앱이 수집하는 정보
없습니다. 앱은 개발자 또는 제3자에게 어떠한 개인정보도 전송하지 않습니다.

### 3. 이용자 기기에만 저장되는 정보
다음 항목은 이용자의 Windows PC에만 저장되며 외부로 전송되지 않습니다.

- **Claude 인증 토큰**: 앱에서 Claude에 로그인한 경우, 발급된 액세스/갱신 토큰을 `%LOCALAPPDATA%\AiUsageWidget\claude-auth.dat`에 Windows DPAPI로 암호화해 저장합니다. 해당 파일은 현재 Windows 사용자 계정에서만 복호화할 수 있습니다.
- **위젯 표시 설정**: 표시할 서비스 목록 등 설정은 Windows 위젯 호스트의 CustomState에 저장됩니다. 데스크톱 미리보기 설정은 별도의 로컬 설정으로 저장됩니다.
- **사용량 수치**: 제공자로부터 조회한 사용률·리셋 시간은 메모리에만 보관하며 앱이 디스크에 기록하지 않습니다. 다만 이용자가 별도의 수집 도구로 `%LOCALAPPDATA%\AiUsageWidget\usage.json` 스냅샷을 만들어 둔 경우 앱은 그 파일을 읽습니다.

앱은 비밀번호를 요구하거나 저장하지 않습니다. 로그인은 각 제공자의 공식 웹 페이지(브라우저)에서 이루어집니다.

### 4. 읽기 전용으로 접근하는 기존 자격 증명 파일
이미 설치된 CLI에 로그인되어 있는 경우, 앱은 사용량 조회에 필요한 범위에서 아래 파일 및 로컬 프로세스를 **읽기만** 합니다. 수정·삭제하지 않으며, 그 내용을 개발자나 제3자에게 전송하지 않습니다.

- Codex: 로컬 `codex app-server` 프로세스(계정 및 사용 한도 조회 요청만 수행)
- Claude Code CLI: `%USERPROFILE%\.claude\.credentials.json` (`CLAUDE_CONFIG_DIR` 지원)
- OpenCode: `opencode\auth.json`
- Command Code: 해당 CLI가 저장한 자격 증명

### 5. 네트워크 통신
앱은 이용자가 연결한 서비스의 공식 엔드포인트에만 직접 연결합니다. 중계 서버는 사용하지 않습니다.

- `api.anthropic.com`, `claude.ai`, `platform.claude.com`, `console.anthropic.com` (Anthropic)
- `opencode.ai`
- `api.commandcode.ai`, `commandcode.ai`
- Codex 사용량은 로컬 Codex 프로세스를 통해 OpenAI 서비스에서 조회됩니다.

이 통신에는 해당 서비스 제공자의 개인정보 처리방침 및 이용약관이 적용됩니다. 앱은 프롬프트 실행이나 모델 호출을 하지 않으며, 계정 사용량 정보만 조회합니다.

### 6. 데이터 삭제
- 앱 내 Setting에서 연결을 해제하거나, `%LOCALAPPDATA%\AiUsageWidget` 폴더를 삭제하면 저장된 토큰이 제거됩니다.
- 앱을 제거하면 앱이 저장한 로컬 데이터가 함께 제거됩니다.
- 각 서비스 계정에 저장된 정보의 삭제는 해당 제공자에게 요청해야 합니다.

### 7. 아동의 개인정보
앱은 아동을 대상으로 하지 않으며 아동으로부터 어떠한 정보도 수집하지 않습니다.

### 8. 방침 변경
방침이 변경되면 이 문서를 갱신하고 상단의 최종 수정일을 변경합니다.

### 9. 문의
mossworm.main@gmail.com

---

## English

### 1. Summary
AI Usage Widget (the "app") **does not collect, transmit, or sell any personal information.** The developer has no access to user data. The app contains no analytics, advertising, tracking, remote logging, or developer-operated servers.

### 2. Information the app collects
None. The app sends no personal information to the developer or to any third party.

### 3. Information stored only on your device
The following stays on your Windows PC and is never transmitted anywhere by the app:

- **Claude authentication tokens.** If you sign in to Claude from the app, the issued access and refresh tokens are stored at `%LOCALAPPDATA%\AiUsageWidget\claude-auth.dat`, encrypted with Windows DPAPI so that only your Windows user account can decrypt them.
- **Widget display settings.** Which services to show and related preferences are stored in the Windows widget host's CustomState; the desktop preview keeps its own local settings.
- **Usage figures.** Usage percentages and reset times fetched from providers are kept in memory only; the app does not write them to disk. If you separately run a collector that writes a `%LOCALAPPDATA%\AiUsageWidget\usage.json` snapshot, the app reads that file.

The app never asks for or stores passwords. Sign-in happens on each provider's own web page in your browser.

### 4. Existing credential files accessed read-only
If you already have the relevant CLIs installed and signed in, the app **reads only** the following, solely to query your usage. It never modifies or deletes them, and never sends their contents to the developer or anyone else:

- Codex: the local `codex app-server` process (account and rate-limit requests only)
- Claude Code CLI: `%USERPROFILE%\.claude\.credentials.json` (honors `CLAUDE_CONFIG_DIR`)
- OpenCode: `opencode\auth.json`
- Command Code: credentials stored by that CLI

### 5. Network connections
The app connects directly to the official endpoints of the services you choose to connect. No relay or intermediary server is used.

- `api.anthropic.com`, `claude.ai`, `platform.claude.com`, `console.anthropic.com` (Anthropic)
- `opencode.ai`
- `api.commandcode.ai`, `commandcode.ai`
- Codex usage is retrieved from OpenAI's service through the local Codex process.

These connections are governed by the respective provider's privacy policy and terms. The app does not run prompts or invoke models; it only reads account usage information.

### 6. Deleting your data
- Disconnect a service in the app's Settings, or delete the `%LOCALAPPDATA%\AiUsageWidget` folder, to remove stored tokens.
- Uninstalling the app removes the local data it stored.
- To delete data held in your provider accounts, contact that provider.

### 7. Children's privacy
The app is not directed at children and collects no information from them.

### 8. Changes to this policy
If this policy changes, this document is updated and the "Last updated" date above is revised.

### 9. Contact
mossworm.main@gmail.com

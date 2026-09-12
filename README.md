# Agent Usage Widget

Windows 11 위젯 패널용 C# 위젯과 별도 데스크톱 미리보기입니다. 참고 이미지의 서비스 순서와 Status / Setting 두 화면을 구현했습니다. Logs와 순서 변경 기능은 없습니다. Codex·Claude·Gemini 로그인은 Setting에서 브라우저로 연결합니다.

- Codex (ChatGPT), ChatGPT API, Claude, Claude API, Gemini CLI, Gemini API 표시 토글
- Codex·Claude: 이름·플랜, 5시간 사용률·리셋 시간, 주간 사용률·리셋 시간, 진행 막대
- Gemini CLI: 이름·플랜, 모델별 사용률·리셋 시간, 진행 막대
- API: 이번 달·오늘 비용(USD), 미확인 값은 `—`
- Windows 라이트·다크 테마 자동 적용
- 위젯 설정은 Windows 위젯 호스트의 CustomState에 저장. 데스크톱 미리보기 설정은 별도로 저장
- 위젯은 보이는 동안 30초마다 화면을 갱신하고, 미리보기는 15초마다 갱신. 자동 서버 조회 간격은 인스턴스당 Codex 2분, Claude·Gemini 5분. HTTP 429는 Retry-After에 따라 최소 5분 동안 재시도를 제한

**Codex·Claude·Gemini CLI의 로그인 기반 사용량 조회를 지원합니다.** Codex는 실제 계정으로 조회 검증했고, Claude·Gemini는 사용자 요청에 따라 로그인하지 않은 상태로 기능을 준비했습니다. API 비용 수집은 아직 미연결입니다. 기존 `chatgpt` 서비스 ID를 유지하면서 표시 이름은 `Codex (ChatGPT)`로 바꿨습니다. 이 값은 일반 ChatGPT 대화 한도가 아니라 Codex 한도입니다.

샘플 실행은 네트워크 조회·로그인·설정 저장을 하지 않으며 화면 하단에 샘플임을 표시합니다. 샘플 플랜과 수치는 레이아웃 확인용입니다.

## Codex 연결

Codex CLI가 이미 설치되고 ChatGPT 계정으로 로그인되어 있으면 앱 실행 시 자동 연결됩니다. 이 PC에서 실제 조회를 검증했습니다.

처음 연결하거나 인증이 만료된 경우 데스크톱 앱의 **Setting → Codex 로그인 / 다시 연결**을 누르고 열린 브라우저에서 로그인하세요. 위젯 패널의 같은 버튼은 데스크톱 앱으로 연결됩니다. 비밀번호는 브라우저의 OpenAI 로그인 화면에서 직접 입력합니다. 상단 탭은 기존 Status / Setting 두 개 그대로입니다.

CLI가 없다면 설치하고 로그인하세요.

```powershell
npm install -g @openai/codex
codex login
```

앱은 PATH의 `codex.exe` 또는 npm 설치 경로의 네이티브 실행 파일을 찾습니다. 다른 위치라면 `CODEX_BIN`에 `codex.exe`의 절대 경로를 지정한 뒤 앱을 다시 실행하세요. `CODEX_HOME`은 Codex가 기존 환경 설정대로 사용합니다. API 키 로그인은 이 구독 한도 조회용으로 사용하지 않습니다.

구현은 로컬 `codex app-server --listen stdio://`에 `initialize` → `initialized` → `account/read` → `account/rateLimits/read` 순서로 요청합니다. 프롬프트 실행이나 모델 호출은 하지 않습니다. 로그인은 공식 `codex login`이 담당합니다. 위젯은 `auth.json`, 비밀번호, 액세스 토큰, 갱신 토큰을 읽거나 별도로 저장하지 않으며, 이메일·계정 ID도 저장하지 않습니다. 사용량은 메모리에만 보관합니다.

서버의 사용 비율(`usedPercent`)을 남은 비율로 변환해 표시합니다. 서버의 `windowDurationMins`가 300인 창을 5시간, 10080인 창을 주간으로 매핑합니다. 주간 한도만 있는 계정은 5시간을 `—`로 표시하며, 다른 모델/코드 리뷰 한도를 섞지 않습니다. 조회 실패 시 이전 계정의 수치를 남기지 않고 연결 상태를 표시한 후 2분 간격으로 재시도합니다. 자동 조회는 브라우저를 열지 않습니다.

## 실행

### Claude / Gemini 연결

이 PC에는 공식 Claude Code CLI 2.1.269와 Gemini CLI 0.59.0을 설치했습니다. **지금은 로그인하지 않아도 됩니다.** 나중에 앱의 Setting에서 `Claude 로그인 / 다시 연결` 또는 `Gemini 로그인 / 다시 연결`을 눌러 브라우저 인증을 완료하세요. 토글을 켜면 해당 로그인 정보로 자동 조회합니다.

다른 PC에서 필요한 CLI를 설치하려면:

```powershell
npm install -g @anthropic-ai/claude-code@2.1.269 @google/gemini-cli@0.59.0
```

Claude 로그인은 `claude auth login --claudeai`, Gemini 로그인은 공식 CLI ACP의 `initialize` → `authenticate` (`oauth-personal`)를 사용합니다. Gemini 프롬프트나 에이전트 세션을 생성하지 않습니다. 로그인 창을 닫거나 5분 동안 완료하지 않으면 재연결할 수 있습니다.

Claude 조회는 `%USERPROFILE%/.claude/.credentials.json`의 구독 OAuth 정보를 읽어 `https://api.anthropic.com/api/oauth/usage`에 GET 요청합니다. `CLAUDE_CONFIG_DIR`을 지원합니다. 전체 `five_hour` / `seven_day`만 사용하며 Sonnet·Opus별 한도를 전체 주간 한도로 혼동하지 않습니다. CLI 인증 파일을 수정하지 않습니다. CLI가 토큰을 갱신하면 다음 조회에서 읽으며, 만료된 경우 Setting에서 재연결 안내를 표시합니다.

Gemini 조회는 `%USERPROFILE%/.gemini/oauth_creds.json`을 읽어 Google Code Assist의 `loadCodeAssist` → `retrieveUserQuota`를 호출합니다. `GEMINI_CLI_HOME`, `GOOGLE_CLOUD_PROJECT`, `GOOGLE_CLOUD_PROJECT_ID`를 지원합니다. 만료된 액세스 토큰은 Google OAuth로 갱신해 메모리에서만 사용하며 CLI 인증 파일을 덮어쓰지 않습니다. CLI의 별도 암호화 인증 저장 모드·서비스 계정·API 키는 이 연결에서 지원하지 않습니다.

Gemini의 `remainingFraction`을 사용 비율로 변환하고, 화면의 두 줄에는 Pro 및 Flash 계열별로 가장 많이 사용한 모델 하나씩을 실제 모델 ID와 함께 표시합니다. 해당 계열이 없으면 다른 모델로 채웁니다. 서로 다른 모델의 비율을 더하지 않으며, 5시간·주간 한도로 임의 변환하지 않습니다. Gemini 웹 채팅·Google AI Studio API 비용과도 구분됩니다.

Claude·Gemini의 OAuth 사용량 경로는 공개 결제 API가 아닌 CLI 서비스 경로라 변경될 수 있습니다. 조회 실패·로그인 만료·429는 서비스별 상태로 표시하며 다른 서비스 갱신을 막지 않습니다. 위젯은 비밀번호 입력을 받지 않고, 읽은 토큰·계정 식별자를 사용량 데이터나 로그에 기록하지 않습니다.

참고: [Claude 공식 인증·저장 위치](https://code.claude.com/docs/en/authentication), [Claude 저장소의 OAuth 사용량 응답 보고](https://github.com/anthropics/claude-code/issues/31021), [Google 공식 Code Assist 구현](https://github.com/google-gemini/gemini-cli/blob/main/packages/core/src/code_assist/server.ts), [Google 공식 할당량 타입](https://github.com/google-gemini/gemini-cli/blob/main/packages/core/src/code_assist/types.ts). Google 설치형 OAuth 클라이언트의 공개 메타데이터 출처는 [oauth2.ts](https://github.com/google-gemini/gemini-cli/blob/main/packages/core/src/code_assist/oauth2.ts)이며 사용자 비밀 키가 아닙니다.

빌드 결과가 있으면 `artifacts/package/Desktop/AiUsage.Desktop.exe`를 실행하세요. 이 실행 파일은 데스크톱 미리보기이며 위젯 패널 등록은 아래 단계가 필요합니다.

```powershell
# 연결 전 상태
powershell -ExecutionPolicy Bypass -File scripts/Preview.ps1
# 샘플 화면 (사용량·설정을 실제 파일에 쓰지 않음)
powershell -ExecutionPolicy Bypass -File scripts/Preview.ps1 -Sample
```

## 빌드 / 위젯 패널 등록

필요 환경: Windows 11 22H2 이상, x64, .NET 10 SDK, Windows SDK(makeappx), Windows Web Experience Pack. 최초 빌드는 NuGet 다운로드가 필요합니다.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Build.ps1
```

`artifacts/AiUsageWidget.msix`와 `artifacts/package/`가 생성됩니다. .NET 및 Windows App SDK 런타임을 포함합니다. MSIX는 배포 서명 전 상태입니다. 개발 PC에서는 Windows 설정에서 **개발자 모드**를 켠 뒤 다음 명령으로 압축 해제된 패키지를 등록할 수 있습니다.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Install-Dev.ps1
```

이후 **Win + W → 위젯 추가 → Agent Usage**를 고정하세요. 6개 서비스를 표시하기 위해 큰 크기만 지원합니다. 등록 이후 `artifacts/package`를 이동하거나 삭제하지 마세요. 다른 PC 배포에는 신뢰할 수 있는 인증서 서명 또는 Microsoft Store 배포가 필요합니다.

위젯 패널은 Windows가 Adaptive Card를 렌더링하므로 버튼·간격·모서리가 데스크톱 미리보기와 일부 다릅니다. 실제 패널에서의 최종 모양과 테마 전환은 설치 후 확인해야 합니다.

## 사용량 데이터 계약

API 비용 수집기가 `%LOCALAPPDATA%/AiUsageWidget/usage.json`에 `examples/usage.sample.json` 형식으로 스냅샷을 기록하면 위젯이 읽습니다. 실제 모드에서 `chatgpt`, `claude`, `gemini` 항목은 각 서비스 조회 결과로 대체하며 파일에는 쓰지 않습니다. `IsSample: true`인 로컬 파일은 전체 샘플 모드로 취급해 네트워크 조회를 중지합니다. 토글을 끄면 해당 인스턴스의 추가 서버 조회도 중지합니다.

- `UpdatedAt`: 실제 수집 시각, ISO 8601 시간대 포함. 5분 이상 경과하면 오래된 데이터 표시
- `SessionPercent` / `WeeklyPercent`: 사용한 비율, 0–100. 알 수 없으면 null
- `SessionReset` / `WeeklyReset`: ISO 8601 리셋 시각. Windows 현지 시간으로 표시
- `Windows`: Gemini처럼 시간 창이 아닌 모델별 한도용 `{Label, Percent, Reset}` 배열
- `MonthCost` / `DayCost`: USD 비용. 집계 시간대는 수집기가 정함
- `IsSample`: 샘플이면 true. 실제 수집 결과만 false
- API 키나 세션 토큰은 이 파일에 넣지 마세요

샘플 파일의 날짜는 고정입니다. `--sample` 미리보기는 실행 시각을 기준으로 리셋 시간을 만들어 줍니다. 초기화는 해당 앱을 닫은 후 `%LOCALAPPDATA%/AiUsageWidget/settings.json`을 지우거나 위젯을 제거·다시 추가하면 됩니다.

## 검증

```powershell
dotnet run --project tests/AiUsage.Checks
```

토글 독립성·저장 복원, 전체 끄기, 미연결/0 비용 구분, 리셋 계산, 잘못된 데이터 복구, 두 탭의 카드 액션을 검사합니다. 빌드 과정에서 라이트·다크 Status / Setting PNG를 `artifacts/package/Assets`에 렌더링합니다.

현재 검증 결과: 기존 기능과 세 서비스 응답 매핑, 가짜 HTTP 전송을 이용한 인증·갱신·프로젝트 조회·429 대기·401 메시지·인증 파일 보존 검사 65개 통과. Codex는 실제 계정 조회를 검증했습니다. Claude·Gemini의 실제 로그인 완료 및 라이브 사용량 조회는 사용자 요청으로 보류했습니다. Windows 위젯 패널 내 클릭·테마 전환도 실기 검증 범위에 포함하지 않습니다.

v1.2.0.0 실행 파일·MSIX 빌드 및 이 PC의 Windows 패키지 등록을 완료했습니다. 샘플 Status와 로그인 버튼이 포함된 실제 Setting의 라이트·다크 PNG도 확인했습니다.

실제 계정 조회만 별도로 확인하려면 다음 명령을 실행하세요. 출력은 위젯 표시용 수치와 시간뿐입니다.

```powershell
dotnet run --project tests/AiUsage.Checks -- --live
dotnet run --project tests/AiUsage.Checks -- --live-claude
dotnet run --project tests/AiUsage.Checks -- --live-gemini
```

Codex 연결 참고: [공식 App Server 문서](https://learn.chatgpt.com/docs/app-server), [공식 인증 문서](https://learn.chatgpt.com/docs/auth). 사용자 제공 예제인 [spourdei/codex-usage-widget](https://github.com/spourdei/codex-usage-widget)과 [ZeroP27/codex-usage](https://github.com/ZeroP27/codex-usage)의 RPC 방식·시간 창 매핑을 참고하여 C#으로 별도 구현했습니다. 외부 프로젝트의 OAuth 토큰 직접 관리·계정 전환·리셋 크레딧 기능은 포함하지 않습니다.

구현 참고: [Microsoft 위젯 공급자 문서](https://learn.microsoft.com/en-us/windows/apps/develop/widgets/implement-widget-provider-cs), [위젯 패키지 매니페스트](https://learn.microsoft.com/en-us/windows/apps/develop/widgets/widget-provider-manifest).

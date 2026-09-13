# AI Usage Widget

Windows 11 위젯 패널용 C# 위젯과 별도 데스크톱 미리보기입니다. 참고 이미지의 서비스 순서와 Status / Setting 두 화면을 구현했습니다. Logs와 순서 변경 기능은 없습니다. Codex·Claude Code 로그인은 Setting에서 브라우저로 연결합니다.

- Codex, Claude Code, Antigravity, OpenCode, Command Code 표시 토글
- 각 제공자: 이름·플랜, 제공자가 보고한 사용률·리셋 시간, 진행 막대
- Windows 라이트·다크 테마 자동 적용
- 표시 언어는 Windows UI 언어와 관계없이 영어만 지원
- 위젯 설정은 Windows 위젯 호스트의 CustomState에 저장. 데스크톱 미리보기 설정은 별도로 저장
- 위젯은 보이는 동안 30초마다 화면을 갱신하고, 미리보기는 15초마다 갱신. 자동 서버 조회 간격은 인스턴스당 Codex 2분, Claude Code 5분. HTTP 429는 Retry-After에 따라 최소 5분 동안 재시도를 제한

**Codex, Claude Code, Antigravity, OpenCode, Command Code 사용량 조회를 지원합니다.** Codex는 일반 ChatGPT 대화 한도가 아니라 Codex 한도입니다. 기존 설정 호환을 위해 서비스 ID `chatgpt`, `claude`를 유지합니다.

샘플 실행은 네트워크 조회·로그인·설정 저장을 하지 않으며 화면 하단에 샘플임을 표시합니다. 샘플 플랜과 수치는 레이아웃 확인용입니다.

## Codex 연결

Codex CLI가 이미 설치되고 ChatGPT 계정으로 로그인되어 있으면 앱 실행 시 자동 연결됩니다. 이 PC에서 실제 조회를 검증했습니다.

처음 연결하거나 인증이 만료된 경우 데스크톱 앱의 **Setting → Codex 로그인 / 다시 연결**을 누르고 열린 브라우저에서 로그인하세요. 위젯 패널에서는 위젯의 `...` 메뉴에서 **Customize widget**을 선택해 Setting 화면으로 전환한 뒤 같은 연결 버튼을 사용할 수 있습니다. 비밀번호는 브라우저의 OpenAI 로그인 화면에서 직접 입력합니다. 위젯 카드 상단의 Status / Setting 버튼은 표시하지 않으며, 데스크톱 미리보기의 탭은 유지합니다.

CLI가 없다면 설치하고 로그인하세요.

```powershell
npm install -g @openai/codex
codex login
```

앱은 PATH의 `codex.exe` 또는 npm 설치 경로의 네이티브 실행 파일을 찾습니다. 다른 위치라면 `CODEX_BIN`에 `codex.exe`의 절대 경로를 지정한 뒤 앱을 다시 실행하세요. `CODEX_HOME`은 Codex가 기존 환경 설정대로 사용합니다. API 키 로그인은 이 구독 한도 조회용으로 사용하지 않습니다.

구현은 로컬 `codex app-server --listen stdio://`에 `initialize` → `initialized` → `account/read` → `account/rateLimits/read` 순서로 요청합니다. 프롬프트 실행이나 모델 호출은 하지 않습니다. 로그인은 공식 `codex login`이 담당합니다. 위젯은 `auth.json`, 비밀번호, 액세스 토큰, 갱신 토큰을 읽거나 별도로 저장하지 않으며, 이메일·계정 ID도 저장하지 않습니다. 사용량은 메모리에만 보관합니다.

서버의 사용 비율(`usedPercent`)을 남은 비율로 변환해 표시합니다. 서버의 `windowDurationMins`가 300인 창을 5시간, 10080인 창을 주간으로 매핑합니다. 주간 한도만 있는 계정은 5시간을 `—`로 표시하며, 다른 모델/코드 리뷰 한도를 섞지 않습니다. 조회 실패 시 이전 계정의 수치를 남기지 않고 연결 상태를 표시한 후 2분 간격으로 재시도합니다. 자동 조회는 브라우저를 열지 않습니다.

## 실행

### Claude Code 연결

이 PC에는 공식 Claude Code CLI 2.1.269를 설치했습니다. **지금은 로그인하지 않아도 됩니다.** 나중에 앱의 Setting에서 `Connect Claude Code`를 눌러 브라우저 인증을 완료하세요. 토글을 켜면 해당 로그인 정보로 자동 조회합니다.

다른 PC에서 필요한 CLI를 설치하려면:

```powershell
npm install -g @anthropic-ai/claude-code@2.1.269
```

Claude 로그인은 `claude auth login --claudeai`를 사용합니다. 로그인 창을 닫거나 5분 동안 완료하지 않으면 재연결할 수 있습니다.

Claude 조회는 `%USERPROFILE%/.claude/.credentials.json`의 구독 OAuth 정보를 읽어 `https://api.anthropic.com/api/oauth/usage`에 GET 요청합니다. `CLAUDE_CONFIG_DIR`을 지원합니다. 전체 `five_hour` / `seven_day`만 사용하며 Sonnet·Opus별 한도를 전체 주간 한도로 혼동하지 않습니다. CLI 인증 파일을 수정하지 않습니다. CLI가 토큰을 갱신하면 다음 조회에서 읽으며, 만료된 경우 Setting에서 재연결 안내를 표시합니다.

Claude의 OAuth 사용량 경로는 공개 결제 API가 아닌 CLI 서비스 경로라 변경될 수 있습니다. 조회 실패·로그인 만료·429는 서비스별 상태로 표시하며 다른 서비스 갱신을 막지 않습니다. 위젯은 비밀번호 입력을 받지 않고, 읽은 토큰·계정 식별자를 사용량 데이터나 로그에 기록하지 않습니다.

참고: [Claude 공식 인증·저장 위치](https://code.claude.com/docs/en/authentication), [Claude 저장소의 OAuth 사용량 응답 보고](https://github.com/anthropics/claude-code/issues/31021)

### Antigravity, OpenCode 연결

CodexBar 문서의 provider API 경로를 사용합니다. 앱은 비밀값을 저장하지 않고 현재 프로세스 환경 변수와 읽기 전용 로컬 credential 파일만 사용합니다.

- **Antigravity**: `Connect`는 일반 Google 계정 페이지가 아니라 설치된 `agy.exe` 또는 `antigravity.exe`를 실행합니다. Antigravity 앱/CLI에서 로그인하면 `~/.codexbar/antigravity/oauth_creds.json` 또는 `%USERPROFILE%/.gemini/oauth_creds.json`의 Google OAuth credential로 Code Assist `loadCodeAssist`와 `retrieveUserQuota`를 호출합니다. 다른 위치의 credential은 `ANTIGRAVITY_OAUTH_CREDENTIALS_JSON`에 JSON 전체를 지정할 수 있습니다. `GOOGLE_CLOUD_PROJECT` 또는 `GOOGLE_CLOUD_PROJECT_ID`가 필요할 수 있습니다.
- **OpenCode**: OpenCode Go API `https://opencode.ai/zen/go/v1/usage`를 호출합니다. API 키를 `OPENCODE_API_KEY`에 지정하면 rolling 5시간과 weekly 사용량을 읽습니다.
- **Command Code**: `COMMANDCODE_COOKIE`의 브라우저 `Cookie` 헤더로 `api.commandcode.ai/internal/billing/credits`와 subscription billing endpoint를 호출합니다. 먼저 [commandcode.ai](https://commandcode.ai)에 로그인한 뒤 Cookie 헤더를 지정하세요.

세 서비스의 `Connect` 버튼은 Antigravity는 앱/CLI를 실행하고, 나머지는 각 로그인 페이지를 기본 브라우저에서 엽니다. 로그인 후 앱을 새로고침하면 로컬 credential 또는 환경 변수/쿠키를 사용해 자동 조회합니다.

빌드 결과가 있으면 `artifacts/package/Desktop/AiUsage.Desktop.exe`를 실행하세요. 이 실행 파일은 데스크톱 미리보기이며 위젯 패널 등록은 아래 단계가 필요합니다.


## 빌드 / 위젯 패널 등록

필요 환경: Windows 11 22H2 이상, x64, .NET 10 SDK, Windows SDK(x64 makeappx·makepri), Windows Web Experience Pack. 설치에는 Windows 개발자 모드와 64비트 Windows PowerShell 5.1이 필요합니다. 최초 빌드는 NuGet 다운로드가 필요합니다.

앞으로 빌드 후 설치는 저장소 루트의 통합 스크립트를 사용합니다. 저장소 폴더에서 다음 명령을 실행하세요.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-install.ps1
```

기본 Release 빌드 → 오프라인 검사 → Desktop·Provider 자체 포함 게시 → 샘플 라이트·다크 Status / Setting 미리보기 생성 → PRI·MSIX 패키징 검증 → 현재 사용자의 개발 패키지 등록 → 등록 상태와 COM 공급자 활성화 확인을 순서대로 실행합니다. 계정 로그인이나 라이브 사용량 검사는 실행하지 않습니다. `packaging/Assets`에 포함된 로고를 사용하므로 기존 `artifacts` 없이도 빌드할 수 있습니다.

```powershell
# 설치하지 않고 빌드·검증·패키징만 실행
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-install.ps1 -BuildOnly

# MSIX 생성 전용 명령 (설치 생략, -BuildOnly와 동일)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-install.ps1 -Msix

# Debug 구성으로 빌드·검증·설치
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-install.ps1 -Configuration Debug
```

기본 실행, `-BuildOnly`, `-Msix` 모두 빌드·검증 후 `artifacts/msix/<패키지 이름>_<버전>_<아키텍처>.msix`를 생성합니다. 현재 매니페스트 기준 파일명은 `Mossworm.AiUsageWidget_1.2.4.0_x64.msix`입니다. 최신 패키지는 `artifacts/AiUsageWidget.msix`에도 복사합니다. 같은 버전은 덮어쓰고 다른 버전의 MSIX는 보존합니다. MSIX는 서명되지 않으며 개발 설치는 매니페스트 등록 방식입니다.

게시 파일과 패키징 로그(`resources.log`, `packaging.log`)는 `artifacts/publish/`에 생성됩니다. 매 실행 전에 준비 폴더 `artifacts/publish/package`만 비우고, MSIX와 로그는 최신 결과로 갱신합니다. `-BuildOnly`와 `-Msix`는 기존 설치를 교체하지 않으며 개발자 모드가 필요하지 않습니다. 기본 실행은 패키지 생성 후 설치까지 진행하며, 설치 성공 시 실행 파일은 `artifacts/package`에 둡니다.

검증 완료 후 이 저장소의 실행 중인 Desktop·Provider를 종료하고 패키지를 교체합니다. 이전 `artifacts/package` 파일은 `artifacts/publish/previous-package-<고유 ID>`에 보관하며, 이후 빌드에서도 기존 백업을 보존합니다. 등록 또는 활성화 실패 시 이전 파일과 등록을 복구합니다. 기존 설치가 이 저장소의 `artifacts/버전/package`에 있으면 원래 파일을 보존하고, 기존 개발 등록을 제거한 뒤 `artifacts/package`로 다시 등록합니다. 경로 전환 시 위젯 패널에서 AI Usage를 다시 추가해야 할 수 있습니다. 복구까지 실패하면 경고에 표시된 경로와 오류를 확인하세요. `%LOCALAPPDATA%/AiUsageWidget`의 사용자 설정은 유지합니다. 서명된 설치나 이 저장소의 `artifacts` 밖에 있는 개발 등록은 교체하지 않고 중단합니다. 같은 저장소에서 스크립트를 동시에 실행할 수 없습니다.

이후 **Win + W → 위젯 추가 → AI Usage**를 고정하세요. 작음·보통·큼 크기를 지원하며 모든 크기에 동일한 레이아웃을 사용하므로 작은 크기에서는 일부 내용이 잘릴 수 있습니다. 등록 이후 `artifacts/package`를 이동하거나 삭제하지 마세요. 다른 PC 배포에는 신뢰할 수 있는 인증서 서명 또는 Microsoft Store 배포가 필요합니다.

개발 등록을 제거하려면 다음 명령을 실행하세요. 기본적으로 앱 등록만 제거하고 `%LOCALAPPDATA%/AiUsageWidget`의 설정은 보존합니다.

```powershell
Get-AppxPackage -Name Mossworm.AiUsageWidget | Remove-AppxPackage
```

위젯 패널은 Windows가 Adaptive Card를 렌더링하므로 버튼·간격·모서리가 데스크톱 미리보기와 일부 다릅니다. 실제 패널에서의 최종 모양과 테마 전환은 설치 후 확인해야 합니다.

## 사용량 데이터 계약

사용량 수집기가 `%LOCALAPPDATA%/AiUsageWidget/usage.json`에 `examples/usage.sample.json` 형식으로 스냅샷을 기록하면 위젯이 읽습니다. 실제 모드에서 `chatgpt`, `claude` 항목은 각 서비스 조회 결과로 대체하며 파일에는 쓰지 않습니다. `IsSample: true`인 로컬 파일은 전체 샘플 모드로 취급해 네트워크 조회를 중지합니다. 토글을 끄면 해당 인스턴스의 추가 서버 조회도 중지합니다.

- `UpdatedAt`: 실제 수집 시각, ISO 8601 시간대 포함. 5분 이상 경과하면 오래된 데이터 표시
- `SessionPercent` / `WeeklyPercent`: 사용한 비율, 0–100. 알 수 없으면 null
- `SessionReset` / `WeeklyReset`: ISO 8601 리셋 시각. Windows 현지 시간으로 표시
- `Windows`: 모델별 한도용 `{Label, Percent, Reset}` 배열
- `MonthCost` / `DayCost`: USD 비용. 집계 시간대는 수집기가 정함
- `IsSample`: 샘플이면 true. 실제 수집 결과만 false
- API 키나 세션 토큰은 이 파일에 넣지 마세요

샘플 파일의 날짜는 고정입니다. `--sample` 미리보기는 실행 시각을 기준으로 리셋 시간을 만들어 줍니다. 초기화는 해당 앱을 닫은 후 `%LOCALAPPDATA%/AiUsageWidget/settings.json`을 지우거나 위젯을 제거·다시 추가하면 됩니다.

## 검증

```powershell
dotnet run --project tests/AiUsage.Checks
```

토글 독립성·저장 복원, 전체 끄기, 미연결/0 비용 구분, 리셋 계산, 잘못된 데이터 복구, 위젯 상단 내비게이션 제거, 사용자 지정 메뉴와 콜백 프록시 등록을 검사합니다. 빌드 과정에서 라이트·다크 Status / Setting PNG를 `artifacts/package/Assets`에 렌더링합니다.

`Customize widget` 메뉴가 열려도 설정 화면으로 바뀌지 않는다면 패키지 매니페스트의 `IWidgetProvider2` 프록시 등록을 확인하세요. 이 프로젝트는 MSIX를 직접 조립하므로 Windows App SDK의 `package.appxfragment`에 있는 사용자 지정 콜백 프록시를 데스크톱 COM용 `windows.comInterface` 형식으로 `packaging/AppxManifest.xml`에 명시합니다. `IsCustomizable`과 C# 인터페이스 구현만으로는 프로세스 간 콜백 전달이 되지 않습니다. [COM 인터페이스 등록 문서](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-com-cominterface)를 참고하세요. 매니페스트 변경 후에는 패키지 버전을 올리고, `build-install.ps1`을 실행해 빌드·검증 후 패키지를 다시 등록해야 합니다.

현재 검증 결과: Codex·Claude Code·Antigravity·OpenCode·Command Code 응답 매핑, 가짜 HTTP 전송을 이용한 인증·429 대기·401 메시지·인증 파일 보존 검사를 통과했습니다. Codex는 실제 계정 조회를 검증했습니다. 다른 제공자의 실제 로그인 및 라이브 사용량 조회는 별도 실행이 필요합니다. Windows 위젯 패널 내 클릭·테마 전환도 실기 검증 범위에 포함하지 않습니다.

v1.2.0.0 실행 파일·MSIX 빌드 및 이 PC의 Windows 패키지 등록을 완료했습니다. 샘플 Status와 로그인 버튼이 포함된 실제 Setting의 라이트·다크 PNG도 확인했습니다.

실제 계정 조회만 별도로 확인하려면 다음 명령을 실행하세요. 출력은 위젯 표시용 수치와 시간뿐입니다.

```powershell
dotnet run --project tests/AiUsage.Checks -- --live
dotnet run --project tests/AiUsage.Checks -- --live-claude
dotnet run --project tests/AiUsage.Checks -- --live-claude
```

Codex 연결 참고: [공식 App Server 문서](https://learn.chatgpt.com/docs/app-server), [공식 인증 문서](https://learn.chatgpt.com/docs/auth). 사용자 제공 예제인 [spourdei/codex-usage-widget](https://github.com/spourdei/codex-usage-widget)과 [ZeroP27/codex-usage](https://github.com/ZeroP27/codex-usage)의 RPC 방식·시간 창 매핑을 참고하여 C#으로 별도 구현했습니다. 외부 프로젝트의 OAuth 토큰 직접 관리·계정 전환·리셋 크레딧 기능은 포함하지 않습니다.

구현 참고: [Microsoft 위젯 공급자 문서](https://learn.microsoft.com/en-us/windows/apps/develop/widgets/implement-widget-provider-cs), [위젯 패키지 매니페스트](https://learn.microsoft.com/en-us/windows/apps/develop/widgets/widget-provider-manifest).

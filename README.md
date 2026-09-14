# AI Usage Widget

Windows 11 위젯 패널용 C# 위젯과 별도 데스크톱 미리보기입니다. 참고 이미지의 서비스 순서와 Status / Setting 두 화면을 구현했습니다. Logs와 순서 변경 기능은 없습니다. 연결 절차는 없습니다. PC에 이미 설치된 Codex·Claude Code CLI의 로그인을 그대로 읽어 씁니다.

- Codex, Claude Code 표시 토글
- 각 제공자: 이름·플랜, 제공자가 보고한 사용률·리셋 시간, 진행 막대
- Windows 라이트·다크 테마 자동 적용
- 표시 언어는 Windows UI 언어와 관계없이 영어만 지원
- 위젯 설정은 Windows 위젯 호스트의 CustomState에 저장. 데스크톱 미리보기 설정은 별도로 저장
- 위젯은 보이는 동안 30초마다 화면을 갱신하고, 미리보기는 15초마다 갱신. 자동 서버 조회 간격은 인스턴스당 Codex 2분, Claude Code 5분. HTTP 429는 Retry-After에 따라 최소 5분 동안 재시도를 제한

**Codex, Claude Code 사용량 조회를 지원합니다.** Codex는 일반 ChatGPT 대화 한도가 아니라 Codex 한도입니다. 기존 설정 호환을 위해 서비스 ID `chatgpt`, `claude`를 유지합니다.

샘플 실행은 네트워크 조회·로그인·설정 저장을 하지 않으며 화면 하단에 샘플임을 표시합니다. 샘플 플랜과 수치는 레이아웃 확인용입니다.

## Codex 연결

Codex CLI가 이미 설치되고 ChatGPT 계정으로 로그인되어 있으면 앱 실행 시 자동 연결됩니다. 이 PC에서 실제 조회를 검증했습니다.

로그인한 적이 없거나 인증이 만료되면 터미널에서 `codex login`을 한 번 실행하세요. 앱에는 로그인 버튼이 없습니다. 위젯 카드 상단의 Status / Setting 버튼은 표시하지 않으며, 데스크톱 미리보기의 탭은 유지합니다.

CLI가 없다면 설치하고 로그인하세요.

```powershell
npm install -g @openai/codex
codex login
```

앱은 아래 위치를 모두 후보로 모은 뒤 **가장 최근에 갱신된 `codex.exe`** 를 씁니다. 설치 방식이 여러 개여도 오래된 바이너리에 걸리지 않습니다.

1. PATH의 `codex.exe`, 그리고 전역 npm(`%APPDATA%/npm`)·`Program Files/nodejs` 아래의 네이티브 실행 파일
2. 단독 설치본의 버전별 폴더 `%LOCALAPPDATA%/OpenAI/Codex/bin/<버전>/codex.exe`
3. VS Code 계열 에디터의 `openai.chatgpt-*` 확장에 동봉된 `bin/windows-x86_64/codex.exe` (`.vscode`, `.vscode-insiders`, `.windsurf`, `.cursor`)

특정 바이너리를 고정하려면 `CODEX_EXECUTABLE`(또는 기존 이름 `CODEX_BIN`)에 `codex.exe`의 절대 경로를 지정하세요. 이 값이 있으면 탐색을 건너뛰고 그 파일만 씁니다. `CODEX_HOME`은 Codex가 기존 환경 설정대로 사용합니다. API 키 로그인은 이 구독 한도 조회용으로 사용하지 않습니다.

구현은 로컬 `codex app-server --listen stdio://`에 `initialize` → `initialized` → `account/read` → `account/rateLimits/read` 순서로 요청합니다. 프롬프트 실행이나 모델 호출은 하지 않습니다. 로그인은 공식 `codex login`이 담당합니다. 위젯은 `auth.json`, 비밀번호, 액세스 토큰, 갱신 토큰을 읽거나 별도로 저장하지 않으며, 이메일·계정 ID도 저장하지 않습니다. 사용량은 메모리에만 보관합니다.

서버의 사용 비율(`usedPercent`)을 남은 비율로 변환해 표시합니다. 서버의 `windowDurationMins`가 300인 창을 5시간, 10080인 창을 주간으로 매핑합니다. 주간 한도만 있는 계정은 5시간을 `—`로 표시하며, 다른 모델/코드 리뷰 한도를 섞지 않습니다. 조회 실패 시 이전 계정의 수치를 남기지 않고 연결 상태를 표시한 후 2분 간격으로 재시도합니다. 자동 조회는 브라우저를 열지 않습니다.

## 실행

### Claude Code 연결

**Claude Code CLI의 로그인을 그대로 사용합니다.** 앱에는 로그인 화면도, 연결 버튼도 없습니다.

Claude 조회는 Claude Code CLI 인증 파일(`%USERPROFILE%/.claude/.credentials.json`, `CLAUDE_CONFIG_DIR` 지원)에서 `claudeAiOauth.accessToken`을 읽어 `https://api.anthropic.com/api/oauth/usage`에 GET 요청합니다. 플랜 배지는 같은 파일의 `subscriptionType`을 씁니다. **인증 파일은 읽기만 하고 절대 수정하지 않습니다.** 앱은 자체 토큰을 저장하지 않습니다.

파일의 `expiresAt`이 지났으면 요청을 보내지 않고 `Claude Code sign-in expired · open Claude Code to refresh it`을 표시합니다. 파일에 함께 들어 있는 refresh token은 **일부러 쓰지 않습니다.** 갱신하면 토큰이 회전되어 실행 중인 CLI가 가진 값이 무효가 되고, 사용자가 Claude Code에서 로그아웃되기 때문입니다. 갱신은 Claude Code를 한 번 실행하면 CLI가 알아서 처리합니다.

기본 두 줄은 전체 한도인 `five_hour` / `seven_day`입니다. 여기에 **모델별 주간 한도를 한 줄 더** 표시하며 기본값은 Fable입니다. 모델별 한도는 응답의 `limits` 배열에서 `kind`가 `weekly_scoped`이고 `scope.model.display_name`이 일치하는 항목을 읽습니다(`seven_day_opus`·`seven_day_sonnet` 같은 키는 실제 계정에서 모두 `null`이라 쓰지 않습니다). 구형 `seven_day_<모델>` 표기도 대체 경로로 매칭합니다. 해당 모델 한도가 없거나 숫자가 없으면 줄을 추가하지 않고 기존 두 줄만 보여줍니다. 모델별 한도를 전체 주간 한도로 혼동하지 않습니다. 줄 이름은 제공자가 준 표시 이름을 그대로 씁니다.

표시할 모델은 `AIUSAGE_CLAUDE_MODEL_WINDOWS`로 바꿉니다. 쉼표로 여러 개를 넣을 수 있고(`fable,opus`), 빈 값이면 전체 한도 두 줄만 남습니다. 막대 색은 위에서부터 파랑(5시간) · 초록(주간) · 주황(모델별)입니다.

계정이 실제로 어떤 모델별 한도를 반환하는지 확인하려면 라이브 조회를 실행하세요. `Per-model weekly quotas:` 줄에 사용 가능한 모델 이름이 나오며, 응답 수치는 출력하지 않습니다.

```powershell
dotnet run --project tests/AiUsage.Checks -- --live-claude
```

**주의.** `/api/oauth/usage`는 Anthropic이 공개한 API가 아니라 Claude Code CLI가 쓰는 비공개 경로입니다. 요청에는 CLI의 `anthropic-beta: oauth-2025-04-20` 헤더와 User-Agent를 그대로 실어 보내므로, 서버에는 이 앱이 Claude Code로 보입니다. 사용 약관상 회색지대이며 Anthropic이 언제든 바꿀 수 있습니다. 변경 시 재빌드 없이 고칠 수 있도록 값을 환경 변수로 덮어쓸 수 있습니다.

| 환경 변수 | 기본값 |
| --- | --- |
| `AIUSAGE_CLAUDE_USER_AGENT` | `claude-code/0.2.29` |
| `AIUSAGE_CLAUDE_MODEL_WINDOWS` | `fable` |
| `CLAUDE_CONFIG_DIR` | `%USERPROFILE%/.claude` |

조회 실패·로그인 만료·429는 서비스별 상태로 표시하며 다른 서비스 갱신을 막지 않습니다. 위젯은 비밀번호를 입력받지 않고, 토큰·계정 식별자를 사용량 데이터나 로그에 기록하지 않습니다.

참고: [Claude 공식 인증·저장 위치](https://code.claude.com/docs/en/authentication), [Claude 저장소의 OAuth 사용량 응답 보고](https://github.com/anthropics/claude-code/issues/31021)

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

기본 실행, `-BuildOnly`, `-Msix` 모두 빌드·검증 후 `artifacts/msix/<패키지 이름>_<버전>_<아키텍처>.msix`를 생성합니다. 현재 매니페스트 기준 파일명은 `Mossworm.AIUsageWidget_1.2.4.0_x64.msix`입니다. 최신 패키지는 `artifacts/AiUsageWidget.msix`에도 복사합니다. 같은 버전은 덮어쓰고 다른 버전의 MSIX는 보존합니다. MSIX는 서명되지 않으며 개발 설치는 매니페스트 등록 방식입니다.

게시 파일과 패키징 로그(`resources.log`, `packaging.log`)는 `artifacts/publish/`에 생성됩니다. 매 실행 전에 준비 폴더 `artifacts/publish/package`만 비우고, MSIX와 로그는 최신 결과로 갱신합니다. `-BuildOnly`와 `-Msix`는 기존 설치를 교체하지 않으며 개발자 모드가 필요하지 않습니다. 기본 실행은 패키지 생성 후 설치까지 진행하며, 설치 성공 시 실행 파일은 `artifacts/package`에 둡니다.

검증 완료 후 이 저장소의 실행 중인 Desktop·Provider를 종료하고 패키지를 교체합니다. 이전 `artifacts/package` 파일은 `artifacts/publish/previous-package-<고유 ID>`에 보관하며, 이후 빌드에서도 기존 백업을 보존합니다. 등록 또는 활성화 실패 시 이전 파일과 등록을 복구합니다. 기존 설치가 이 저장소의 `artifacts/버전/package`에 있으면 원래 파일을 보존하고, 기존 개발 등록을 제거한 뒤 `artifacts/package`로 다시 등록합니다. 경로 전환 시 위젯 패널에서 AI Usage를 다시 추가해야 할 수 있습니다. 복구까지 실패하면 경고에 표시된 경로와 오류를 확인하세요. `%LOCALAPPDATA%/AiUsageWidget`의 사용자 설정은 유지합니다. 서명된 설치나 이 저장소의 `artifacts` 밖에 있는 개발 등록은 교체하지 않고 중단합니다. 같은 저장소에서 스크립트를 동시에 실행할 수 없습니다.

이후 **Win + W → 위젯 추가 → AI Usage**를 고정하세요. 작음·보통·큼 크기를 지원하며 모든 크기에 동일한 레이아웃을 사용하므로 작은 크기에서는 일부 내용이 잘릴 수 있습니다. 등록 이후 `artifacts/package`를 이동하거나 삭제하지 마세요. 다른 PC 배포에는 신뢰할 수 있는 인증서 서명 또는 Microsoft Store 배포가 필요합니다.

기존 개발 등록을 먼저 제거한 뒤 새로 빌드해 설치하려면 루트의 제거·빌드·설치 스크립트를 사용하세요.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\uninstall-build-install.ps1
```

개발자 모드를 확인하고 이 저장소의 실행 중인 Desktop·Provider를 종료한 뒤 개발 등록을 제거하고, 남은 `artifacts/package`를 `artifacts/removed-package-<고유 ID>`로 옮긴 다음 `build-install.ps1`을 그대로 실행합니다. 기본적으로 `Remove-AppxPackage -PreserveApplicationData`로 제거해 앱 데이터를 보존하며, `-RemoveAppData`를 주면 설정과 로그인까지 삭제합니다. `-Configuration`은 `build-install.ps1`에 전달합니다. 서명된 설치나 이 저장소의 `artifacts` 밖에 있는 개발 등록은 제거하지 않고 중단합니다. 제거 단계도 `build-install.ps1`과 같은 잠금 파일을 사용하므로 두 스크립트를 동시에 실행할 수 없습니다.

개발 등록을 제거하려면 다음 명령을 실행하세요. 기본적으로 앱 등록만 제거하고 `%LOCALAPPDATA%/AiUsageWidget`의 설정은 보존합니다.

```powershell
Get-AppxPackage -Name Mossworm.AIUsageWidget | Remove-AppxPackage
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

현재 검증 결과: Codex·Claude Code 응답 매핑, 가짜 HTTP 전송을 이용한 인증·429 대기·401 메시지·인증 파일 보존 검사를 통과했습니다. Claude는 CLI 인증 파일이 없을 때와 만료됐을 때 네트워크 요청 없이 안내 문구를 내는지, 요청이 CLI User-Agent로 나가는지, Setting 카드에 로그인 진입점이 남아 있지 않은지를 검증했습니다. 모델별 주간 한도는 `limits`의 `weekly_scoped` 파싱·다른 모델 미매칭·구형 키 표기 매칭·빈 값 시 줄 미추가를 검증했습니다. 실제 Max 계정에서 CLI 인증 파일만으로 사용량 조회와 Fable 주간 한도 표시까지 실기 검증했습니다. Codex도 실제 계정 조회를 검증했습니다. Windows 위젯 패널 내 클릭·테마 전환도 실기 검증 범위에 포함하지 않습니다.

v1.2.0.0 실행 파일·MSIX 빌드 및 이 PC의 Windows 패키지 등록을 완료했습니다. 샘플 Status와 로그인 버튼이 포함된 실제 Setting의 라이트·다크 PNG도 확인했습니다.

실제 계정 조회만 별도로 확인하려면 다음 명령을 실행하세요. 출력은 위젯 표시용 수치와 시간뿐입니다.

```powershell
dotnet run --project tests/AiUsage.Checks -- --live
dotnet run --project tests/AiUsage.Checks -- --live-claude
```

Codex 연결 참고: [공식 App Server 문서](https://learn.chatgpt.com/docs/app-server), [공식 인증 문서](https://learn.chatgpt.com/docs/auth). 사용자 제공 예제인 [spourdei/codex-usage-widget](https://github.com/spourdei/codex-usage-widget)과 [ZeroP27/codex-usage](https://github.com/ZeroP27/codex-usage)의 RPC 방식·시간 창 매핑을 참고하여 C#으로 별도 구현했습니다. 외부 프로젝트의 OAuth 토큰 직접 관리·계정 전환·리셋 크레딧 기능은 포함하지 않습니다.

구현 참고: [Microsoft 위젯 공급자 문서](https://learn.microsoft.com/en-us/windows/apps/develop/widgets/implement-widget-provider-cs), [위젯 패키지 매니페스트](https://learn.microsoft.com/en-us/windows/apps/develop/widgets/widget-provider-manifest).

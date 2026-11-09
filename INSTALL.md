# ORVIO 설치

TV에 연결한 윈도우 PC에 ORVIO를 올리는 방법입니다. 위에서부터 차례로 하면 됩니다.

## 필요한 것

| 항목 | 내용 |
|---|---|
| 운영체제 | Windows 11에서 확인했습니다. |
| [Playnite](https://github.com/JosefNemec/Playnite/releases/latest) | 10.63에서 확인했습니다. 포터블판이 아닌 설치형을 씁니다. |
| WebView2 런타임 | 윈도우 11에는 보통 들어 있습니다. 없으면 ORVIO가 알려 줍니다. |
| Microsoft Edge | 웹 바로가기를 열 때 씁니다. |
| 컨트롤러 | 엑스박스 방식(XInput) 컨트롤러 |
| [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | 빌드할 때만 필요합니다. |

## 1. Playnite 준비

1. Playnite를 설치하고 켭니다.
2. 처음 뜨는 마법사에서 쓰는 런처를 연결합니다. 게임 패스는 Xbox, 그 밖에 Steam, Ubisoft Connect 등이 있습니다.
3. EA 앱은 왼쪽 위 Playnite 아이콘 → `애드온…` → `탐색` → `라이브러리`에서 **EA app library integration**을 설치합니다.
4. 왼쪽 위 아이콘 → `게임 라이브러리 업데이트`로 게임을 불러옵니다.

## 2. ORVIO 빌드와 설치

### 윈도우에서 빌드

PowerShell에서 실행합니다.

```powershell
winget install --id Microsoft.DotNet.SDK.10 -e
git clone https://github.com/ggabdoll/orvio.git
cd orvio
dotnet build extension\Orvio\Orvio.csproj -c Release
```

`winget` 으로 설치했다면 PowerShell을 새로 연 뒤 `dotnet build` 를 실행합니다. 결과는 `extension\Orvio\bin\Release` 폴더에 생깁니다.

> 작성자는 macOS에서 빌드해 윈도우로 옮겨 썼습니다. 아래의 '다른 PC에서 빌드' 방법입니다.

### Playnite에 붙이기

1. 왼쪽 위 Playnite 아이콘 → `설정…` → `개발자용` 으로 갑니다.
2. `외부 확장 프로그램` 아래 `추가` 를 누릅니다.
3. 새 줄의 `경로` 칸 옆 폴더 단추로 `extension\Orvio\bin\Release` 폴더를 고릅니다. 그 폴더 바로 안에 `extension.yaml` 이 있어야 합니다.
4. `활성화` 가 켜져 있는지 확인합니다.
5. `저장` 을 누르고 재시작을 묻는 창에서 `예` 를 누릅니다. ORVIO가 전체화면으로 뜹니다.

### 다른 PC에서 빌드

맥이나 리눅스에서 만들어 옮길 수도 있습니다.

```bash
extension/pack.sh
cp extension/dist/Orvio.pext orvio-dev.zip
```

`orvio-dev.zip` 을 윈도우로 옮겨 `다운로드` 폴더에 두고, PowerShell에서 실행합니다.

```powershell
Expand-Archive "$HOME\Downloads\orvio-dev.zip" "$HOME\orvio-dev" -Force
Get-ChildItem "$HOME\orvio-dev" -Recurse -File | Unblock-File
```

그다음 위의 'Playnite에 붙이기'에서 `orvio-dev` 폴더를 고릅니다.

코드를 고쳐 다시 넣을 때는 알림 영역(트레이)의 Playnite 아이콘을 오른쪽 클릭해 `종료` 한 뒤 파일을 바꿉니다. 창만 닫으면 Playnite가 트레이에 남아 있어 파일을 덮어쓰지 못합니다.

## 3. Playnite 설정

왼쪽 위 Playnite 아이콘 → `설정…` → `일반` 에서 위 칸부터 차례로 바꿉니다.

| 항목 | 영문 화면 | 값 |
|---|---|---|
| 컴퓨터를 부팅했을 때 Playnite 실행 | Launch Playnite when you start your computer | 켬 |
| 트레이로 숨김 | Start closed to tray | 켬 |
| Playnite를 최소화된 상태로 시작 | Launch Playnite minimized | 켬 |
| 알림 영역에 아이콘 표시 | Show system tray icon | 켬 |
| 알림 영역으로 Playnite 최소화 | Minimize Playnite to system tray | 켬 |
| 전체 화면 모드로 시작 | Launch in Fullscreen Mode | **끔** |
| 게임 시작 시: | When game starts: | 최소화 |
| 게임 종료 시: | After game closes: | **아무것도 하지않음** |
| 플레이 시간 가져오기: | Import playtime of games in library: | 항상 가져오기 |

Playnite가 이미 전체화면 모드로 켜져 있으면 `Win + R` 에 아래를 넣어 데스크톱 모드로 돌린 뒤 위 설정을 바꿉니다.

```
%LOCALAPPDATA%\Playnite\Playnite.DesktopApp.exe --startdesktop
```

## 4. 윈도우 설정

### 애니메이션 켜기

윈도우의 `애니메이션 효과` 가 꺼져 있으면 ORVIO도 모든 움직임을 끕니다. 원판이 돌지 않고, 표지 회전과 글자 전환도 나오지 않습니다.

1. `Win + R` 을 누르고 아래를 입력한 뒤 Enter를 누릅니다.
   ```
   ms-settings:easeofaccess-visualeffects
   ```
   안 열리면 `설정` → `접근성` → `시각 효과` 로 갑니다.
2. `애니메이션 효과` 를 켭니다.
3. Playnite를 다시 켭니다.

지금 상태는 PowerShell에서 확인할 수 있습니다. `True` 면 켜진 것입니다.

```powershell
Add-Type -AssemblyName PresentationFramework; [System.Windows.SystemParameters]::ClientAreaAnimation
```

### 컨트롤러가 두 번 입력되지 않게

Steam을 켠 채 바탕화면에서 스틱을 움직였을 때 마우스 커서가 움직이면, Steam이 컨트롤러를 가로채는 것입니다. `Steam` → `Settings` → `Controller` → `Desktop Layout` 옆 `Edit` → 톱니바퀴 → `Disable Steam Input` 을 고릅니다. 메뉴 이름은 Steam 판과 표시 언어에 따라 다를 수 있습니다.

### 켜지는 데 오래 걸리면

윈도우는 로그인 뒤 시작 프로그램을 차례로 켭니다. `Win + R` 에 `ms-settings:startupapps` 를 넣어, 부팅 때 필요 없는 프로그램을 끕니다. **Playnite는 켜 둡니다.**

## 5. 바탕화면 바로가기

ORVIO가 숨어 있을 때 마우스로 다시 부르는 길입니다.

1. 바탕화면 빈 곳을 오른쪽 클릭 → `새로 만들기` → `바로 가기`
2. 항목 위치에 `playnite://orvio/show` 를 넣고 `다음` 을 누릅니다.
3. 이름에 `ORVIO` 를 넣고 `마침` 을 누릅니다.

아이콘은 만든 바로가기의 `속성` → `웹 문서` 탭 → `아이콘 변경` 에서 빌드 폴더의 `orvio-icon.ico` 로 바꿀 수 있습니다.

## 6. 설정 파일

Playnite 설정 화면에는 ORVIO 항목이 없습니다. 아래 파일을 고친 뒤 Playnite를 다시 켜면 적용됩니다.

```
%AppData%\Playnite\ExtensionsData\a936dbac-b13c-47cc-9278-ae1a453fd946\settings.json
```

| 키 | 기본값 | 뜻 |
|---|---|---|
| `theme` | `auto` | `auto`(19시~7시 다크) · `light` · `dark` |
| `sfxVolume` | `50` | 효과음 크기 0~100. 0이면 끕니다. |
| `menuHoldMs` | `1000` | Menu를 몇 ms 누르면 홈을 부를지 |
| `cursorSpeed` · `wheelSpeed` | `1800` · `1500` | 웹에서 컨트롤러 커서와 스크롤 속도 |
| `web` | 유튜브 등 | 웹 바로가기 목록(`id` · `title` · `url` · `color`) |
| `launchTimeoutSec` | `90` | 게임이 몇 초 안에 안 뜨면 실패로 볼지 |
| `diagnostics` | `true` | 진단 로그. 필요 없으면 `false` |

## 7. 문제 해결

| 증상 | 확인할 것 |
|---|---|
| 원판이 돌지 않고 순간이동하듯 바뀜 | [윈도우 `애니메이션 효과`](#애니메이션-켜기)를 켭니다. |
| ORVIO가 안 뜨고 Playnite만 뜸 | `개발자용` 의 경로가 `extension.yaml` 이 있는 폴더인지, `활성화` 가 켜져 있는지 봅니다. |
| ORVIO가 숨어서 안 보임 | 컨트롤러 Menu를 1초 길게 누르거나 바탕화면 바로가기를 엽니다. |
| 브라우저를 닫아도 홈이 안 돌아옴 | Edge 창이 남아 있거나, 다른 창을 쓰는 중에 닫은 경우입니다. Menu를 길게 누릅니다. |
| 효과음이 안 남 | `settings.json` 의 `sfxVolume` 과 Playnite 설치 폴더의 `Themes\Fullscreen\Default\Audio` 를 봅니다. |
| 10분 넘게 쉰 뒤 첫 소리만 안 남 | 정상입니다. PC 절전을 막지 않으려고 소리 출력 깨우기를 멈추기 때문입니다. |
| 게임이 목록에 없음 | Playnite에서 런처 연결과 `게임 라이브러리 업데이트` 를 확인합니다. ORVIO에는 설치된 게임만 나옵니다. |
| 게임이 끝났는데 Playnite 창이 뜸 | `게임 종료 시:` 를 `아무것도 하지않음` 으로 바꿉니다. |
| 스틱을 움직이면 커서도 움직임 | [Steam 설정](#컨트롤러가-두-번-입력되지-않게)을 확인합니다. |

로그는 `settings.json` 과 같은 폴더의 `orvio.log` 와 `%AppData%\Playnite\playnite.log` 에 있습니다.

## 8. 제거

1. `설정…` → `개발자용` → `외부 확장 프로그램` 에서 ORVIO 줄을 지우고 `저장` 합니다.
2. 바탕화면 바로가기와 ORVIO를 넣어 둔 폴더를 지웁니다.
3. 설정과 로그까지 지우려면 `%AppData%\Playnite\ExtensionsData\a936dbac-b13c-47cc-9278-ae1a453fd946` 폴더를 지웁니다.

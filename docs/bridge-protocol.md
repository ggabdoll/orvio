# ORVIO 메시지 약속 (화면 ↔ 확장)

화면(`web/`, WebView2 안의 HTML·JS)과 확장(`extension/`, C#)이 주고받는 메시지의 기준입니다. 둘 다 이 문서를 따릅니다.

## 1. 통로

| 방향 | WebView2 (실제) | 맥 브라우저 (시험 모드) |
|---|---|---|
| 화면 → 확장 | `window.chrome.webview.postMessage(obj)` | `web/test/fake-bridge.js` 가 받아 흉내 |
| 확장 → 화면 | `CoreWebView2.PostWebMessageAsJson(json)` → 화면은 `window.chrome.webview.addEventListener('message', e => e.data)` | 가짜 다리가 같은 꼴로 보냄 |

- 화면 코드는 `web/bridge.js` 의 `bridge.send(type, payload)` · `bridge.on(fn)` 만 씁니다. `window.chrome.webview` 가 있으면 실제, 없으면 가짜 다리입니다.
- 모든 메시지는 `{ "type": "...", ...필드 }` 꼴의 JSON 객체입니다. 모르는 `type` 은 양쪽 모두 무시하고 로그만 남깁니다.

## 2. 화면 주소

| 가상 호스트 | 폴더 | 접근 |
|---|---|---|
| `https://orvio-app.example/` | 확장 설치 폴더의 `web/` | 홈 화면 HTML·JS·글꼴 |
| `https://orvio-covers.example/` | 확장 데이터 폴더의 `covers/` | 잘라 둔 표지 PNG |

- 화면 호스트는 `Deny`, 표지 호스트는 `DenyCors`(다른 출처 화면의 `<img>`·CSS 배경은 허용, fetch·XHR은 막음). `Deny` 면 화면이 표지를 못 읽습니다. 이 두 호스트 말고는 탐색을 막습니다(외부 웹은 Edge로).
- 화면 시작 주소: `https://orvio-app.example/index.html`

## 3. 화면 → 확장

| type | 필드 | 확장이 하는 일 |
|---|---|---|
| `ready` | `version` (화면 판) | `init` 과 `library` 를 보냄. 게임이 켜지는 중이거나 실행 중이면 이어서 그 게임의 `gameState` 를 한 번 더 보냄(화면을 다시 만든 뒤에도 '게임 실행 중' 줄과 전원 막기가 이어지게). 이미 `init` 을 보낸 화면의 `ready` 는 무시하되, 화면 탐색이 새로 시작되면(`NavigationStarting`, WebView2 다시 만들기) 그 표시를 풀어 새 화면의 `ready` 를 받음 |
| `launch` | `id` (게임 ID) | ID가 라이브러리에 있으면 `StartGame`. 이미 실행 중이면 `gameState: failed, reason: "busy"` |
| `openWeb` | `id` (웹 바로가기 ID) 또는 `query` (검색어) | 설정의 주소로 Edge 열기 |
| `power` | `action`: `screenOff` · `sleep` · `restart` · `shutdown` · `desktop` | 전원 동작. `desktop` 은 창 숨김 |
| `volume` | `value` 0~100 (정수) | 기본 출력 장치 볼륨 |
| `setSetting` | `key`: `theme` · `idle`, `value` | 설정 파일에 저장 |
| `log` | `level`: `info` · `warn` · `error`, `msg` (500자 안) | 확장 로그에 `[web]` 로 남김 |
| `sfx` | `name`: `navigation` · `activation` | 확장이 효과음을 틂(Playnite 전체화면 기본 테마 파일, 크기는 `settings.json` 의 `sfxVolume`) |

확장은 이 표에 없는 `type`, 범위를 벗어난 값을 **거절하고 로그만 남깁니다**(NFR-17). 라이브러리에 없거나 숨김·미설치인 ID의 `launch` 는 시작하지 않고 `gameState: failed, reason: "error"` 로 답합니다(화면이 '켜는 중'에 갇히지 않게).

확장은 **상태에 따라서도 거절**하고, 이렇게 거절한 메시지에는 답을 보내지 않습니다. `ready`·`log` 를 뺀 메시지는 홈이 보이고 앞 창일 때만 받습니다. 게임이 실행 중이거나 켜지는 중이면 `desktop` 이 아닌 `power` 를 받지 않습니다. 비율도 제한해 전체는 초당 약 50개, `log` 는 초당 약 10개(넘친 수만 한 번 기록), `openWeb` 은 2초에 한 번까지 받고, 이미 `init` 을 보낸 뒤 오는 `ready` 는 무시합니다. `restart`·`shutdown`·`sleep` 은 5초에 한 번까지이고, 3초 안에 실제 입력(컨트롤러 버튼·스틱, 또는 키보드·마우스)이 있었을 때만 받습니다. 이때 '실제 입력'은 XInput 컨트롤러와 키보드·마우스만 세며, `inputMode: gamepadApi` 는 맥 시험용 대체 경로라 그 길로만 들어오는 컨트롤러 입력은 세지 않습니다.

## 4. 확장 → 화면

| type | 필드 | 언제 |
|---|---|---|
| `init` | `version` (확장 판), `settings` {`theme`: `auto`·`light`·`dark`, `idle`: bool, `web`: [웹 바로가기], `launchTimeoutSec`: 수}, `inputMode`: `xinput`·`gamepadApi` | `ready` 를 받으면 |
| `library` | `games`: [게임] | `ready` 뒤, 그리고 목록을 통째로 바꿀 때 |
| `libraryDelta` | `upserts`: [게임], `removes`: [ID] | 일부가 바뀌었을 때. 홈이 숨어 있는 동안 모았다가 `resume` 직전에 한 번 |
| `gameState` | `id`, `state`: `launching`·`started`·`stopped`·`failed`, `reason` (failed일 때: `timeout`·`busy`·`error`), `elapsedSec` (stopped일 때) | 실행 단계가 바뀔 때 |
| `status` | 아래 '상태' | 처음 한 번, 그 뒤 바뀔 때(최소 2초 간격) |
| `pad` | `pads`: [{`index`, `buttons`: [17개 bool], `axes`: [4개 수 −1~1]}] | `inputMode: xinput` 이고 홈이 앞에 있을 때, 상태가 바뀔 때마다(최대 60번/초) |
| `pause` | — | 창을 숨기기 직전. 화면은 그리기·입력 읽기·타이머를 멈춤 |
| `resume` | `reason`: `gameStopped`·`menuHold`·`shortcut`·`wake`·`start`·`webClosed` | 창을 보이고 포커스를 준 직후. 화면은 포커스를 원판 3시로 |
| `toast` | `text` | 확장이 알릴 것이 있을 때(예: "브라우저를 열지 못했어요"). 화면은 `toast` 를 받으면 열려 있던 '웹 여는 중' 창도 닫습니다. busy 알림은 화면이 `gameState` 로 직접 띄우므로 확장은 보내지 않습니다 |

### 게임

```json
{
  "id": "64f78cd1-0b00-4271-a9e1-89abc55f66cc",
  "title": "엘든 링",
  "originalTitle": "ELDEN RING",
  "source": "Steam",
  "genres": ["액션 RPG"],
  "input": "pad",
  "players": "1",
  "playtimeSec": 763200,
  "lastPlayed": "2026-11-04T21:10:00",
  "added": "2026-03-01T12:00:00",
  "cover": "https://orvio-covers.example/64f78cd1-....png?v=3a9f",
  "color": ["#3a3324", "#8c7a4b"]
}
```

| 필드 | 규칙 |
|---|---|
| `title` | Playnite 게임 이름 |
| `originalTitle` | 확장이 처음 본 이름. 지금 이름과 같으면 빠짐 |
| `source` | 라이브러리 출처 이름. 없으면 `"PC"` |
| `input` | 태그 `입력: 컨트롤러`→`pad`, `입력: 키보드·마우스`→`kbm`, `입력: 둘 다`→`both`. 태그가 없으면 기능 정보(Steam 'Full Controller Support'→`pad`, 'Partial Controller Support'→`both`), 그것도 없으면 `both` |
| `players` | 태그 `인원: X` 의 X. 없으면 기능 정보에 멀티·협동·분할 화면이 있으면 `"같이"`, 온라인만 있으면 `"온라인"`, 없으면 `"1"` |
| `lastPlayed` · `added` | 로컬 시각 ISO 문자열, 없으면 `null` |
| `cover` | 정사각으로 잘라 둔 표지. 없으면 `null` (화면이 대체 표지를 그림) |
| `color` | 대체 표지 두 색. ID로 정해지는 무채색 계열 두 색 |

정렬(FR-03)·분류(FR-04)·기록 줄 문구(FR-12)는 **화면이** 합니다. 확장은 값만 줍니다.

### 웹 바로가기

```json
{ "id": "youtube", "title": "유튜브", "url": "https://www.youtube.com", "color": "#ff3d3d" }
```

`id: "search"` 는 웹 검색 칸이고, 주소는 `settings.web` 안 `search` 항목의 `url`(`{q}` 자리에 검색어)입니다.

### 상태

```json
{
  "network": { "kind": "wifi", "strength": 3 },
  "pads": [ { "index": 0, "name": "컨트롤러 1", "battery": { "kind": "level", "value": 2 } } ],
  "kbm": { "keyboard": true, "mouse": false },
  "volume": 40
}
```

| 필드 | 값 |
|---|---|
| `network.kind` | `wifi` · `wired` · `none`. `strength` 0~4 (와이파이만) |
| `pads[].battery.kind` | `percent`(value 0~100) · `level`(value 0~3: 비어 감·낮음·보통·가득) · `wired` · `unknown` |
| `kbm` | 윈도우가 아는 키보드·마우스가 있는지 |
| `volume` | 0~100 |

## 5. 컨트롤러 버튼 번호 (`pad.buttons`)

W3C Gamepad 표준 배치와 같습니다. 0 A · 1 B · 2 X · 3 Y · 4 LB · 5 RB · 6 LT · 7 RT · 8 View · 9 Menu · 10 왼쪽 스틱 누름 · 11 오른쪽 스틱 누름 · 12 위 · 13 아래 · 14 왼쪽 · 15 오른쪽 · 16 가운데(늘 false). `axes`: 0 왼쪽 X · 1 왼쪽 Y · 2 오른쪽 X · 3 오른쪽 Y (아래가 +).

## 6. 판

이 문서의 판은 `1`. 화면은 `ready.version`, 확장은 `init.version` 으로 서로의 판을 알립니다. 맞지 않으면 화면이 `log` 로 경고합니다.

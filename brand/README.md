# ORVIO 브랜드 파일

ORVIO(오르비오) = Orbit(궤도) + 회전.

| 파일 | 쓰는 곳 |
|---|---|
| `orvio-logo-dark.svg` · `orvio-logo-light.svg` | 마크 + 글자. 다크 `#e8e8e8` on `#262626`, 라이트 `#262626` on `#f3f3f3` |
| `orvio-mark-dark.svg` · `orvio-mark-light.svg` | 마크만. 불러오는 중 화면(SCR-09) 등 |
| `orvio-mark-*-accent.svg` | 점만 보라 `#8a5cf5`. 128px 이상 홍보용(README 등)에만. 홈 화면 안에서는 쓰지 않음(보라는 포커스 표시) |
| `orvio-icon.svg` | 앱 아이콘(48px 이상). Playnite 확장 아이콘, 바로가기 |
| `orvio-icon-small.svg` | 32px 이하용. 틈을 넓히고 점을 키우고 판 여백을 줄임 |
| `png/orvio-icon-*.png` · `png/orvio-icon.ico` | 16·20·24·32·40·48·64·128·256(·512) px. ICO는 32 이하에 small 판 |

- 모든 SVG는 칠한 도형(path)만 씁니다. mask·stroke·id가 없어 한 화면에 여러 개를 넣어도, XAML 같은 다른 형식으로 바꿔도 깨지지 않습니다.
- 고리 끝면은 45°이고, 두 틈은 점을 지나는 축 위에 있습니다.

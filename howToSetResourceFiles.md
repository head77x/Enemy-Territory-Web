# 게임 리소스 파일 설정 가이드

이 프로젝트는 Wolfenstein: Enemy Territory의 원본 게임 데이터를 별도로 필요로 합니다.  
소스 코드와 달리 게임 데이터 파일은 GPL 라이선스 대상이 아니며, **원본 EULA의 적용을 받습니다.**  
아래 순서대로 직접 설치 파일에서 추출하거나 복사해서 배치해 주세요.

---

## 목차

1. [원본 게임 데이터 입수 방법](#1-원본-게임-데이터-입수-방법)
2. [리소스 배치 방식 개요](#2-리소스-배치-방식-개요)
3. [런타임 데이터 — StreamingAssets/etmain/](#3-런타임-데이터--streamingassetsetmain)
4. [에디터 임포트 데이터 — Assets/ 내부](#4-에디터-임포트-데이터--assets-내부)
5. [파일 종류별 체크리스트](#5-파일-종류별-체크리스트)
6. [pk3 파일을 통한 일괄 배치 (권장)](#6-pk3-파일을-통한-일괄-배치-권장)
7. [문제 해결](#7-문제-해결)

---

## 1. 원본 게임 데이터 입수 방법

Wolfenstein: Enemy Territory는 **무료로 배포**되는 게임입니다.  
아래 방법 중 하나로 설치 파일을 입수할 수 있습니다.

### 방법 A — 공식 설치 파일 다운로드 (권장)

| 플랫폼 | 다운로드 경로 |
|--------|--------------|
| Windows | Splash Damage 공식 배포 페이지 또는 ModDB 에서 `et260b.exe` 검색 |
| Linux | `et-2.60b.x86.run` 검색 |

설치 후 기본 경로에서 `etmain/` 폴더를 찾습니다.

- **Windows:** `C:\Program Files\Wolfenstein - Enemy Territory\etmain\`
- **Linux:** `~/.etwolf/etmain/` 또는 `/usr/local/games/enemy-territory/etmain/`

### 방법 B — Steam / 패키지 매니저

일부 Linux 배포판에서 패키지로 제공됩니다.

```bash
# Ubuntu/Debian 예시
sudo apt-get install enemy-territory
```

---

## 2. 리소스 배치 방식 개요

이 프로젝트에서 게임 리소스는 **두 가지 경로**로 나뉩니다.

```
UnityProject/
├── Assets/
│   ├── Maps/                  ← (에디터 전용) .bsp 맵 파일
│   ├── Textures/              ← (에디터 전용) .tga/.jpg 텍스처
│   ├── Materials/             ← (에디터 전용) 머티리얼 (선택)
│   ├── Models/                ← (에디터 전용) .md3/.mds 모델
│   └── StreamingAssets/
│       └── etmain/            ← (런타임) 스크립트/사운드/애니메이션 등
│           ├── maps/
│           ├── animations/
│           ├── scripts/
│           ├── sound/
│           ├── models/
│           └── ui/
```

| 구분 | 경로 | 처리 주체 | 설명 |
|------|------|-----------|------|
| 런타임 데이터 | `Assets/StreamingAssets/etmain/` | `FileSystem.FS_ReadFile()` | 게임 실행 중 텍스트/바이너리로 직접 읽음 |
| 에디터 임포트 | `Assets/Maps/`, `Assets/Textures/`, `Assets/Models/` | Unity ScriptedImporter | Play 전 에디터가 Unity 에셋으로 변환 |

> **pk3 파일 지원:** `StreamingAssets/etmain/`에 `.pk3` 파일(ZIP 형식)을 그대로 두면  
> `FileSystem`이 자동으로 인식해서 내부 파일을 직접 읽습니다. 폴더 구조로 풀 필요 없습니다.

---

## 3. 런타임 데이터 — StreamingAssets/etmain/

> 원본 `etmain/` 폴더의 내용을 `UnityProject/Assets/StreamingAssets/etmain/`에 복사합니다.  
> pk3 파일 그대로 복사하거나 압축을 풀어 폴더로 배치해도 됩니다.

### 3-1. 맵 관련

| 복사 경로 (etmain 기준) | 설명 |
|------------------------|------|
| `maps/{맵이름}.tga` | 트레이스맵 — 충돌 계산용 하이트맵. 예: `maps/mp_beach.tga` |
| `maps/{맵이름}.script` | 맵 엔티티 스크립트 — 트리거/이벤트 정의 |
| `maps/{맵이름}_props.script` | 파괴/건설 가능 오브젝트 설정 |

### 3-2. 애니메이션

| 복사 경로 (etmain 기준) | 설명 |
|------------------------|------|
| `animations/groups.txt` | 애니메이션 그룹 정의 파일. 캐릭터 등록 시 반드시 필요 |
| `animations/scripts/*.anim` | 캐릭터별 애니메이션 클립 정의 |
| `animations/scripts/*.script` | 애니메이션 상태 머신 스크립트 |

### 3-3. 게임 설정 스크립트

| 복사 경로 (etmain 기준) | 설명 |
|------------------------|------|
| `scripts/gameinfo.dat` | 게임 전역 설정값 |
| `scripts/arenas.txt` | 플레이 가능한 맵 목록 |
| `scripts/{맵이름}.arena` | 맵별 브리핑/설명 |
| `scripts/{이름}.sscript` | 앰비언트 사운드 스크립트 (`.pk3` 안에 포함되어 있음) |
| `scripts/campaigns/{이름}.campaign` | 캠페인 미션 정의 |
| `ui/{이름}.menu` | UI 메뉴 스크립트 (예: `ui/ingame_main.menu`) |

### 3-4. 봇 AI

| 복사 경로 (etmain 기준) | 설명 |
|------------------------|------|
| `scripts/bots/{이름}.script` | 봇 행동 정의 파일 |

### 3-5. 사운드

| 복사 경로 (etmain 기준) | 설명 |
|------------------------|------|
| `sound/scripts/filelist.txt` | 사운드 스크립트 목록 인덱스 |
| `sound/scripts/{파일명}` | 개별 사운드 스크립트 |
| `sound/player/default/gurp1.wav` | 피격음 (체력 25% 이하) |
| `sound/player/default/gurp2.wav` | 피격음 (체력 50% 이하) |
| `sound/player/default/death1.wav` | 사망음 |
| `sound/player/default/jump1.wav` | 점프음 |
| `sound/player/default/land1.wav` | 착지음 |
| `sound/player/default/drown1.wav` | 입수음 |
| `sound/player/default/gasp1.wav` | 출수음 |
| `sound/misc/referee.wav` | 심판 호루라기 |
| `sound/osp/prepare.wav` | 경기 시작 신호 |
| `sound/items/n_health.wav` | 체력 아이템 픽업음 |
| `sound/misc/am_pkup.wav` | 탄약 픽업음 |
| `sound/misc/w_pkup.wav` | 무기 픽업음 |

### 3-6. 모델 경로 참조 (런타임)

아래 경로는 게임 로직이 `GameItems.cs`에서 참조하는 모델 경로입니다.  
런타임에 경로를 문자열로 보관하며, 실제 메시 로드는 에디터 임포트 데이터를 사용합니다.  
경로가 `Assets/Models/` 내 임포트된 에셋과 일치해야 합니다.

**체력 아이템 (MD3)**
```
models/powerups/health/small_cross.md3
models/powerups/health/medium_cross.md3
models/powerups/health/large_cross.md3
models/powerups/health/mega_cross.md3
```

**탄약 아이템 (MD3) — 12종**
```
models/ammo/9mm/ammo_9mm.md3
models/ammo/45cal/ammo_45cal.md3
models/ammo/792mm/ammo_792mm.md3
models/ammo/30cal/ammo_30cal.md3
models/ammo/panzerfaust/panzerfaust_ammo.md3
models/ammo/flamethrower/flamethrower_ammo.md3
models/ammo/grenade/grenade_ammo.md3
models/ammo/mortar/mortar_ammo.md3
models/ammo/dynamite/dynamite_ammo.md3
models/ammo/landmine/landmine_ammo.md3
models/ammo/satchel/satchel_ammo.md3
models/ammo/mg42/mg42_ammo.md3
```

**무기 드롭 모델 (MD3) — 21종**
```
models/weapons2/knife/knife.md3
models/weapons2/luger/luger.md3
models/weapons2/mp40/mp40.md3
models/weapons2/grenade/grenade.md3
models/weapons2/panzerfaust/pf.md3
models/weapons2/flamethrower/flamethrower.md3
models/weapons2/colt/colt.md3
models/weapons2/thompson/thompson.md3
models/weapons2/sten/sten.md3
models/weapons2/kar98/kar98.md3
models/weapons2/carbine/carbine.md3
models/weapons2/garand/garand.md3
models/weapons2/k43/k43.md3
models/weapons2/fg42/fg42.md3
models/weapons2/mg42/mg42.md3
models/weapons2/mortar/mortar.md3
models/weapons2/dynamite/dynamite.md3
models/weapons2/landmine/landmine.md3
models/weapons2/satchel/satchel.md3
models/weapons2/gpg40/gpg40.md3
models/weapons2/m7/m7.md3
```

---

## 4. 에디터 임포트 데이터 — Assets/ 내부

> Unity 에디터가 직접 처리하는 파일들입니다.  
> 아래 경로에 복사하면 Unity가 자동으로 임포트합니다.  
> **반드시 Play 전에 임포트가 완료되어야 합니다.**

### 4-1. 맵 파일 (.bsp)

`Assets/Maps/` 폴더에 복사합니다.

```
Assets/
└── Maps/
    ├── mp_beach.bsp
    ├── mp_assault.bsp
    └── ...
```

`BspImporter`(ScriptedImporter)가 `.bsp` 확장자를 인식해서 자동으로 Unity `Mesh` + 라이트맵 `Texture2D` 에셋으로 변환합니다.  
변환된 씬 오브젝트는 에디터에서 씬에 드래그 앤 드롭하면 됩니다.

### 4-2. 텍스처 (.tga / .jpg / .png / .dds)

BSP의 셰이더 이름과 경로가 일치해야 합니다.

```
Assets/
└── Textures/
    ├── textures/
    │   ├── egypt_floor/
    │   │   └── sand_01.tga
    │   └── common/
    │       └── clip.tga
    └── ...
```

> **경로 규칙:** BSP 내부의 셰이더 이름이 `textures/egypt_floor/sand_01`이면  
> `Assets/Textures/textures/egypt_floor/sand_01.tga` (또는 `.jpg`, `.png`, `.dds`)로 배치합니다.  
> 없으면 흰색 Standard 머티리얼로 자동 대체됩니다.

### 4-3. 셰이더 파일 (.shader)

`ShaderParser`가 `scripts/` 폴더 안의 `.shader` 파일 전체를 스캔합니다.

```
Assets/
└── StreamingAssets/
    └── etmain/
        └── scripts/
            ├── egypt.shader
            ├── beach.shader
            └── ...
```

> 별도 경로가 필요하지 않습니다. `StreamingAssets/etmain/scripts/`에 `.shader` 파일을 두면  
> 런타임과 에디터 양쪽에서 참조됩니다.

### 4-4. 3D 모델 (.md3 / .mds)

```
Assets/
└── Models/
    ├── models/
    │   ├── weapons2/
    │   │   ├── mp40/
    │   │   │   └── mp40.md3
    │   │   └── ...
    │   └── powerups/
    │       └── health/
    │           └── small_cross.md3
    └── ...
```

- `.md3` — `Md3Importer`가 처리 (캐릭터, 무기, 아이템 등 정적 메시)
- `.mds` — `MdsImporter`가 처리 (캐릭터 스켈레탈 메시)

> `Assets/Models/` 내부 경로는 원본 `etmain/` 경로 구조를 그대로 유지해야  
> `GameItems.cs` 등에서 참조하는 경로와 일치합니다.

---

## 5. 파일 종류별 체크리스트

최소한 아래 항목이 있어야 게임이 정상 시작됩니다.

### 필수 (없으면 시작 시 오류)

- [ ] `StreamingAssets/etmain/animations/groups.txt`
- [ ] `StreamingAssets/etmain/scripts/gameinfo.dat`
- [ ] `StreamingAssets/etmain/sound/scripts/filelist.txt`
- [ ] `StreamingAssets/etmain/sound/player/default/` (7개 wav)

### 맵 플레이에 필요

- [ ] `Assets/Maps/{맵이름}.bsp`
- [ ] `StreamingAssets/etmain/maps/{맵이름}.script`
- [ ] `StreamingAssets/etmain/scripts/{맵이름}.arena`

### 무기/아이템 렌더링에 필요

- [ ] `Assets/Models/models/weapons2/` (21종 md3)
- [ ] `Assets/Models/models/powerups/health/` (4종 md3)
- [ ] `Assets/Models/models/ammo/` (12종 md3)
- [ ] `Assets/Textures/textures/` (BSP 참조 텍스처)

---

## 6. pk3 파일을 통한 일괄 배치 (권장)

원본 ET를 설치하면 `etmain/` 안에 `pak0.pk3` ~ `pak8.pk3` 등 여러 pk3 파일이 있습니다.  
이 파일들은 ZIP 형식이므로 그대로 복사해서 사용할 수 있습니다.

```bash
# 원본 etmain의 pk3 파일들을 그대로 복사
cp /원본설치경로/etmain/*.pk3  ./UnityProject/Assets/StreamingAssets/etmain/
```

`FileSystem.FS_AddGameDirectory()`가 해당 폴더의 `.pk3` 파일을 자동으로 읽습니다.  
pk3 내부에 있는 `maps/`, `scripts/`, `sound/`, `animations/` 등 모든 경로가  
런타임에 투명하게 접근됩니다.

> **주의:** `.bsp`, `.md3`, `.mds` 파일은 Unity 에디터 임포트가 필요하므로,  
> pk3 외에 `Assets/Maps/`, `Assets/Models/`에 별도로 복사해야 합니다.

### 최소 pk3 구성

| 파일 | 포함 내용 |
|------|----------|
| `pak0.pk3` | 기본 텍스처, 사운드, 스크립트 |
| `pak1.pk3` ~ `pak8.pk3` | 추가 맵, 애니메이션, UI |
| `mp_beach.pk3` 등 | 개별 맵 데이터 (맵마다 별도 pk3인 경우) |

---

## 7. 문제 해결

### "[FileSystem] Directory not found" 경고

`StreamingAssets/etmain/` 폴더가 없거나 경로가 잘못된 경우입니다.

```
UnityProject/Assets/StreamingAssets/etmain/   ← 이 폴더가 있는지 확인
```

### "[BgAnimGroup] No tracemap found at maps/..." 경고

해당 맵의 `.tga` 트레이스맵이 없습니다. 충돌 계산이 비활성화되지만 게임은 계속 실행됩니다.  
`StreamingAssets/etmain/maps/{맵이름}.tga`를 복사하면 해결됩니다.

### BSP 임포트 후 텍스처가 흰색으로 표시될 때

BSP 셰이더 이름에 해당하는 텍스처가 `Assets/Textures/`에 없는 경우입니다.  
Unity 에디터 콘솔에 출력되는 셰이더 이름을 확인하고, 해당 `.tga`/`.jpg` 파일을  
`Assets/Textures/{셰이더이름}.tga` 경로로 복사합니다.

### 사운드가 재생되지 않을 때

`sound/scripts/filelist.txt`가 없거나, 해당 파일에 나열된 `.sscript` 파일이  
`StreamingAssets/etmain/sound/scripts/`에 없는 경우입니다.  
원본 pk3에서 `sound/` 폴더를 통째로 추출해서 복사하세요.

### 모델이 보이지 않을 때

`Assets/Models/` 경로가 `GameItems.cs`에 기록된 경로와 다른 경우입니다.  
예를 들어 `models/weapons2/mp40/mp40.md3`이라면  
`Assets/Models/models/weapons2/mp40/mp40.md3`으로 배치해야 합니다.  
원본 경로 구조를 그대로 유지해서 복사하는 것이 가장 안전합니다.

---

## 라이선스 안내

이 프로젝트의 소스 코드는 GPL v3 라이선스입니다.  
그러나 Wolfenstein: Enemy Territory의 **게임 데이터(아트, 사운드, 맵 등)는 GPL 대상이 아니며**,  
원본 EULA의 적용을 받습니다. 게임 데이터를 재배포하거나 상업적으로 사용할 수 없습니다.  
자세한 내용은 `COPYING.txt` 및 원본 EULA를 참고하세요.

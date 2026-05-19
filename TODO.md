# Enemy Territory → Unity3D C# 변환 작업 현황

> 브랜치: `claude/evaluate-webassembly-conversion-etj1D`
> 원본 소스: GPL 공개 소스 (Wolfenstein: Enemy Territory, ~473,000 줄 C/C++)

---

## 완료된 작업

### Layer 1 — 핵심 기반 모듈 ✅
커밋: `[Layer 1] Add Unity C# core foundation modules`

| C# 파일 | 원본 C 파일 | 설명 |
|---------|-----------|------|
| `Core/ETMath.cs` | `src/game/q_math.c` | 벡터/각도/평면/경계 수학, ByteDirs(162), ColorTable(32) |
| `Core/ETShared.cs` | `src/game/q_shared.c` | 문자열 유틸, Info 스트링, 파싱 세션, 비트 배열 |
| `Core/Huffman.cs` | `src/qcommon/huffman.c` | 적응형 허프만 압축 (Sayood 알고리즘) |
| `Core/MD4.cs` | `src/qcommon/md4.c` | MD4 해시 — 맵/PK3 무결성 검증, challenge-response |

### Layer 2 — 네트워크 레이어 ✅
커밋: `[Layer 2] Add Unity C# network layer`

| C# 파일 | 원본 C 파일 | 설명 |
|---------|-----------|------|
| `Network/NetField.cs` | `src/qcommon/msg.c` | 제네릭 `NetField<T>` — C의 오프셋 해킹을 람다로 대체 |
| `Network/NetAddress.cs` | `src/qcommon/qcommon.h` | `netadr_t` + IPEndPoint 변환 |
| `Network/UserCmd.cs` | `src/game/q_shared.h` | `usercmd_t` + Button/WButton 상수 + DoubleTap 열거형 |
| `Network/EntityState.cs` | `src/game/q_shared.h` + msg.c | `entityState_t` + `trajectory_t` + NetFields[67] |
| `Network/PlayerState.cs` | `src/game/q_shared.h` + msg.c | `playerState_t` + NetFields[74] |
| `Network/NetMessage.cs` | `src/qcommon/msg.c` | 허프만 비트스트림 I/O, 델타 인코딩, OOB 모드 |
| `Network/NetChannel.cs` | `src/qcommon/net_chan.c` | UDP 채널 — 단편화/시퀀싱/루프백 |

### Layer 3 — 플레이어 물리 + 무기 상태 머신 ✅

| C# 파일 | 원본 C 파일 | 설명 |
|---------|-----------|------|
| `Game/GameConst.cs` | `bg_public.h`, `q_shared.h` | 모든 게임 상수 (WP_*, EF_*, PMF_*, 등) — 완전 교정 |
| `Game/GameEvents.cs` | `bg_public.h` | EV_* 이벤트 상수 전부 |
| `Game/SurfaceFlags.cs` | `q_shared.h` | Contents.* + Surf.* 플래그 |
| `Game/TraceResult.cs` | `q_shared.h` | `trace_t` → TraceResult struct |
| `Game/PmoveInput.cs` | `bg_pmove.h` | PmoveInput, PmoveLocal, 델리게이트 |
| `Game/PmoveExt.cs` | `bg_pmove.c` | PmoveExt 헬퍼 |
| `Game/SlideMove.cs` | `bg_slidemove.c` | PM_ClipVelocity, PM_SlideMove, PM_StepSlideMove |
| `Game/PlayerMovement.cs` | `bg_pmove.c` (5,709줄) | 전체 플레이어 물리 + PM_Weapon 완전 포팅 |
| `Game/CvarSystem.cs` | `cvar.c` | 경량 Cvar 레지스트리 |
| `Game/GameMisc.cs` | `bg_misc.c` | BG_ 유틸리티 함수 (PlayerStateToEntityState 등) |

### Layer 4 — 서버 핵심 ✅

| C# 파일 | 원본 C 파일 | 설명 |
|---------|-----------|------|
| `Server/ServerTypes.cs` | `sv_*.h` | ServerConst, 열거형, ServerClient, 상태 타입 |
| `Server/ServerMain.cs` | `sv_main.c` | SV_Frame, configstring, 클라이언트 메시지 전송 |
| `Server/ServerInit.cs` | `sv_init.c` | SV_Init/SpawnServer/Startup, PROTOCOL_VERSION=84 |
| `Server/ServerSnapshot.cs` | `sv_snapshot.c` | 스냅샷 빌드, 델타 엔티티 전송, SVC_* 옵코드 |
| `Server/ServerClient.cs` | `sv_client.c` | 연결/해제, usercmd 처리, 신뢰성 명령 |
| `Server/ServerWorld.cs` | `sv_world.c` | AABB 공간 쿼리, swept-AABB trace |
| `Server/ServerGame.cs` | `sv_game.c` | 게임 VM 브릿지, 서버↔게임 이벤트 |

---

## 남은 작업

### 우선순위 기준 레이어

```
Layer 5: 클라이언트 핵심   ← 현재 진행 중
Layer 6: 렌더러 인터페이스 (BSP 임포터)
Layer 7: 봇 AI
Layer 8: 통합 / 실행 가능한 데모
```

---

### Layer 5 — 클라이언트 핵심 🔲 (진행 중)

| 대상 C# 파일 | 원본 C 파일 | 설명 |
|------------|-----------|------|
| `Client/ClientTypes.cs` | `client.h` | 클라이언트 상태 타입 (in progress) |
| `Client/ClientMain.cs` | `cl_main.c` | 클라이언트 초기화/루프/연결 관리 |
| `Client/ClientInput.cs` | `cl_input.c` | 입력 수집 + UserCmd 생성 |
| `Client/ClientParse.cs` | `cl_parse.c` | 서버 메시지 파싱 (스냅샷, 게임스테이트) |
| `Client/ClientCGame.cs` | `cl_cgame.c` | cgame 인터페이스 (이벤트 기반 스텁) |
| `Client/ClientNetChan.cs` | `cl_net_chan.c` | 클라이언트 측 netchan 래퍼 |

---

### Layer 6 — 렌더러 인터페이스 (Unity 전환)

원본 OpenGL 렌더러는 Unity URP/HDRP로 완전 대체.

| 대상 | 설명 | Unity 대체 |
|------|------|-----------|
| `Renderer/BspImporter.cs` | ET BSP 맵 → Unity Mesh/GameObject | Editor 전용 AssetImporter |
| `Renderer/Md3Importer.cs` | MD3 모델 임포터 | Editor 전용 |
| `Renderer/MdsImporter.cs` | MDS 스켈레탈 모델 임포터 | Editor + Animator |
| `Renderer/ShaderParser.cs` | ET .shader 파일 → Unity Material | Editor 전용 |
| `Renderer/SkinParser.cs` | .skin 파일 파서 | Runtime |

**BSP 임포터 핵심 작업 (`src/renderer/tr_bsp.c`):**
- Lump 파싱: 엔티티/셰이더/평면/노드/리프/면/표면
- SOLID_BMODEL 인라인 모델
- 패치 곡면 → Unity Mesh (Bezier 테셀레이션)
- 라이트맵 → Unity Texture2D (RGB 리스케일 필요)
- 가시성 PVS 데이터 → Occlusion Culling 또는 Unity Portals

---

### Layer 7 — 봇 AI

| 대상 C# 파일 | 원본 | 설명 |
|------------|------|------|
| `BotAI/BotMain.cs` | `src/botai/`, `src/botlib/` | 봇 프레임 루프 |
| `BotAI/PathFinding.cs` | AAS 시스템 | Unity NavMesh로 대체 가능 |
| `BotAI/BotChat.cs` | `src/game/g_bot.c` | 채팅/명령 처리 |

> AAS (Area Awareness System) 포팅은 Unity NavMesh 사용으로 대부분 생략 가능.

---

### 미결 기술 항목

| 항목 | 현황 | 해결 방법 |
|------|------|---------|
| Cmd 시스템 (cmd.c) | 미작업 | 콘솔 명령 라우터 구현 필요 |
| 파일 시스템 (files.c) | 미작업 | PK3(ZIP) 로드 → `System.IO.Compression` |
| 오디오 (snd_*.c) | 미작업 | Unity Audio System으로 완전 대체 |
| 충돌 감지 (cm_*.c) | 미작업 | BSP 충돌 → Unity PhysX 혼용 |
| WeaponSystem.cs (g_weapon.c) | 미작업 | 서버 측 무기 데미지 처리 |
| DamageSystem.cs (g_combat.c) | 미작업 | 서버 측 데미지/죽음 처리 |
| Animations.cs (bg_animation.c) | 미작업 | Layer 6 애니메이션 스크립트 |

---

## 변환 진행률

```
전체 원본 C 소스: ~473,000줄 (456 파일)
────────────────────────────────────────────
Layer 1 완료:  ~4,000줄  (q_math.c, q_shared.c, huffman.c, md4.c)
Layer 2 완료:  ~3,300줄  (msg.c, net_chan.c, netField 타입)
Layer 3 완료:  ~8,000줄  (bg_pmove.c, bg_slidemove.c, bg_misc.c, cvar.c)
Layer 4 완료:  ~5,000줄  (sv_main.c, sv_client.c, sv_snapshot.c, sv_game.c, sv_world.c, sv_init.c)
────────────────────────────────────────────
현재까지 포팅: ~20,300줄  (약 4.3%)
Layer 5 진행: cl_main.c, cl_input.c, cl_parse.c, cl_cgame.c
렌더러 교체:  Unity로 완전 대체 (tr_*.c 직접 포팅 불필요)
봇 AI:       Unity NavMesh 부분 대체 가능
```

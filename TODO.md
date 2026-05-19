# Enemy Territory → Unity3D C# 변환 작업 현황

> 브랜치: `claude/evaluate-webassembly-conversion-etj1D`
> 원본 소스: GPL 공개 소스 (Wolfenstein: Enemy Territory, ~473,000 줄 C/C++)

---

## 완료된 작업

### Layer 1 — 핵심 기반 모듈 ✅

| C# 파일 | 원본 C 파일 | 설명 |
|---------|-----------|------|
| `Core/ETMath.cs` | `q_math.c` | 벡터/각도/평면/경계 수학, ByteDirs(162), ColorTable(32) |
| `Core/ETShared.cs` | `q_shared.c` | 문자열 유틸, Info 스트링, 파싱 세션, 비트 배열 |
| `Core/Huffman.cs` | `huffman.c` | 적응형 허프만 압축 (Sayood 알고리즘) |
| `Core/MD4.cs` | `md4.c` | MD4 해시 — 맵/PK3 무결성 검증, challenge-response |
| `Core/CmdSystem.cs` | `cmd.c` | 콘솔 명령 레지스트리 + 디퍼드 버퍼 (Cbuf) |
| `Core/FileSystem.cs` | `files.c` | 가상 파일시스템 — PK3(ZIP) + 루즈 파일 레이어드 검색 |

### Layer 2 — 네트워크 레이어 ✅

| C# 파일 | 원본 C 파일 | 설명 |
|---------|-----------|------|
| `Network/NetField.cs` | `msg.c` | 제네릭 `NetField<T>` — C의 오프셋 해킹을 람다로 대체 |
| `Network/NetAddress.cs` | `qcommon.h` | `netadr_t` + IPEndPoint 변환 |
| `Network/UserCmd.cs` | `q_shared.h` | `usercmd_t` + Button/WButton 상수 + DoubleTap 열거형 |
| `Network/EntityState.cs` | `q_shared.h` + `msg.c` | `entityState_t` + `trajectory_t` + NetFields[67] |
| `Network/PlayerState.cs` | `q_shared.h` + `msg.c` | `playerState_t` + NetFields[74] |
| `Network/NetMessage.cs` | `msg.c` | 허프만 비트스트림 I/O, 델타 인코딩, OOB 모드 |
| `Network/NetChannel.cs` | `net_chan.c` | UDP 채널 — 단편화/시퀀싱/루프백 |

### Layer 3 — 플레이어 물리 + 무기 + 게임 로직 ✅

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
| `Game/GameMisc.cs` | `bg_misc.c` | BG_ 유틸리티 함수 |
| `Game/WeaponSystem.cs` | `g_weapon.c` | 서버 측 무기 발사, 데미지 디스패치, 50개 무기 테이블 |
| `Game/DamageSystem.cs` | `g_combat.c` | 데미지 해결, 사망 처리, XP 시스템, 출혈 타이머 |
| `Game/GameItems.cs` | `g_items.c` | 아이템 픽업, 탄약 보급, 무기 드롭, 아이템 테이블 |

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

### Layer 5 — 클라이언트 핵심 ✅

| C# 파일 | 원본 C 파일 | 설명 |
|---------|-----------|------|
| `Client/ClientTypes.cs` | `client.h` | 연결 상태, 스냅샷 링, 명령 링 타입 |
| `Client/ClientMain.cs` | `cl_main.c` | CL_Frame, 연결/challenge 관리, 패킷 전송 |
| `Client/ClientInput.cs` | `cl_input.c` | 입력 수집 + UserCmd 생성 (ANGLE2SHORT) |
| `Client/ClientParse.cs` | `cl_parse.c` | 스냅샷/게임스테이트/서버 명령 파싱 |
| `Client/ClientCGame.cs` | `cl_cgame.c` | cgame 인터페이스 — VM_Call → C# 이벤트 |
| `Client/ClientNetChan.cs` | `cl_net_chan.c` | 클라이언트 측 netchan (단편화 우선순위) |

### Layer 6 — 렌더러 인터페이스 ✅

| C# 파일 | 원본 | 설명 |
|---------|------|------|
| `Renderer/BspImporter.cs` | `tr_bsp.c` | ET BSP v46 파서 + Unity ScriptedImporter, 패치 테셀레이션 |
| `Renderer/ShaderParser.cs` | `tr_shader.c` | .shader 파일 파서 → EtShader → Unity Material |
| `Renderer/Md3Importer.cs` | `tr_mesh.c` | MD3 정적 메시 + BlendShape 애니메이션 |
| `Renderer/MdsImporter.cs` | `tr_animation.c` | MDS 스켈레탈 메시 + SkinnedMeshRenderer + Avatar |

### Layer 7 — 봇 AI ✅

| C# 파일 | 원본 | 설명 |
|---------|------|------|
| `BotAI/BotMain.cs` | `g_bot.c`, `ai_main.c` | 봇 상태 머신, NavMesh 이동, UserCmd 생성 |
| `BotAI/BotChat.cs` | `ai_chat.c` | 음성 명령 처리, 채팅 응답 테이블 |
| `BotAI/PathFinding.cs` | `be_aas_*.c` | AAS → Unity NavMesh 브릿지, 경로 계획 API |

---

## 변환 진행률

```
전체 원본 C 소스: ~473,000줄 (456 파일)
────────────────────────────────────────────
Layer 1 완료:  ~4,500줄  (q_math, q_shared, huffman, md4, cmd, files)
Layer 2 완료:  ~3,300줄  (msg, net_chan, netField 타입)
Layer 3 완료:  ~14,000줄 (bg_pmove, bg_slidemove, bg_misc, cvar, g_weapon, g_combat, g_items)
Layer 4 완료:  ~5,000줄  (sv_main, sv_client, sv_snapshot, sv_game, sv_world, sv_init)
Layer 5 완료:  ~2,500줄  (cl_main, cl_input, cl_parse, cl_cgame, cl_net_chan)
Layer 6 완료:  ~4,000줄  (tr_bsp, tr_shader, tr_mesh, tr_animation)
Layer 7 완료:  ~2,000줄  (g_bot, ai_main, ai_chat, be_aas_*)
────────────────────────────────────────────
현재까지 포팅: ~35,300줄  (약 7.5%)
렌더러 교체:   Unity로 완전 대체 (tr_*.c 직접 포팅 불필요 — OpenGL → URP)
봇 AI:        AAS → Unity NavMesh 대체 완료
```

---

## 남은 주요 작업

| 항목 | 현황 | 메모 |
|------|------|------|
| `bg_animation.c` 포팅 | 미작업 | 애니메이션 스크립트 시스템 (레이어 3b) |
| `cm_*.c` 충돌 감지 | 미작업 | BSP 충돌 → Unity PhysX 혼용 |
| 오디오 (`snd_*.c`) | 미작업 | Unity Audio System으로 완전 대체 |
| 통합 테스트 씬 | 미작업 | MonoBehaviour로 레이어 연결, 기본 플레이 확인 |
| WeaponSystem → DamageSystem 구독 | 미작업 | `DamageSystem` static 생성자 자동 구독 |
| MDS 프레임 압축 해제 | 미작업 | `mdsBoneFrameCompressed_t` 5-short 디코더 (TODO 주석) |

---

## Unity 프로젝트 설정 가이드

```
권장 버전: Unity 6 (6000.0.x LTS) 또는 Unity 2022.3 LTS
렌더 파이프라인: Universal Render Pipeline (URP)
API 호환성: .NET Standard 2.1
```

Assembly definition 체인:
```
ET.Core → ET.Network → ET.Game → ET.Server
                               → ET.Client
                               → ET.BotAI
```

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
| `Game/Animations.cs` | `bg_animation.c` | 데이터 기반 애니메이션 스크립트 시스템 — AnimMoveType/Condition/WeaponClass, BG_ParseAnimationFile, BG_AnimScriptEvent |
| `Game/CollisionSystem.cs` | `cm_*.c` | BSP 충돌 → Unity PhysX 백엔드 — CM_BoxTrace, CM_PointContents, DefaultTraceFunc |
| `Game/AudioSystem.cs` | `snd_*.c` | Unity AudioSource 풀(32 채널) — S_RegisterSound, S_StartSound, S_Respatialize |
| `Game/ETGameManager.cs` | (통합) | MonoBehaviour 통합 씬 — 모든 레이어 연결, SV_Frame→CL_Frame→BotAI 루프 |

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
| `Renderer/MdsImporter.cs` | `tr_animation.c` | MDS 스켈레탈 메시 + SkinnedMeshRenderer + Avatar + mdsBoneFrameCompressed_t 디코더 |

### Layer 7 — 봇 AI ✅

| C# 파일 | 원본 | 설명 |
|---------|------|------|
| `BotAI/BotMain.cs` | `g_bot.c`, `ai_main.c` | 봇 상태 머신, NavMesh 이동, UserCmd 생성 |
| `BotAI/BotChat.cs` | `ai_chat.c` | 음성 명령 처리, 채팅 응답 테이블 |
| `BotAI/PathFinding.cs` | `be_aas_*.c` | AAS → Unity NavMesh 브릿지, 경로 계획 API |

### Layer 9 — 서버 게임 로직 ✅

| C# 파일 | 원본 C 파일 | 설명 |
|---------|-----------|------|
| `Server/ServerGameLogic.cs` | `g_main.c`, `g_active.c`, `g_client.c`, `g_spawn.c` | GEntity/GClient, G_InitGame/RunFrame, ClientConnect/Spawn/Disconnect |
| `Server/MissileSystem.cs` | `g_missile.c` | 8종 투사체 발사, G_RunMissile, BulletFire hitscan, G_RadiusDamage |
| `Server/MoverSystem.cs` | `g_mover.c` | MoverState, 8종 SP_func_* (door/plat/train/rotating/button/explosive) |
| `Server/GameScript.cs` | `g_script.c`, `g_script_actions.c` | .script 파서, 14개 액션 핸들러, ScriptEventType |
| `Server/GameCommands.cs` | `g_cmds.c`, `g_misc.c` | 15개 클라이언트 명령, 투표/채팅, 21개 엔티티 스폰 함수 |
| `Server/GameMatch.cs` | `g_match.c`, `g_session.c`, `g_stats.c`, `g_fireteams.c`, `g_antilag.c` | 매치/세션/스탯/화력팀(8팀×6원)/안티랙 히스토리 |
| `Server/GameEntities.cs` | `g_target.c`, `g_trigger.c`, `g_utils.c`, `g_alarm.c` | G_Find/UseTargets, 12개 target_*, 11개 trigger_*, 8존 알람 시스템 |
| `Server/GameTeam.cs` | `g_team.c`, `g_vote.c`, `g_referee.c`, `g_teammapdata.c`, `g_multiview.c` | 팀 관리, 투표 시스템(30s/50%+1), 심판, 목표 상태, 스폰 웨이브 |
| `Server/GameProps.cs` | `g_props.c`, `g_svcmds.c`, `bg_tracemap.c`, `bg_animgroup.c`, `bg_sscript.c` | 건설/파괴 오브젝트, 서버 콘솔 명령, 높이맵, 사운드 스크립트 |

### Layer 10 — 클라이언트 게임(cgame) ✅

| C# 파일 | 원본 C 파일 | 설명 |
|---------|-----------|------|
| `Client/CGameMain.cs` | `cg_main.c`, `cg_snapshot.c`, `cg_predict.c` | CGameState/CGameSharedState, 스냅샷 처리, pmove 클라이언트 예측 |
| `Client/CGameEntities.cs` | `cg_ents.c`, `cg_players.c`, `cg_event.c` | CentityState, 엔티티 보간, 플레이어 애니메이션, 12개 이벤트 핸들러 |
| `Client/CGameView.cs` | `cg_view.c`, `cg_draw.c`, `cg_marks.c`, `cg_localents.c` | 카메라/FOV/줌, HUD 시스템, 데칼 풀(500), 로컬 엔티티 풀(512) |
| `Client/CGameWeapons.cs` | `cg_weapons.c`, `cg_effects.c` | WeaponRenderInfo[64], 머즐 플래시, 8종 폭발 ParticleSystem 효과 |
| `Client/CGameAtmospheric.cs` | `cg_atmospheric.c`, `cg_particles.c`, `cg_trails.c` | 비/눈 ParticleSystem, 4096 파티클 풀, 128 LineRenderer 트레일 시스템 |
| `Client/CGameFireteams.cs` | `cg_fireteams.c`, `cg_fireteamoverlay.c`, `cg_commandmap.c`, `cg_consolecmds.c` | 화력팀 UI, 커맨드맵 아이콘, 콘솔 명령 등록 |
| `Client/CGameUI.cs` | `cg_scoreboard.c`, `cg_limbopanel.c`, `cg_debriefing.c`, `cg_popupmessages.c` | 스코어보드, 림보패널(사망 후 스폰 선택), 디브리핑, 팝업 10-slot 링 버퍼 |
| `Client/CGameServerCmds.cs` | `cg_servercmds.c`, `cg_playerstate.c`, `cg_spawn.c`, `cg_flamethrower.c`, `cg_multiview.c` | 서버 명령 디스패치, 데미지 피드백, 화염방사기 파티클, 4분할 관전 |
| `Client/CGameExtra.cs` | `cg_character.c`, `cg_info.c`, `cg_window.c`, `cg_newDraw.c`, `cg_drawtools.c`, `cg_statsranksmedals.c` | 캐릭터/스킨, 로딩 화면, 4종 창 시스템, HUD 드로 툴, 훈장 표시 |

### Layer 11 — UI 시스템 ✅

| C# 파일 | 원본 C 파일 | 설명 |
|---------|-----------|------|
| `Client/UISystem.cs` | `ui_main.c`, `ui_shared.c`, `ui_atoms.c`, `ui_players.c`, `ui_gameinfo.c` | Stack 메뉴 내비게이션, .menu 스크립트 파서, 서버 브라우저, 아레나 정보 |

### Layer 12 — 나머지 소규모 파일 ✅

| C# 파일 | 원본 C 파일 | 설명 |
|---------|-----------|------|
| `Server/GameCommandsExt.cs` | `g_cmds_ext.c` | OSP 확장 명령 (lock/unlock/pause/ready/topshots/bottomshots/specinvite/speclock/players 등 25개) + 무기 정확도 랭킹 |
| `Server/GameCharacterConfig.cs` | `g_character.c`, `g_config.c`, `g_mem.c`, `g_save.c` | 캐릭터 등록/업데이트, comp/pub 서버 프리셋 cvar 테이블, GameMemory 스텁 (g_save는 #ifdef SAVEGAME_SUPPORT로 원본 미사용) |
| `Client/CGamePanels.cs` | `cg_panelhandling.c`, `cg_missionbriefing.c` | PanelButton 시스템, .campaign/.arena 파서, CampaignInfo/ArenaInfo, 미션 브리핑 이벤트 |
| `Client/CGameSound.cs` | `cg_sound.c` | cgame 사운드 스크립트 시스템 (4096 스크립트 / 8192 사운드), 해시 테이블 조회, 버퍼드 큐, ScriptSpeaker 풀, AudioSystem 위임 |

### Layer 13 — qcommon/서버/클라이언트/봇AI 잔여 파일 ✅

| C# 파일 | 원본 C 파일 | 설명 |
|---------|-----------|------|
| `Core/CommonSystem.cs` | `qcommon/common.c` | Com_Init/Frame/Error/Printf/DPrintf, 이벤트 루프, 커맨드라인 파싱, 설정 I/O, HashKey/Filter/StringContains, ModifyMsec, Quit/Shutdown |
| `Server/GameServerUtils.cs` | `g_buddy_list.c`, `g_sv_entities.c`, `g_systemmsg.c` | WaypointSystem (G_SetWayPoint/ClearWayPoint), ServerEntityPool (MAX_SERVER_ENTITIES=4096, Init/Alloc/Get/Find), SystemMessageSystem (11가지 메시지, 15000ms 팀별 쿨다운, G_NeedEngineers/CheckForNeededClasses/CheckMenDown) |
| `Server/ServerConsoleCommands.cs` | `sv_ccmds.c`, `sv_bot.c`, `sv_net_chan.c` | ServerConsoleCommands (14개 오퍼레이터 명령, SV_TransitionGameState 상태 머신), ServerBotIntegration (AllocateBotClient/FreeClient/InPVS/IsBot), ServerNetChan (XOR 인코딩/디코딩, SV_ENCODE_START=4, SV_DECODE_START=12) |
| `Client/ClientSystem.cs` | `cl_keys.c`, `cl_console.c`, `cl_scrn.c`, `cl_ui.c` | KeySystem (ETKey 320키, SetBinding/GetBinding/OnKeyEvent, WriteBindings), ClientConsole (32768자 링버퍼, 32개 히스토리, 슬라이드 애니메이션), ClientScreen (AdjustFrom640, FillRect/DrawNamedPic 이벤트, DebugGraph 1024샘플), ClientUI (LAN/글로벌/즐겨찾기 서버 브라우저, PlayerPrefs 캐시) |
| `Client/CinematicSystem.cs` | `cl_cin.c`, `cg_loadpanel.c`, `ui_loadpanel.c`, `ui_util.c` | CinematicSystem (MAX_VIDEO_HANDLES=16, VideoPlayer+RenderTexture, CIN_Play/Run/Stop/Draw/SetExtents/SetLooping), LoadPanel (LoadPanelData, UpdateProgress/SetMapName/SetCampaignData, OnRender 이벤트) |
| `BotAI/BotTeamAI.cs` | `ai_team.c`, `ai_cmd.c`, `ai_script.c`, `ai_script_actions.c` | BotScriptSystem (.script 파일 파서, 이벤트/액션 체인, accum/if/setweapon/movetomarker), BotTeamAI (유효 리더 탐색, NavMesh 이동시간 정렬, 폭발물/건설 목표), BotCommands (AddressedToBot, MatchHelp/Camp/Patrol/GetItem/RushBase) |
| `BotAI/BotCombatAI.cs` | `ai_dmgoal_mp.c`, `ai_dmnet_mp.c`, `ai_dmq3.c` | BotState (40개 필드), BotGoalType (16종), BotAIState (17상태), BotGoalSystem (클래스별 우선순위 테이블, FindGoal/CheckClassActions, FindNearestEnemy/FallenTeammate), BotStateMachine (17개 Node_* 핸들러), BotCombat (ChooseWeapon/OptimalRange/FaceTarget/UpdateInventory) |

---

## 변환 진행률

```
전체 원본 C 소스: ~473,000줄 (456 파일)       C# 파일: 81개, ~52,000줄
────────────────────────────────────────────────────────────────────
Layer 1  완료:  ~4,500줄  (q_math, q_shared, huffman, md4, cmd, files)
Layer 2  완료:  ~3,300줄  (msg, net_chan, netField 타입)
Layer 3  완료:  ~16,400줄 (bg_pmove, bg_slidemove, bg_misc, cvar, g_weapon,
                            g_combat, g_items, bg_animation, cm_*.c, snd_*.c, 통합씬)
Layer 4  완료:  ~5,000줄  (sv_main, sv_client, sv_snapshot, sv_game, sv_world, sv_init)
Layer 5  완료:  ~2,500줄  (cl_main, cl_input, cl_parse, cl_cgame, cl_net_chan)
Layer 6  완료:  ~4,000줄  (tr_bsp, tr_shader, tr_mesh, tr_animation)
Layer 7  완료:  ~2,000줄  (g_bot, ai_main, ai_chat, be_aas_*)
Layer 9  완료:  ~6,200줄  (g_main, g_active, g_client, g_spawn, g_missile, g_mover,
                            g_script+actions, g_cmds, g_misc, g_match, g_session,
                            g_stats, g_fireteams, g_antilag, g_target, g_trigger,
                            g_utils, g_alarm, g_team, g_vote, g_referee,
                            g_teammapdata, g_multiview, g_props, g_svcmds,
                            bg_tracemap, bg_animgroup, bg_sscript)
Layer 10 완료:  ~6,400줄  (cg_main, cg_snapshot, cg_predict, cg_ents, cg_players,
                            cg_event, cg_view, cg_draw, cg_marks, cg_localents,
                            cg_weapons, cg_effects, cg_atmospheric, cg_particles,
                            cg_trails, cg_fireteams, cg_fireteamoverlay,
                            cg_commandmap, cg_consolecmds, cg_scoreboard,
                            cg_limbopanel, cg_debriefing, cg_popupmessages,
                            cg_servercmds, cg_playerstate, cg_spawn,
                            cg_flamethrower, cg_multiview, cg_character,
                            cg_info, cg_window, cg_newDraw, cg_drawtools,
                            cg_statsranksmedals)
Layer 11 완료:  ~700줄   (ui_main, ui_shared, ui_atoms, ui_players, ui_gameinfo)
Layer 12 완료:  ~4,400줄  (g_cmds_ext, g_character, g_config, g_mem, g_save(stub),
                            cg_panelhandling, cg_missionbriefing, cg_sound)
Layer 13 완료:  ~9,000줄  (common.c, g_buddy_list, g_sv_entities, g_systemmsg,
                            sv_ccmds, sv_bot, sv_net_chan, cl_keys, cl_console,
                            cl_scrn, cl_ui, cl_cin, cg_loadpanel, ui_loadpanel,
                            ui_util, ai_team, ai_cmd, ai_script, ai_script_actions,
                            ai_dmgoal_mp, ai_dmnet_mp, ai_dmq3)
────────────────────────────────────────────────────────────────────
현재까지 포팅: ~64,400줄  (약 13.6%)
렌더러 교체:   Unity URP로 완전 대체 (OpenGL → URP, tr_*.c 불필요)
봇 AI:        AAS → Unity NavMesh 대체 완료
충돌 감지:    cm_*.c → Unity PhysX 대체 완료
오디오:       snd_*.c → Unity AudioSource 풀 대체 완료
영상 재생:    ROQ 코덱 → Unity VideoPlayer + RenderTexture 대체 완료
```

---

## 남은 작업

| 항목 | 원본 | 메모 |
|------|------|------|
| `cg_polybus.c` | ~93줄 | 폴리곤 버스 (렌더러 대체로 불필요) |
| `ui_syscalls.c` / `cg_syscalls.c` | syscall 브릿지 | Unity에서 직접 호출로 대체 완료 |

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

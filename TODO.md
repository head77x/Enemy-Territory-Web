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

---

## 남은 작업

### 우선순위 기준 레이어

```
Layer 3: 플레이어 물리 / 게임 로직   ← 다음 작업
Layer 4: 서버 핵심
Layer 5: 클라이언트 핵심
Layer 6: 렌더러 인터페이스 (BSP 임포터)
Layer 7: 봇 AI
Layer 8: 통합 / 실행 가능한 데모
```

---

### Layer 3 — 플레이어 물리 및 게임 로직

| 대상 C# 파일 | 원본 C 파일 | 크기 | 우선순위 |
|------------|-----------|------|---------|
| `Game/PlayerMovement.cs` | `src/game/bg_pmove.c` | 5,709줄 | ★★★ 최고 |
| `Game/SlideMove.cs` | `src/game/bg_slidemove.c` | 403줄 | ★★★ |
| `Game/WeaponSystem.cs` | `src/game/g_weapon.c` | 4,285줄 | ★★★ |
| `Game/DamageSystem.cs` | `src/game/g_combat.c` | 2,094줄 | ★★ |
| `Game/GameItems.cs` | `src/game/g_items.c` | ~1,800줄 | ★★ |
| `Game/GameMisc.cs` | `src/game/bg_misc.c` | ~2,000줄 | ★★ |
| `Game/Animations.cs` | `src/game/bg_animation.c` | ~1,500줄 | ★ |

**bg_pmove.c 포팅 시 주의사항:**
- ET 좌표계: X=앞, Y=왼, Z=위 (오른손 Z-up)
- Unity 좌표계: X=오른, Y=위, Z=앞 (왼손 Y-up)
- pmove 계산은 ET 내부 좌표로 유지, 렌더링 경계에서만 변환
- `trace_t` 콜백 함수는 Unity PhysX의 `Physics.CapsuleCast`로 대체
- `bg_slidemove.c`는 `bg_pmove.c`보다 먼저 포팅 필요

---

### Layer 4 — 서버 핵심

| 대상 C# 파일 | 원본 C 파일 | 설명 |
|------------|-----------|------|
| `Server/ServerMain.cs` | `src/server/sv_main.c` | 서버 초기화/프레임 루프 |
| `Server/ServerClient.cs` | `src/server/sv_client.c` | 클라이언트 연결 관리 |
| `Server/ServerSnapshot.cs` | `src/server/sv_snapshot.c` | 스냅샷 생성/전송 |
| `Server/ServerGame.cs` | `src/server/sv_game.c` | 게임 VM 인터페이스 |
| `Server/ServerWorld.cs` | `src/server/sv_world.c` | 공간 분할 쿼리 |
| `Server/ServerInit.cs` | `src/server/sv_init.c` | 게임스테이트 초기화 |

---

### Layer 5 — 클라이언트 핵심

| 대상 C# 파일 | 원본 C 파일 | 설명 |
|------------|-----------|------|
| `Client/ClientMain.cs` | `src/client/cl_main.c` | 클라이언트 초기화/루프 |
| `Client/ClientInput.cs` | `src/client/cl_input.c` | Unity Input System으로 대체 |
| `Client/ClientParse.cs` | `src/client/cl_parse.c` | 서버 메시지 파싱 |
| `Client/ClientCGame.cs` | `src/client/cl_cgame.c` | cgame 인터페이스 |
| `Client/ClientNetChan.cs` | `src/client/cl_net_chan.c` | 클라이언트 측 netchan |

---

### Layer 6 — 렌더러 인터페이스 (Unity 전환)

원본 OpenGL 렌더러는 Unity URP/HDRP로 완전 대체.
**에디터 임포터**와 **런타임 시스템** 두 부분으로 나뉨.

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
| VM 시스템 (vm.c, vm_x86.c) | 미작업 | 모드 지원 불필요 시 제거; 필요 시 인터프리터만 포팅 |
| Cvar 시스템 (cvar.c) | 미작업 | 경량 `CvarSystem.cs` 구현 필요 |
| Cmd 시스템 (cmd.c) | 미작업 | 콘솔 명령 라우터 구현 필요 |
| 파일 시스템 (files.c) | 미작업 | PK3(ZIP) 로드 → `System.IO.Compression` |
| 오디오 (snd_*.c) | 미작업 | Unity Audio System으로 완전 대체 |
| 충돌 감지 (cm_*.c) | 미작업 | BSP 충돌 → Unity PhysX 혼용 |
| trace_t 콜백 | bg_pmove에서 필요 | `Func<TraceInput, TraceResult>` 델리게이트로 추상화 |

---

## Unity 프로젝트 초기 설정 가이드

> 현재 `UnityProject/` 폴더에는 C# 스크립트만 있고 Unity 프로젝트 파일이 없음.
> 아래 단계를 순서대로 진행.

### 1단계 — Unity 설치

```
권장 버전: Unity 6 (6000.0.x LTS) 또는 Unity 2022.3 LTS
다운로드:  https://unity.com/releases/editor/qa/lts-releases
```

- Unity Hub 설치 후 해당 버전의 Editor 설치
- 모듈 추가: **Windows Build Support** (또는 Mac/Linux)
- 렌더 파이프라인: **Universal Render Pipeline (URP)** 선택 권장

---

### 2단계 — Unity 프로젝트 생성

**Unity Hub → New Project:**

```
Template : 3D (URP)
Project Name : EnemyTerritoryUnity
Location : /home/user/Enemy-Territory-Web/UnityProject
```

> ⚠️ 이미 `UnityProject/Assets/Scripts/` 폴더와 C# 파일이 있으므로,
> Unity가 프로젝트를 생성할 때 기존 파일을 덮어쓰지 않도록 주의.
> 프로젝트 생성 후 Unity가 자동으로 스크립트를 인식한다.

---

### 3단계 — Assembly Definition 설정

`UnityProject/Assets/Scripts/` 안에 `.asmdef` 파일을 만들어
컴파일 의존성을 명확히 한다.

**`Assets/Scripts/Core/ET.Core.asmdef`**
```json
{
    "name": "ET.Core",
    "rootNamespace": "ET.Core",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "autoReferenced": true
}
```

**`Assets/Scripts/Network/ET.Network.asmdef`**
```json
{
    "name": "ET.Network",
    "rootNamespace": "ET.Network",
    "references": ["ET.Core"],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "autoReferenced": true
}
```

**`Assets/Scripts/Game/ET.Game.asmdef`**
```json
{
    "name": "ET.Game",
    "rootNamespace": "ET.Game",
    "references": ["ET.Core", "ET.Network"],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "autoReferenced": true
}
```

---

### 4단계 — 프로젝트 폴더 구조 확인

```
UnityProject/
├── Assets/
│   ├── Scripts/
│   │   ├── Core/              ✅ 완료
│   │   │   ├── ET.Core.asmdef     ← 생성 필요
│   │   │   ├── ETMath.cs
│   │   │   ├── ETShared.cs
│   │   │   ├── Huffman.cs
│   │   │   └── MD4.cs
│   │   ├── Network/           ✅ 완료
│   │   │   ├── ET.Network.asmdef  ← 생성 필요
│   │   │   ├── EntityState.cs
│   │   │   ├── NetAddress.cs
│   │   │   ├── NetChannel.cs
│   │   │   ├── NetField.cs
│   │   │   ├── NetMessage.cs
│   │   │   ├── PlayerState.cs
│   │   │   └── UserCmd.cs
│   │   ├── Game/              🔲 Layer 3 예정
│   │   │   ├── ET.Game.asmdef     ← 생성 필요
│   │   │   ├── PlayerMovement.cs  ← bg_pmove.c
│   │   │   ├── SlideMove.cs       ← bg_slidemove.c
│   │   │   └── WeaponSystem.cs    ← g_weapon.c
│   │   ├── Server/            🔲 Layer 4 예정
│   │   ├── Client/            🔲 Layer 5 예정
│   │   ├── Renderer/          🔲 Layer 6 예정
│   │   └── BotAI/             🔲 Layer 7 예정
│   ├── Scenes/
│   │   └── Main.unity             ← Unity가 자동 생성
│   └── Settings/
│       └── URPAsset.asset         ← URP 설정
├── Packages/
│   └── manifest.json
└── ProjectSettings/
    └── ...
```

---

### 5단계 — 컴파일 오류 확인 및 수정

Unity Editor를 열면 Console 창에서 컴파일 오류를 확인할 수 있다.

**예상되는 초기 오류:**

1. **`UnityEngine` 네임스페이스 없음**
   - `ETMath.cs`의 `Vector3`, `Color`, `Mathf` 등은 `using UnityEngine;` 필요
   - 이미 포함되어 있으나, asmdef 없이는 참조가 안 될 수 있음

2. **`System.Net` 없음 (WebGL 빌드 시)**
   - `NetAddress.cs`, `NetChannel.cs`는 `System.Net.Sockets.UdpClient` 사용
   - PC/Mac/Linux 빌드에서는 정상 동작
   - WebGL에서는 UDP 불가 — 그러나 현재 목표는 PC 서버/클라이언트

3. **`BitConverter.Int32BitsToSingle`**
   - .NET 4.x 필요: Project Settings → Player → Api Compatibility Level → **.NET 4.x** 또는 **.NET Standard 2.1**

---

### 6단계 — 기본 테스트 씬 구성

Unity Editor에서 테스트 씬을 만들어 Layer 1~2가 동작하는지 확인:

```csharp
// Assets/Scripts/Tests/NetworkTest.cs
using UnityEngine;
using ET.Core;
using ET.Network;

public class NetworkTest : MonoBehaviour
{
    void Start()
    {
        // MD4 해시 테스트
        byte[] data = System.Text.Encoding.ASCII.GetBytes("Hello ET");
        uint checksum = MD4.BlockChecksum(data, data.Length);
        Debug.Log($"MD4 BlockChecksum: {checksum:X8}");

        // NetMessage 허프만 테스트
        NetMessage.EnsureHuffmanInit();
        byte[] buf = new byte[NetConst.MAX_MSGLEN];
        var msg = new NetMessage(buf, buf.Length);
        msg.WriteLong(12345);
        msg.WriteString("Hello ET Network");
        msg.BeginReading();
        int val = msg.ReadLong();
        string str = msg.ReadString();
        Debug.Log($"NetMessage roundtrip: {val}, '{str}'");
    }
}
```

---

### 7단계 — Layer 3 작업 시작 전 필수 인터페이스

`PlayerMovement.cs` (bg_pmove.c) 포팅 전에 아래 인터페이스가 필요:

```csharp
// Game/TraceSystem.cs  — Physics 추상화
public struct TraceInput {
    public Vector3 Start, End, Mins, Maxs;
    public int     PassEntity;
    public int     ContentMask;
}

public struct TraceResult {
    public bool    AllSolid, StartSolid;
    public float   Fraction;
    public Vector3 EndPos;
    public Vector4 Plane;       // ETPlane 대신 Vector4 (xyz=normal, w=dist)
    public int     SurfaceFlags;
    public int     Contents;
    public int     EntityNum;
}

public static class TraceSystem {
    public static Func<TraceInput, TraceResult> Trace;  // 외부에서 주입
}
```

```csharp
// Game/CvarSystem.cs  — 간단한 Cvar 관리자
public static class CvarSystem {
    public static float GetFloat(string name, float defaultVal = 0f);
    public static int   GetInt(string name, int defaultVal = 0);
    public static string GetString(string name, string defaultVal = "");
    public static void Set(string name, string value);
}
```

---

## 변환 진행률

```
전체 원본 C 소스: ~473,000줄 (456 파일)
────────────────────────────────────────────
Layer 1 완료:  ~4,000줄 포팅  (q_math.c, q_shared.c, huffman.c, md4.c)
Layer 2 완료:  ~3,300줄 포팅  (msg.c, net_chan.c, netField 타입)
────────────────────────────────────────────
현재까지 포팅: ~7,300줄  (약 1.5%)
남은 핵심 로직: 게임플레이에 필요한 ~50,000줄 (pmove, weapon, server, client)
렌더러 교체:   Unity로 완전 대체 (tr_*.c 직접 포팅 불필요)
봇 AI:        Unity NavMesh 부분 대체 가능
```

---

## 다음 즉시 작업 명령

계속 진행하려면 대화에서 아래 중 하나를 입력:

- **"계속 진행해"** → Layer 3 시작 (SlideMove.cs + PlayerMovement.cs)
- **"유니티 설정 먼저"** → asmdef 파일 생성 + 테스트 씬 구성
- **"서버 먼저"** → Layer 4 서버 핵심 모듈 시작

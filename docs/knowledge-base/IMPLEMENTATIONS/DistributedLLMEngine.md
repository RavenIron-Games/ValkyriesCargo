# DistributedLLMEngine — Technical Architecture Report

## Overview

DistributedLLMEngine (DLNE, "Distributed LLM Network Engine") is a standalone Python application that lets a group of ordinary computers pool their spare GPU VRAM, system RAM, and CPU cores to run large local LLMs (via a bundled/patched `llama.cpp`) that would not fit on any single machine. One node runs as a **Coordinator** (a FastAPI hub that also serves a chat Web UI and exposes an Ollama/OpenAI-compatible API on port `11434`), and any number of other machines run as **Workers**, each launching a `ggml-rpc-server` and reporting hardware telemetry over HTTP heartbeats. The Coordinator dynamically shards a requested GGUF model's layers across the pool of live worker RPC endpoints by invoking `llama-server --rpc ip:port,ip:port,...`, and streams the resulting OpenAI-style chat completion back to the browser. The system is built almost entirely from first-party async Python (FastAPI/uvicorn, httpx, asyncio, websockets) plus `rich`-based CLI UX, WireGuard-based VPN tunneling for WAN deployments, AES-256-GCM local encryption for both chat history and disaster-recovery config vaults, PyInstaller for single-file executables, and a large PowerShell build pipeline that cross-compiles CUDA/Vulkan/CPU variants of llama.cpp and code-signs the result. The `discord/` folder is **not** a live bot that talks to the running engine — it is a separate community-server bootstrap/marketing toolkit (role menus, channel content sync, invite management).

---

### Core Coordinator / Worker Distributed Cluster (Star Topology + Heartbeats)
- **Purpose:** The heart of the system — lets N worker machines register themselves and their hardware with one Coordinator, which continuously tracks live/dead nodes and aggregates total VRAM/RAM/CPU capacity for scheduling decisions.
- **Key files:** `engine/coordinator.py`, `engine/registry.py`, `engine/worker.py`, `engine/hardware.py`, `main.py`
- **Architecture:**
  - `engine/coordinator.py` defines a FastAPI `app` with global singletons `registry = WorkerRegistry()` and `manager = InferenceManager()`.
  - `WorkerRegistry` holds `self.workers: Dict[token_or_id -> dict]`. `update_worker(worker_id, ip, port, token, hardware_profile)` is called on every heartbeat; `prune_dead_workers()` removes any worker whose `last_heartbeat` is >15s old (5s heartbeat interval → 3 missed beats); `get_active_rpc_endpoints()` returns `"ip:port"` strings for alive, non-GPU-yielding workers; `get_total_vram_gb/get_total_sysram_gb/get_total_cpu_cores/get_total_cpu_threads` aggregate cluster capacity.
  - `WorkerNode` is the worker-side agent: on `start()` it (1) downloads/locates the `ggml-rpc-server` binary, spawns it as a subprocess bound to `rpc_ip:rpc_port`, with GPU isolation via `CUDA_VISIBLE_DEVICES`/`GGML_VK_VISIBLE_DEVICES`; (2) starts a background thread `heartbeat_loop()` that every 5s POSTs `{worker_id, version, port, token, hardware}` to `{coordinator_url}/worker/heartbeat` via `httpx.post(..., timeout=httpx.Timeout(10.0, connect=5.0))`. A stable `worker_id` (uuid4) is persisted to `config/.dlne_id`. An ephemeral bearer `access_token` is rotated every 60s and persisted for local WebUI-proxy auth reuse.
  - Coordinator endpoint `POST /worker/heartbeat` (Pydantic model) rejects mismatched engine `VERSION` with HTTP 426 and rejects CPU-only nodes below a hard-coded floor (≥8GB RAM AND ≥2 physical cores) with HTTP 403.
  - `engine/hardware.py`'s `get_hardware_profile()` is what worker heartbeats send: OS, physical/logical CPU counts, total RAM, GPU info (`nvidia-smi`, PowerShell `Get-CimInstance Win32_VideoController`, `vulkaninfo`, `clinfo`, or Apple `system_profiler`), and `is_yielding` — a "Smart Gaming Mode" flag computed by `is_gaming_or_heavy_load()`, which scans running processes for `llama-server`/`rpc-server`, measures blocking `psutil.cpu_percent(interval=0.5)`, subtracts the engine's own normalized CPU usage, and flags yielding if external load > 60%.
  - `main.py` is the process entrypoint: parses `--setup`/`--unlock`, runs interactive setup if no config exists, then dispatches to one of 5 modes based on `MODE=` in config: `coordinator_only`, `coordinator_hybrid`, `worker_only`, `redundancy_worker` (standby coordinator — see failover system below). Every mode also starts the local WebUI reverse-proxy in a background thread.
  - A background asyncio task `monitor_workers_async()` runs every 2s: prunes dead workers, decides whether the currently active model can run given current total VRAM+RAM (pause/resume/spillover), restarts `InferenceManager` if the RPC endpoint set changed (hot re-shard), and broadcasts stats to every connected `/ws/events` WebSocket.
- **How to implement (step-by-step):**
  1. Use FastAPI + uvicorn for the Coordinator's HTTP server; use `httpx` (async client) for heartbeats and proxying.
  2. Define a `POST /worker/heartbeat` endpoint accepting a Pydantic model; store results in an in-memory dict keyed by worker_id with `last_heartbeat = time.time()`.
  3. Run a periodic prune (asyncio task loop under FastAPI's startup event) that evicts entries older than a timeout.
  4. On the worker, launch the actual compute binary as a `subprocess.Popen`, and run heartbeats on a **separate background thread** so the main thread can idle/monitor the subprocess.
  5. Compute an aggregated "endpoint list" for anything not overloaded/yielding — this becomes the argument list handed to the inference engine for RPC-based model sharding.
  6. Gate every heartbeat with a version check (return HTTP 426 to force stale clients to stop) — critical for a distributed fleet you can't SSH into individually.
- **Reusable pattern/snippet:**
```python
# Minimal heartbeat registry pattern
class Registry:
    def __init__(self, timeout=15):
        self.nodes, self.timeout = {}, timeout
    def update(self, node_id, meta):
        meta["last_seen"] = time.time()
        self.nodes[node_id] = meta
    def alive(self):
        now = time.time()
        return {k: v for k, v in self.nodes.items() if now - v["last_seen"] <= self.timeout}
```

---

### Tensor-Sharded Inference Engine & Spillover Scheduler
- **Purpose:** Turns the raw worker pool into an actual multi-machine inference cluster by spawning a local `llama-server` process configured to distribute model layers over RPC to all live worker endpoints, dynamically re-sharding when topology changes, falling back to slower system-RAM ("spillover") when VRAM is insufficient, and throttling/queueing under load spikes.
- **Key files:** `engine/inference_manager.py`, `engine/coordinator.py` (chat_completions endpoint, `monitor_workers_async`, `recalculate_engine_queue`), `engine/downloader.py`
- **Architecture:**
  - `InferenceManager.start_model(model_path, rpc_endpoints, total_vram_gb, total_sysram_gb, queue_channels=2)`: resolves the model's logical tag to a real `.gguf` path; computes required memory as `filesize_GB * 1.2` (20% context overhead); rejects if it would exceed cluster capacity ("Network Maxed Out"); computes a dynamic max context window from available memory, clamped between 2048 and the model's `max_context`; assigns each model instance a sequential port starting at 8081; builds and launches:
    ```
    llama-server -m <path> --host 127.0.0.1 --port <port> -c <safe_context> -np <slots> --threads <cpu_limit> --threads-batch <cpu_limit> -fa on [--model-draft <dflash.gguf>] [--rpc ip1:port1,ip2:port2,...]
    ```
  - **RPC sharding**: passing `--rpc <comma-separated worker endpoints>` to `llama-server` is the actual sharding mechanism — llama.cpp's own RPC backend (each worker runs `ggml-rpc-server`) handles splitting model layers across remote nodes. DLNE doesn't implement tensor math itself; it orchestrates *which* endpoints to pass and *when* to restart.
  - **Spillover / Diffusion Draft (DFlash)**: if a `_dflash.gguf` companion file exists, it's passed via `--model-draft` for speculative decoding.
  - **Topology-change re-sharding**: `monitor_workers_async()` diffs `manager.current_endpoints` against `registry.get_active_rpc_endpoints()` every 2s; on any diff it stops and restarts the model with the new endpoint set — this is how a worker joining/leaving/gaming triggers a live re-shard.
  - **Viral traffic throttling**: `chat_completions` tracks a global `active_inference_requests` counter; above a threshold it drains in-flight streams, stops all models, and restarts with more parallel slots (`-np 6`) for higher throughput.
  - **Context truncation & rolling memory summarization**: before proxying, estimates token counts, truncates history to fit context, and for overflow makes a *separate internal completion call* asking the same `llama-server` instance to summarize the overflow into a compact "MEMORY MODULE" injected into the system prompt — a cheap self-hosted RAG-lite technique for infinite chat history within a bounded context window.
  - **Streaming proxy**: the reply streams via `httpx.AsyncClient().stream(...)` wrapped in a FastAPI `StreamingResponse`, forwarding SSE chunks byte-for-byte. Errors are converted into synthetic SSE `data:` chunks containing a `[System Alert]` message so the chat UI degrades gracefully.
- **How to implement (step-by-step):**
  1. Pick (or build) an inference server binary that supports remote RPC layer-splitting (llama.cpp's `--rpc` + `rpc-server` is the reference) — the coordinator never touches tensors itself, it's purely a process supervisor + arg-builder.
  2. Maintain a `Dict[model_path -> {process, port, ...}]`; on request, diff the desired vs. running RPC endpoint set and only restart if they differ.
  3. Precompute a memory budget (file-size-based estimate × 1.2 for KV-cache overhead) before spawning, and reject/queue requests that would exceed total capacity.
  4. Wrap the child process's stdout/stderr to a rotating log file — critical for diagnosing crashes on remote workers you can't attach a debugger to.
  5. Poll TCP-connectability of the spawned server in a loop with a generous timeout before proxying — large model loads are slow, distinguish "still loading" from "crashed."
  6. For chat-history-longer-than-context, either truncate hard or auto-summarize the overflow via a self-call to the same completion endpoint.
  7. Add a request-rate circuit breaker: above a threshold, drain in-flight streams, then restart the engine with more parallel slots.
- **Reusable pattern/snippet:**
```python
# Topology-driven auto-reshard loop (asyncio, runs forever)
async def monitor():
    while True:
        live = registry.active_endpoints()
        if set(manager.current_endpoints) != set(live):
            manager.stop_all_models()
            manager.start_model(active_model, live, vram_gb, ram_gb)
        await asyncio.sleep(2)
```

---

### LAN Auto-Discovery, Split-Brain Prevention & "Ghost Primary" Failover
- **Purpose:** Lets workers find a Coordinator with zero IP configuration on a LAN, and lets a designated standby ("Redundancy") node safely take over if the Primary Coordinator dies — while guaranteeing the *original* Primary can safely reclaim control if it comes back online, without a split-brain.
- **Key files:** `engine/coordinator.py` (`udp_broadcaster`, `reclaim_listener`, `send_reclaim`, `start_coordinator_server`), `setup_cli.py` (`listen_for_coordinator`), `main.py`.
- **Architecture:**
  - **Discovery broadcast**: a daemon thread on the Coordinator sends a UDP broadcast every 2s to port `11435` containing `DLNE_COORDINATOR:{port}:{timestamp}`, optionally HMAC-SHA256-signed with a shared `network_key`. Workers verify the HMAC (`hmac.compare_digest`) and reject messages older than 10s (replay protection).
  - **Split-brain guard at coordinator boot**: setup explicitly listens for an existing Primary broadcast (3s) before letting a new Coordinator start — if one is heard, setup aborts rather than allowing two Primaries on one LAN.
  - **Redundancy node loop**: runs as an active compute Worker *and* passively listens for the Primary's UDP pulse. On 3 consecutive missed pulses (~12s), enters a "Time-Offset Election Delay" of `redundancy_priority_index * 3` seconds (unique per node) — if another node's broadcast is heard first, aborts its own promotion. If the wait elapses with silence, promotes itself to Coordinator and repoints its own worker thread at itself.
  - **Background model sync**: while in standby, polls the Primary's model catalog every 5 minutes and pre-downloads any model the Primary currently has unlocked, so promotion doesn't require a cold multi-GB download.
  - **Ghost Primary Reclaimer**: when *any* coordinator boots normally, it fires a single signed UDP broadcast to a separate port (`11436`): `DLNE_RECLAIM:{timestamp}:{hmac}`. A promoted (temporary) coordinator listens on that port; on receiving a validly signed, non-stale (<30s) reclaim message it sets `server.should_exit = True`, gracefully shutting down its own uvicorn server and stepping back down to worker behavior — the shared admin-password hash is the secret used both to authorize promotion and to validate reclaim signatures.
- **How to implement (step-by-step):**
  1. Use a raw UDP socket with `SO_BROADCAST` for zero-config discovery — don't require multicast/mDNS, often blocked on corporate networks.
  2. Sign discovery payloads with HMAC using a pre-shared key from the invite file, and embed + verify a timestamp to prevent replay attacks (`hmac.compare_digest`, not `==`).
  3. Before a node boots as "Primary", listen briefly for an existing Primary and abort if found.
  4. For failover, use a probabilistic/priority-based election delay rather than instant promotion — avoids a promotion race between multiple standby nodes.
  5. Implement a distinct, separately-signed "reclaim" broadcast channel so a returning Primary can force any usurper to step down — otherwise a returning-but-stale Primary and a promoted standby could both be active simultaneously.
  6. Gate `server.should_exit = True` (uvicorn's clean-shutdown flag) behind the reclaim listener so stepping down doesn't kill the whole process — only the coordinator API — letting it seamlessly resume as a normal worker.
- **Reusable pattern/snippet:**
```python
# Signed UDP presence beacon with replay protection
def broadcast(sock, secret, port, dest_port):
    payload = f"ROLE:{port}:{time.time()}"
    sig = hmac.new(secret.encode(), payload.encode(), hashlib.sha256).hexdigest()
    sock.sendto(f"{payload}|{sig}".encode(), ('<broadcast>', dest_port))

def verify(msg, secret, max_age=10.0):
    payload, sig = msg.split("|")
    if not hmac.compare_digest(hmac.new(secret.encode(), payload.encode(), hashlib.sha256).hexdigest(), sig):
        return None  # spoofed
    _, port, ts = payload.split(":")
    if abs(time.time() - float(ts)) > max_age:
        return None  # replay
    return port
```

---

### WireGuard VPN Orchestration & Invite-File Onboarding
- **Purpose:** Lets workers on a different network join the swarm securely without port-forwarding, by auto-provisioning a WireGuard tunnel and packaging all connection info into a single drop-in `.conf` "invite" file.
- **Key files:** `engine/wireguard.py`, `setup_cli.py`.
- **Architecture:**
  - `WireGuardOrchestrator.generate_keypair()` shells out to the local `wg` CLI; falls back to dummy placeholder keys if WireGuard isn't installed (feature silently degrades).
  - The Coordinator is always `.1` on the virtual subnet (`10.8.0.1/24`); worker invites get `.2/24`, `PersistentKeepalive = 25`.
  - The setup wizard appends non-standard comment lines (`# DLNE_NETWORK_KEY=...`, `# DLNE_ADMIN_HASH=...`) to the bottom of the `.conf` file — repurposing WireGuard's `.conf` format as a generic secret-bundle carrier.
  - **Onboarding UX**: setup globs for `*.conf` files matching "worker"/"redundancy"/"invite" in the name; if found, the whole role/IP/port/network-key/admin-hash configuration is auto-extracted — the user only has to type `start`.
  - Because WireGuard assigns the Coordinator a fixed `10.8.0.1`, any worker that joined via invite always talks to the Coordinator at that fixed address regardless of the real WAN address — collapses "is this LAN or WAN" into a single code path downstream.
- **How to implement (step-by-step):**
  1. Shell out to the platform WireGuard CLI to generate keypairs; never roll your own X25519.
  2. Fix the server's tunnel address (e.g., `.1/24`) and hand out deterministic small increments to peers.
  3. Bundle any extra application secrets as trailing comment lines inside the same `.conf` your users are already going to distribute — avoids inventing a second file format/distribution channel.
  4. On the client, `glob` for likely invite filenames and offer an auto-detected "just click start" flow; always keep a manual-entry fallback.
  5. Wrap OS-specific tunnel installation in try/except with graceful "fall back to LAN/your own VPN" messaging.

---

### "Ollama Illusion" — OpenAI/Ollama API Mirror & Port Hijack
- **Purpose:** Zero-config interoperability: any third-party LLM frontend that natively supports "local Ollama" can point at the Coordinator and transparently get the entire distributed swarm as its backend, with no plugin or custom integration needed.
- **Key files:** `engine/coordinator.py` (`/v1/models`, `/v1/chat/completions`).
- **Architecture:** The Coordinator exposes `GET /v1/models` and `POST /v1/chat/completions` matching the OpenAI/Ollama wire schema. `get_models()` returns each locally-available `.gguf` tagged with a custom `dlne_status` field (`Unlocked`/`Unlocked (Spillover Warning)`/`Locked`) — additive metadata that OpenAI-compatible clients simply ignore. `chat_completions` never returns model output itself — it spins up (or reuses) an internal `llama-server` instance and proxies raw SSE bytes through unmodified, so the wire format is indistinguishable from a real Ollama/OpenAI-compatible server.
- **How to implement (step-by-step):**
  1. Implement exactly the request/response JSON shape the target ecosystem expects (SSE `data: {...}\n\n` framing terminated by `data: [DONE]\n\n`) — study the real wire protocol closely; clients will silently break on any deviation.
  2. Bind on the port the target client hard-codes so users need zero configuration.
  3. Keep the proxy fully transparent: don't buffer/transform the underlying stream except to inject synthetic error/status chunks in the same schema on failure.
  4. Add non-standard extra fields to responses for your own UI's benefit — compatible clients that don't recognize a field will just ignore it.

---

### Non-Contrib Clause Access Control (Contribute-to-Access Enforcement)
- **Purpose:** A fair-use gate: enforces that anyone accessing the swarm's Web UI or chat API must themselves be running an active Worker node contributing real hardware (or be on the Coordinator's own local machine) — prevents free-riders from consuming pooled compute without contributing.
- **Key files:** `engine/coordinator.py` (`NonContribBlockerMiddleware`, `check_contrib_clause`, `is_local_ip`, `get_request_token`).
- **Architecture:** A Starlette `BaseHTTPMiddleware` intercepts requests to `/webui`, `/`, `/v1/chat`. Extracts caller IP and a bearer token (header or `?token=` query param — needed since browser WebSocket clients can't set custom headers). Local-machine callers (detected via a clever trick: attempting to bind a UDP socket to the given IP succeeds only if that IP is actually assigned to a local network interface) are always exempted. Otherwise scans the live worker registry for a matching active token, 403ing with an HTML "Access Denied" page if no match. A separate FastAPI `Depends()` dependency applies the identical check to individual REST endpoints, and the WebSocket endpoint re-implements the same check manually (middleware doesn't apply to the WS upgrade handshake).
- **How to implement (step-by-step):**
  1. Implement the trust check as three separate enforcement points that must all agree: HTTP middleware, a FastAPI `Depends()` dependency, and manual re-implementation inside any WebSocket handler.
  2. Use short-lived rotating tokens rather than static API keys, so a compromised/stale token self-expires quickly.
  3. Detect "local machine" callers by attempting to bind a UDP socket to the caller's claimed IP — succeeds only for locally-owned interface addresses.
  4. Always support a query-param token fallback alongside the `Authorization` header for browser WebSocket compatibility.
- **Reusable pattern/snippet:**
```python
def is_local_ip(ip: str) -> bool:
    if ip in ("127.0.0.1", "::1", "localhost", "0.0.0.0"):
        return True
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.bind((ip, 0)); s.close()
        return True   # binding succeeds only for locally-owned interface IPs
    except Exception:
        return False
```

---

### Client Portal — Universal Local WebUI Proxy Daemon
- **Purpose:** A tiny local FastAPI server (default `127.0.0.1:8080`, auto-incrementing on conflict) that every DLNE node runs, so the end user always opens the **same** `localhost:8080` URL regardless of what role their machine plays or which Coordinator IP is currently active; also auto-launches the browser the first time a Coordinator is detected.
- **Key files:** `engine/client_portal.py`.
- **Architecture:** A background thread either locks `active_coordinator_ip` to a manually configured WAN IP, or listens on UDP `11435` for the Coordinator's signed discovery broadcast, updating live and auto-opening the browser on first discovery. A catch-all route forwards every request via `httpx.AsyncClient` to the active Coordinator, injecting the locally-stored worker bearer token as the `Authorization` header, and streams the response back. Two WebSocket proxy routes bridge the browser's WebSocket to the Coordinator's WebSocket using the `websockets` library with bidirectional `asyncio.gather(...)` pumps, since `httpx` alone can't proxy WS. Also exposes `/local/key`, which lazily generates and persists a 32-byte encryption key (XOR-obfuscated at rest using the machine's MAC-derived id) used purely client-side for zero-knowledge chat encryption — the key never leaves the local machine.
- **How to implement (step-by-step):**
  1. Run a lightweight local reverse-proxy FastAPI/uvicorn instance on every node regardless of role.
  2. Use a catch-all `api_route("/{path:path}")` plus a manual WebSocket bridge built on the `websockets` package.
  3. Auto-discover the real backend via the same signed UDP beacon used for worker discovery.
  4. Do port-conflict detection at startup by attempting `socket.bind` in a loop, incrementing on `OSError`.
  5. Store any locally-generated secrets obfuscated with a machine-derived value so a bare file copy to another machine doesn't work.

---

### Web UI — Real-Time Swarm Chat Client
- **Purpose:** The end-user chat interface: browser-based multi-conversation LLM chat client with live cluster telemetry, cooperative multi-user "vote to swap model" governance, download/VRAM-load progress bars, local zero-knowledge encrypted chat history, and a hidden admin analytics portal.
- **Key files:** `webui/index.html`, `webui/app.js`, `webui/style.css`.
- **Architecture:**
  - On load, fetches `/local/key` (WebCrypto AES-GCM import), decrypts local chat history, polls models, then opens `ws(s)://.../ws/events`.
  - **WebSocket protocol**: server→client message types include `identity`, `cluster_stats`, `cluster_sync`, `library_sync`, `vote_prompt`/`vote_end`, `download_progress`/`vram_progress`, `ban`, `pong` (RTT-based connection-quality dot). Client→server: `ping` (sent every 3s for latency), `vote_request`/`vote_result`/`execute_swap`.
  - **Model swap governance ("voting")**: any client can propose a model change; the Coordinator auto-approves if ≤1 client connected, otherwise broadcasts a 15-second countdown vote to all clients (default deny on timeout); first `approved=true` reaching the server wins and broadcasts `vote_end` — a lightweight, non-persistent, first-past-the-post consensus mechanism.
  - **Zero-knowledge local encryption**: chat history encrypted client-side with AES-256-GCM (random 12-byte IV, base64-encoded) and written to `localStorage` — the Coordinator/workers never see plaintext chat and hold no server-side chat storage at all.
  - **Streaming render**: raw `fetch('/v1/chat/completions', {stream:true})`, reads via `response.body.getReader()`, manually parses SSE lines, incrementally re-renders via client-side Markdown parsing.
  - **Admin Easter-egg portal**: a hidden symbol in the sidebar, on click, prompts for a password, POSTs to `/v1/admin/auth` (bcrypt-checked server-side), receives a session-scoped bearer token stored only in a JS variable (never localStorage).
- **How to implement (step-by-step):**
  1. Use a single persistent WebSocket as the "control plane" and plain `fetch()` + SSE parsing for the actual chat completion stream — keep them separate rather than multiplexing token streaming over the control WS.
  2. Implement client-driven latency measurement via an app-level `ping`/`pong` exchange rather than relying on the WebSocket protocol's built-in ping frames (JS `WebSocket` doesn't expose timing for those).
  3. For local-only "zero-knowledge" persistence, fetch a symmetric key from a same-origin endpoint, import via WebCrypto, and always prepend a fresh random IV to every ciphertext blob.
  4. Build multi-client consensus features as: proposer → server fan-out prompt to all connected sockets → first accept wins → server-side idempotency guard to avoid double-execution if multiple approvals race in.
  5. Design every streamed error path to degrade into the *same* SSE delta-content schema as a normal token so rendering code never needs a separate "error state."
- **Reusable pattern/snippet:**
```js
// SSE fetch-stream parsing without EventSource (needed for POST + custom body)
const reader = (await fetch(url, {method:'POST', body})).body.getReader();
const decoder = new TextDecoder('utf-8');
let buf = '';
while (true) {
  const {value, done} = await reader.read();
  if (done) break;
  buf += decoder.decode(value, {stream:true});
  for (const line of buf.split('\n')) {
    if (line.startsWith('data: ') && line !== 'data: [DONE]') {
      const delta = JSON.parse(line.slice(6)).choices[0].delta;
      if (delta.content) render(delta.content);
    }
  }
}
```

---

### Model Library & Multi-Stream Chunked Downloader
- **Purpose:** Provides a curated catalog of ~35 GGUF models (with per-model VRAM estimate, max context, optional DFlash draft companion, HuggingFace source URL) and downloads them at high speed using parallel ranged HTTP requests, with live progress broadcast to every connected Web UI client.
- **Key files:** `engine/coordinator.py` (`DEFAULT_MODEL_LIBRARY`, `load_models_json`, `download_file_multistream_async`, `monitor_models_json_async`, `/v1/models/request`), `download_dlne_models.sh`, `generate_sh.py`.
- **Architecture:**
  - `DEFAULT_MODEL_LIBRARY` is a hardcoded list of dicts, written to `models/models.json` on first run (hand-editable thereafter). `monitor_models_json_async()` polls the file's mtime every 15s and re-broadcasts an update on external edit — lets an admin hot-edit the catalog without restarting.
  - `download_file_multistream_async(url, dest_path, model_name)`: resolves the final redirect URL and `Content-Length` via a `HEAD` (in `asyncio.to_thread`), splits the byte range into 8 equal chunks, downloads each chunk concurrently via a `ThreadPoolExecutor(max_workers=8)` (each worker synchronous `httpx` `Range` GET, writing to `.part{i}` files — network/file I/O releases the GIL, making a real thread pool effective despite Python's GIL), polls aggregate progress every 0.5s and rebroadcasts, then stitches `.part*` files back together and atomically renames into place.
  - `/v1/models/request` validates an optional admin whitelist, does a pre-flight HEAD for real file size, rejects if projected memory footprint exceeds capacity, rejects if it would leave <20GB free disk, then fires the download as a detached `asyncio.create_task` so the HTTP response returns immediately.
- **How to implement (step-by-step):**
  1. Resolve redirects and get `Content-Length` with one `HEAD` request before starting parallel downloads.
  2. Split the byte range evenly across N workers (8 is reasonable) using HTTP `Range` headers per chunk.
  3. Use a real `ThreadPoolExecutor` and drive it via `loop.run_in_executor` — network reads and file writes both release the GIL, so this genuinely parallelizes despite `asyncio` alone not helping here.
  4. Track a shared mutable progress array (approximate reads are fine for a progress bar, no locking needed) and poll it periodically to broadcast progress without blocking download threads.
  5. Always download into `.partN` temp files first and only rename-into-place after every part completes and is stitched.
  6. Do capacity/disk-space preflight checks against a real estimate before committing to a multi-GB download.

---

### Hardware Detection & "Smart Gaming Mode" Yielding
- **Purpose:** Lets a worker node politely and automatically stop contributing to the swarm the instant its owner starts a game or other heavy local workload, and resume automatically when the load clears.
- **Key files:** `engine/hardware.py` (`is_gaming_or_heavy_load`, `get_hardware_profile`), `engine/registry.py`, `engine/coordinator.py`.
- **Architecture:** `is_gaming_or_heavy_load()` first scans processes for the engine's own inference binaries; if none running, falls back to a simple GPU-load heuristic. If inference processes ARE running, primes and reads their own CPU usage, measures total system CPU over a blocking 0.5s window, subtracts the engine's own normalized share, and flags yielding if the *external* (non-DLNE) load exceeds 60%. This boolean rides along inside every heartbeat's hardware payload; `WorkerRegistry.get_active_rpc_endpoints()`/`get_total_vram_gb()` both exclude any worker whose hardware is yielding (and which has a GPU). Because the topology-reshard monitor loop diffs the endpoint set every 2s, a yield-flag flip triggers a live model re-shard away from the gaming machine within a couple of seconds, no manual intervention.
- **How to implement (step-by-step):**
  1. Detect "my own workload" processes by name first, so you can separate "load I caused" from "load someone else caused" — critical to avoid a false-positive self-triggered yield loop.
  2. Use a short blocking CPU sample rather than an instantaneous read, but keep it short since this runs inside a heartbeat loop with its own cadence.
  3. Pipe the yield state through the *same* heartbeat channel as hardware capacity, not a separate side channel.
  4. Have the scheduler treat yielding purely as "temporarily exclude from the endpoint list," not "deregister the node" — the node keeps heartbeating and is instantly available again the moment load clears.

---

### Interactive Setup CLI Wizard & Windows Security Auto-Configuration
- **Purpose:** A guided terminal wizard (built on `rich`) that walks a new user through choosing a role and auto-configures Windows security exceptions (Defender exclusions, WDAC supplemental policy) so bundled unsigned llama.cpp binaries aren't blocked by endpoint protection.
- **Key files:** `setup_cli.py`.
- **Architecture:**
  - `configure_windows_security()` (Windows-only, one-time-marker-guarded) detects active WDAC policy count and Smart App Control state via PowerShell, then builds a **single** elevated PowerShell script combining a Defender folder exclusion and (if WDAC active) a supplemental WDAC policy — bundled so the user sees **one** UAC prompt rather than several. Smart App Control (no exclusion mechanism) is handled by opening the Windows settings page and asking the user to manually disable it.
  - Checks for a drop-in invite `.conf` for one-command onboarding, including a hidden admin password gate that unlocks Redundancy-node selection even via the invite fast-path.
  - Manual flow: role selection (worker is the only publicly advertised option; a hidden password reveals a 5-option admin menu).
  - For Coordinator roles: generates a random 32-byte hex `NETWORK_KEY`, optional bcrypt-hashed admin password, WAN vs LAN routing (auto-detects public IP), the split-brain pre-check, conditional WireGuard invite generation.
  - For Worker/Hybrid/Redundancy: WireGuard invite auto-install, LAN auto-discovery with manual-IP fallback, hardware-isolation section (GPU selection, per-GPU VRAM limits, RAM spillover limit defaulting to 1/3 of total, core allocation defaulting to 1/4) with hard minimum-hardware enforcement.
  - For any Coordinator role, forces creation of a disaster-recovery Master Vault before completing setup.
- **How to implement (step-by-step):**
  1. Use `rich.console.Console` + `rich.prompt.Prompt/Confirm` for a polished terminal wizard.
  2. Batch all admin-elevation-requiring OS changes into a **single** generated script executed via one `Start-Process -Verb RunAs -Wait` — minimizes UAC fatigue; have the script write a "done" marker file the caller polls for, since exit codes don't reliably propagate back.
  3. Feature-detect security software state before prompting — check specific registry-backed cmdlets and only show relevant prompts.
  4. Provide a "power user" hidden menu behind a shared secret for advanced roles you don't want to advertise publicly, while keeping the default path extremely simple.
  5. Auto-glob for expected file patterns in conventional locations and always offer a manual fallback.

---

### Disaster-Recovery Master Vault (AES-256-GCM Config Backup/Restore)
- **Purpose:** Because a Coordinator holds unique cryptographic secrets (network HMAC key, admin password hash, WireGuard keys) the whole swarm's trust model depends on, this system lets an operator export everything into one password-protected encrypted file for off-site storage, and later restore a dead Coordinator's full identity onto a fresh machine.
- **Key files:** `engine/backup.py`, `main.py --unlock`, `setup_cli.py`.
- **Architecture:** `create_master_vault(password, output_path)` zips the entire `config/` directory (minus `.enc`/`.py`), `models/models.json`, and `wg_configs/` in-memory; derives a 256-bit key via PBKDF2HMAC-SHA256 (480,000 iterations, random 16-byte salt); encrypts with `AESGCM(key).encrypt(random 12-byte nonce, data, None)`; writes `salt || nonce || ciphertext` to one `.enc` file. `restore_master_vault` reverses this, cleanly re-deriving the key, decrypting (returns `False` cleanly on bad password/corruption via `AESGCM.decrypt` raising), wiping existing config/wg_configs directories, then re-extracting.
- **How to implement (step-by-step):**
  1. Use PBKDF2-HMAC-SHA256 with a high iteration count to derive a symmetric key from a user password; fresh random salt per vault, stored alongside the ciphertext.
  2. Use AES-GCM (authenticated encryption) rather than plain AES-CBC — a failed `.decrypt()` cleanly signals tampering/wrong-password with no separate HMAC verification needed.
  3. Bundle heterogeneous state into a single in-memory zip before encrypting — keeps the on-disk vault to exactly one file.
  4. On restore, explicitly wipe destination directories first so restore is idempotent/deterministic.
  5. Support the encrypted vault living in more than one conventional location with a fallback lookup.

---

### Binary Obfuscation / Manifest-Chunk Reassembly System
- **Purpose:** Splits each shipped compiled engine binary into 2 randomly-named, randomly-sized chunk files at build time, so the raw executable never exists on disk as a recognizable file until runtime — primarily to reduce false-positive AV/EDR quarantine on unsigned third-party binaries bundled inside a signed launcher.
- **Key files:** `builder/obfuscate.py`, `engine/manifest.py` (generated), `engine/downloader.py`, `manifest.json`.
- **Architecture:**
  - `obfuscate.py init` shuffles a fixed pool of 20 decoy filenames and assigns 2 unique chunk names to each of 9 target binary names, writing both `manifest.json` and a Python module `engine/manifest.py` (so the shipped app can import it directly with no file I/O at runtime).
  - `obfuscate.py chunk <source> <target_dir>` splits the real binary into 2 pieces at a **randomized offset** (30%–70% of file size, varying between builds so file-size fingerprinting also doesn't work), writes each under its manifest-assigned decoy filename.
  - At runtime, `download_and_extract_engine(role)` checks for the real binary; if absent, looks up chunk mapping, checks all chunks exist, **concatenates them in order** into the real target filename, registers `atexit.register(cleanup_binary, target_path)` so it's deleted on process exit. Then runs a compatibility self-test (`--help`, inspecting exit code/stderr for missing-DLL/illegal-instruction/missing-Vulkan signatures) before accepting the tier, falling back to a `-cpu` variant if the primary tier fails.
- **How to implement (step-by-step):**
  1. Maintain the chunk-name↔binary-name mapping as a small importable dict, not an external file lookup.
  2. Randomize both the *names* (per rebuild) and the *split offsets* (per chunk operation).
  3. Reassemble into a real temp/working file lazily, on first use, and register an `atexit` cleanup hook.
  4. Pair reassembly with an explicit compatibility self-test and a tiered fallback list rather than assuming the first reassembled binary will run on the host CPU.
- **Reusable pattern/snippet:**
```python
def reassemble(manifest, bin_dir, real_name):
    chunks = manifest.get(real_name)
    if not chunks or not all(os.path.exists(os.path.join(bin_dir, c)) for c in chunks):
        return None
    target = os.path.join(bin_dir, real_name)
    with open(target, "wb") as out:
        for c in chunks:
            out.write(open(os.path.join(bin_dir, c), "rb").read())
    atexit.register(lambda: os.remove(target) if os.path.exists(target) else None)
    return target
```

---

### Builder / Release Pipeline (Multi-Platform Cross-Compilation & Code Signing)
- **Purpose:** A single administrator-run PowerShell "mega-script" that compiles llama.cpp from source for every supported hardware tier, packages the Python engine with PyInstaller into a single-file executable, applies the binary-obfuscation chunking step, code-signs the Windows executable via Azure Trusted Signing, and zips each platform's release folder.
- **Key files:** `builder/build_all_releases.ps1` (~1050 lines), `builder/obfuscate.py`, `DLNE-Node.spec`.
- **Architecture:**
  - Requires Administrator; auto-installs (via `winget`) Git, CMake, Python, Azure CLI, .NET SDK, the Microsoft "Trusted Signing" dotnet tool; checks/installs WSL2 for Linux cross-compilation; checks/installs Vulkan SDK.
  - **`Invoke-QuietCommand`**: reusable helper that runs a scriptblock, captures output to a log file, renders a live single-line progress indicator by regex-matching common build-tool output patterns into an ASCII bar/spinner.
  - **Phase 1**: for each selected tier runs CMake with tier-specific flags (Vulkan, CUDA-legacy, CUDA-latest), building only `llama-server`/`ggml-rpc-server`. Thread count computed dynamically from available RAM to avoid OOM during CUDA template compilation. Linux is cross-compiled inside WSL2 as a **background PowerShell Job** running concurrently with native Windows builds.
  - **Phase 2**: obfuscation-chunks each compiled binary per release directory.
  - **Phase 3**: PyInstaller `--onefile` packaging per platform; macOS instead does **bytecode-only source distribution** (compiles to `.pyc`, deletes original `.py`, zips, obfuscation-chunks the zip itself) with a generated launcher script that reassembles/unzips/pip-installs/runs at first launch on the Mac.
  - **Code signing**: signs the Windows exe via Azure Trusted Signing if enabled; signing failure is explicitly **non-fatal** — the build continues shipping unsigned rather than aborting.
  - **Phase 4**: zips each platform's release folder.
  - Includes incremental-rebuild detection (skip phases whose outputs already exist) and interactive per-tier build selection.
- **How to implement (step-by-step):**
  1. Separate the pipeline into clearly labeled, independently skippable/resumable phases based on artifact-presence checks.
  2. Offload cross-platform builds you can't natively target to background jobs so native and cross-compile work proceeds in parallel.
  3. Auto-bootstrap every required SDK/tool via a package manager with a "does it already exist" pre-check.
  4. When your compiled binaries are unsigned third-party code bundled inside a signed launcher, treat "gets false-flagged by AV/WDAC/SmartScreen" as an expected first-class problem: solve it at both build time (chunked obfuscation) and install time (auto-configure Defender/WDAC exceptions).
  5. Prefer a cloud code-signing service over a locally-held certificate for CI/build-server signing.
  6. Make signing failures non-fatal to the overall pipeline if you'd rather ship an unsigned build than block a release on a transient signing-service outage.

---

### Discord Community Bootstrap Toolkit (NOT a live engine integration)
- **Purpose:** Important finding: **there is no live bot that talks to the running DLNE engine, no slash commands that query cluster status.** The `discord/` folder is a completely separate, one-off/administrative toolkit for standing up and maintaining the project's *community Discord server* — role-gated onboarding, channel content sync from markdown source-of-truth files, invite-link management. It shares no runtime process, port, or state with the Coordinator/Worker engine.
- **Key files:** `discord/scripts/setup_server.py`, `discord/scripts/sync_channels.py`, `discord/scripts/create_invite.py`, `discord/channels/*.md`.
- **Architecture:**
  - All three scripts use `discord.py` with a **hardcoded bot token literal in source** (flagged here as a real secret-hygiene issue worth fixing if this pattern is reused — use environment variables or an untracked secrets file instead).
  - `setup_server.py`: idempotently creates/prunes a fixed set of roles and channel layout, deleting anything present that isn't in the declared allow-list — a "sync to desired state" reconciliation pattern. Posts a persistent `discord.ui.View` with toggleable role-assignment buttons, pruning any earlier onboarding message first.
  - `sync_channels.py`: smart-diff content sync — reads local markdown as the canonical channel content, chunks to Discord's per-message character limit, fetches the channel's existing bot-authored message history, only **edits** messages whose content differs, **sends** new chunks, **deletes** trailing messages if the doc shrank — an efficient minimal-diff publish pipeline.
  - `create_invite.py`: finds-or-creates a permanent invite, sweeps/deletes any other bot-created invites, keeping exactly one canonical link live.
- **How to implement (step-by-step):**
  1. Use `discord.py`'s `commands.Bot` with `discord.ui.View(timeout=None)` + custom `Button` subclasses with **static `custom_id`s** for persistent, always-clickable buttons that survive bot restarts (must register persistent views via `bot.add_view(...)` in `on_ready`).
  2. Treat server layout as declarative desired-state in code: diff against what currently exists and both create missing and delete extraneous entries on each admin-invoked sync.
  3. For any channel whose content is really "documentation you want editable in git," keep the markdown as the source of truth and have a sync script push to Discord using an edit-in-place diff rather than wipe-and-repost.
  4. **Never hardcode bot tokens in source** — use environment variables or a local untracked secrets file.
  5. Keep this kind of one-off admin tooling as standalone scripts invoked manually, not a long-running always-on service, if it only needs to run occasionally on command.

---

### WebLander Marketing Site + Live Style Designer
- **Purpose:** A separate static marketing/landing page (distinct from the actual product `webui/`) advertising DLNE's features with an animated canvas visualization of the swarm concept, plus a companion "Designer" tool for live-tweaking the landing page's CSS custom-property theme in one browser tab while previewing it in an embedded iframe of another.
- **Key files:** `WebLander/index.html`, `WebLander/app.js`, `WebLander/designer.html`.
- **Architecture:** A hand-rolled `requestAnimationFrame` particle sim (`Node` class, no external animation library) drives a canvas visualization illustrating the yield/reshard behavior interactively. `index.html` registers a `window.addEventListener('message', ...)` handler that applies incoming `{type: 'update-styles', styles: {...}}` payloads as `document.documentElement.style.setProperty(key, value)` calls. `designer.html` is the parent page: a color-picker/typography control panel that pushes style updates via `postMessage` as the user drags controls, previewing the embedded page in an iframe.
- **How to implement (step-by-step):**
  1. Drive marketing-site "product visualization" widgets with a minimal hand-rolled canvas animation rather than pulling in an animation library.
  2. Author CSS with `:root { --var: value; }` custom properties for every themeable value, so runtime overrides are just `element.style.setProperty('--var', value)`.
  3. Build a live style-editor as a separate page that embeds the real page in an iframe and communicates via `postMessage`/`addEventListener('message')`.
  4. Validate/whitelist the `event.data.type` before acting on `postMessage` payloads, and verify `event.origin` too (this codebase checks `type` but not `origin` — worth tightening if reused).

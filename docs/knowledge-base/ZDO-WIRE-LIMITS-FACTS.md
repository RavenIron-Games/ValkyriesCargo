# ZDO wire limits, and the disconnect they cause

**How big a single ZDO payload may be before Valheim's own network stack breaks, and what the
breakage looks like from a log.** Verified against the live Valheim build, August 2026, from the
decompiled `assembly_valheim` in this folder. Method names drift across game patches; the shapes
below have not.

Written after a TortalPortal investigation where a client was disconnecting every session and the
snapshot JPEGs it stores on portal ZDOs were the obvious suspect. They were not the cause that time —
but the ceiling is real, the margin was 19%, and the failure mode is far worse than "the data does
not arrive".

---

## The ceiling

**512 KiB (524,288 bytes) per reliable Steam message.** `ZSteamSocket.SendQueuedPackages` calls
`SteamNetworkingSockets.SendMessageToConnection(..., flags: 8)` — flag 8 is
`k_nSteamNetworkingSend_Reliable`. Anything larger comes straight back as **`k_EResultInvalidParam`**.

Note that `k_EResultInvalidParam` is ambiguous: it *also* means "invalid connection handle". Tell them
apart by the connection state — if Steam has not reported
`k_ESteamNetworkingConnectionState_ProblemDetectedLocally` and the socket still believes it is
connected, the handle is fine and the **message** was rejected.

## Why exceeding it is a disconnect and not a dropped packet

```csharp
private void SendQueuedPackages() {
    while (m_sendQueue.Count > 0) {
        byte[] array = m_sendQueue.Peek();          // PEEK, not Dequeue
        EResult val = SteamNetworkingSockets.SendMessageToConnection(...);
        if ((int)val == 1) { m_sendQueue.Dequeue(); continue; }
        ZLog.Log("Failed to send data " + val);
        break;                                      // and it is STILL at the head
    }
}
```

A refused message is never discarded. It sits at the head of the queue and is retried **once per
frame, forever**, blocking every packet behind it. The peer stops talking entirely, and ~30 seconds
later the other end's `ZRpc` timeout tears the connection down. On reconnect the same object is
usually re-serialised and it happens again — **a boot loop out of one oversized blob.**

Log signature, from the sending side:

```
Failed to send data k_EResultInvalidParam     <- ~60/second (once per frame), for 29 seconds
...
ZRpc timeout detected
  send queue size:98
Closing socket / Lost connection to server:ErrorDisconnected
```

## Why a big ZDO cannot be split

`ZDOMan.SendZDOs(peer, flush)`:

```csharp
int num = 10240 - peer.m_peer.m_socket.GetSendQueueSize();
if (num < 2048) return false;
...
foreach (ZDO item2 in m_tempToSync) {
    if (zPackage.Size() > num) break;    // checked BEFORE the append, never after
    ... item2.Serialize(zPackage2); zPackage.Write(zPackage2);
}
peer.m_peer.m_rpc.Invoke("ZDOData", zPackage);
```

Three consequences worth knowing:

1. **The budget is advisory, not a cap.** The size test runs *before* each append, so a packet already
   at 9 KB happily takes one more ZDO of any size. A 2 MB ZDO ships as a 2 MB packet.
2. **At most one oversized ZDO per packet.** After appending it, `Size() > num` is true and the loop
   breaks. Two large ZDOs can never share a message — so the worst case is
   `~10 KB + one ZDO + framing`.
3. **`peer.m_invalidSector` is written first and is unbounded**, but if it alone exceeds the budget
   the ZDO loop breaks immediately and no ZDO is appended. The two cannot stack.

## And nothing coalesces messages either

`ZRpc.Invoke` clears its package, writes the method hash, serialises the parameters and calls
`SendPackage` → `m_socket.Send(pkg)`, which enqueues exactly one `byte[]` and sends it. **One RPC =
one Steam message.** Several large ZDOs produced on consecutive frames become several separate
messages, never one giant one. (Worth verifying if this changes — it is the only thing standing
between "one big object" and "several big objects added together".)

## Practical rules

- **Keep any single ZDO's total payload under ~440 KB.** That is 512 KiB less ZDOMan's ~10 KB of
  co-packed ZDOs and RPC framing, with real margin left over.
- **Never truncate to fit — discard.** A half-written blob is worse than none, and a ZDO cannot carry
  a continuation.
- **Chunk anything genuinely large over a routed RPC instead.** TortalPortal's icon library is the
  worked example: 96 KB per icon, sent in 16 KB slices, size and dimensions validated at both ends,
  a cap on how many will be accepted.
- **Remember a saved world only contains what was successfully sent.** Measuring blob sizes in a
  `.db` tells you about the uploads that *fit*; the ones that broke the connection are by definition
  absent. Use it for the distribution, not as proof of the ceiling never being hit.

## Measuring the blobs in a world

Byte arrays are serialised with a little-endian `int32` length immediately before the data, so a
JPEG's exact stored size is recoverable from a `.db`:

```python
i = b.find(b'\xff\xd8\xff', i)                       # candidate JPEG start
n = struct.unpack('<I', b[i-4:i])[0]                 # the length prefix
if 1024 <= n <= 4_000_000 and b[i+n-2:i+n] == b'\xff\xd9':
    ...                                              # confirmed: exactly n bytes
```

Sanity-check the hit count against the object count you expect. On a 92-portal world this matched 92
of 5,220 raw `FFD8FF` byte sequences — the other 5,128 are coincidences inside compressed data, all
spanning under 48 KB.

Measured there, TortalPortal at 1080p / JPEG quality 85: **48 KB to 425 KB**, median 197 KB. 425 KB is
81% of the ceiling, which is thinner than it looks for a failure that costs somebody their session.

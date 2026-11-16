# Networking security hardening

This branch applies a narrow server-side mitigation to the client-asserted NPC-damage path and records the remaining security boundaries that cannot be safely repaired by changing a single RPC.

## Implemented

### NPC damage relay authorization

`NetPlayer.HitUpRpc` and `NetPlayer.SentryHitUpRpc` accept a client-supplied `byNpc` flag. That flag is not proof of an NPC hit. Before this change, any connected client could set it and reach the NPC-damage branch, bypassing the player-vs-player and squad checks.

The server now additionally requires a `byNpc` request to come from the player that the server has selected to run the target's orbit (`RunsOrbit == Station`), and that player must still be in space. The check uses the server-written `RunsOrbit` value plus a separate server-written `RunsOrbitActive` bit. The separate bit matters because the Void's valid orbit ID is `-1`, which is also the existing no-runner sentinel. It does not rely on the owner's client-written `authority` flag. A focused Edit Mode test covers the allowed and rejected runner states.

**Residual limitation:** NPC combat itself is still simulated by the orbit runner's client. A malicious client that is legitimately selected as the runner can still fabricate an NPC hit. Fully proving that an NPC fired at a particular player requires authoritative NPC combat or a server-verifiable shot/hit event; the current architecture does not provide that. This patch closes the easy cross-client flag abuse, not all client-authoritative combat cheating.

## Findings requiring architectural or deployment work

### Client-reported player location

`NetPlayer.station` and `NetPlayer.place` are owner-writable. Many gameplay RPC checks use these values through `NetGuard.SameOrbit`, `NetGuard.DockedAt` and related helpers. A modified client can potentially lie about its location.

Changing those variables to server-write-only without replacing the existing owner updates would break normal travel and docking. Merely forwarding the same claims through a new RPC would not make them trustworthy. A real fix needs a server-maintained location state machine, validation of legal transitions against trusted travel/docking events, and migration of every RPC that currently consumes the owner-reported fields. The server currently does not simulate the player's flight or scene transitions, so this cannot be completed safely as a local permission change.

### Direct-IP transport confidentiality

The Relay path explicitly selects Unity Relay's `dtls` connection type. The direct-IP path calls `SetConnectionData` and does not configure equivalent encryption in this project. Do not treat direct-IP sessions as having the same transport confidentiality as Relay sessions. Use Relay, or restrict direct-IP play to a trusted LAN/VPN until a supported authenticated encryption design is implemented and tested for the exact Unity Transport package version in use.

No speculative transport API or custom cryptography has been added here.

### Flooding and denial of service

`NetRateLimit` is an application-level RPC limiter. It helps against clients that connect and then flood recognized RPCs. It cannot stop volumetric UDP floods, connection/handshake floods, or resource use that occurs in the transport before an RPC reaches its handler. Those require transport/host firewall limits, provider-level DDoS mitigation, or Relay. Do not describe the token buckets as DDoS protection.

### Web Admin

The web admin already binds to `127.0.0.1` by default and warns when configured on a non-loopback address because its built-in HTTP listener is not TLS. Keep it loopback-only, or expose it only through a correctly configured TLS reverse proxy/VPN. A warning does not encrypt a connection.

## Verification status

- Added an Edit Mode unit test for the orbit-runner authorization predicate.
- No Unity compilation, Edit Mode test run, live adversarial-client test, packet capture, transport load test, or DDoS test has been run for this branch.
- This branch is a mitigation and security follow-up, not a claim that the multiplayer protocol is cheat-proof.

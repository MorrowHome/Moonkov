# Network session behavior

Updated 2026-10-24: authenticated online raids now support bounded restoration of the original character, inventory and deployment for up to 90 seconds. Direct transport requires DTLS; game joins use short-lived single-use tickets. Relay has separate create and join-by-code actions. See [current online setup and validation boundaries](OnlineSessions-20261024.md).

The game keeps Netcode for Entities prediction, authoritative server movement,
inventory mutations and damage. A client world makes one transport connection
request immediately; Unity Transport owns retransmission and handshake retries.

- Connection and world synchronization waits use 30 seconds of real time.
- Ghost loading measures instantiated ghosts against the server count. An empty
  world has a one-second grace period, without division by zero.
- Transport disconnect events distinguish timeout, incompatible protocol and
  rejected credentials. The ship screen displays the localized failure message.
- Enter and return requests are serialized so repeated UI clicks cannot create
  overlapping worlds or dispose the same worlds twice through the return path.
- Service-session leave failures still allow local world cleanup.
- Prediction and server movement use the same finite, bounded input contract.
  Server weapon actions consume that validated input as well. Target coordinates
  cannot overflow the shot ray or turn it behind the requested look direction.
- Command sending compares NetworkTick values with wrap-aware ordering, retains
  a single-shot press between ticks and restores pruned current-tick commands.

A dropped raid returns to the ship and offers an explicit reconnect action. The
online server retains the original body for up to 90 seconds, still vulnerable,
then settles it on expiry or death. Fresh authentication can transfer the same
deployment to a new connection. Resume-only requests never create a new raid.
Server restart and a lost host process cannot restore the in-memory world.

Validation covers compilation, progress/reason branches, invalid numeric input
and an isolated ECS run of the real sender across tick wrap, buffered fire,
continuous light state and command-buffer recovery. Host/client latency,
packet loss, interrupted connections and persistence recovery need a separate
multiplayer acceptance run before a production-readiness claim.

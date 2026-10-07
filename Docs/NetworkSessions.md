# Network session behavior

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

A dropped raid returns to the ship. It does not silently create a new connection
in the old client world: the server must settle the previous raid and release its
equipment before a new join reloads the persisted profile. Seamless reconnect
would require an explicit server-side grace period and authenticated resumption
of the same raid, rather than repeating the join request.

Validation covers compilation and progress/reason branches. Host/client latency,
packet loss, interrupted connections and persistence recovery need a separate
multiplayer acceptance run before a production-readiness claim.

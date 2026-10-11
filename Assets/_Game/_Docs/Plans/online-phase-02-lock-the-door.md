# Online phase 2: lock the door

Part of `online-roadmap.md`; the rules are in `online-security-model.md`. Written 11 Oct 2026.
**Not built.** Needs Phase 1.

## Goal

Nobody gets a player object without proving who they are and that they run the right build, and nothing a
modified client sends can do more than the real client could.

## What exists today

- No connection approval (`Networking/Prefabs/NetworkManager.prefab`: `ConnectionApproval: 0`).
- The client signs in to Unity Authentication (`NetworkConnectionManager.EnsureServicesInitializedAsync`)
  but the save is keyed by an id the client generates (`CharacterSession`, key `kn.account.v1`) and sends
  in `PlayerIdentity.SubmitIdentityRpc`. The code says so: "taken on trust until real sign-in exists".
- Transport encryption is off (`UseEncryption: 0`).
- 100 client-to-server calls in 29 files; 14 use the old `[ServerRpc]` attribute. Only chat has a rate
  limit (`PlayerChat`, 5 in 5 s).
- The only kick is for a second character of one account (`PlayerSaveAgent.cs` : 160).

## Work items

1. **Connection approval.** Turn it on and add a `ConnectionGate` (in `Networking/Runtime`). The client
   sends, in the approval data: the build version, a protocol number we raise when the two sides stop
   being compatible, and its Unity Authentication token. The gate refuses, with a reason the client can
   show, when: the version or protocol is wrong; the token is missing, expired or not signed by Unity; the
   account is banned; the account is already in the world; the map is full; approval took too long.
2. **Verified accounts.** The server checks the token's signature against Unity's published keys (fetched
   and cached; they rotate) and its expiry and project. The account id becomes a 32-character id derived
   from the provider and the verified player id, so every existing save key rule (`SaveKeys.IsId`) holds.
   `SubmitIdentityRpc` stops carrying an account id.
   **Prove first:** verifying the token inside a Unity server build (one forum report says the token and
   the published keys used different signature types). If it cannot be done locally, the server asks
   Unity's player API to confirm the token instead.
3. **Carry the test saves across.** Once, after signing in, a client may present its old `kn.account.v1`
   id and the server moves that account document to the verified id if nobody has claimed it. This exists
   for the owner's and his brothers' test characters and is switched off before any beta.
4. **Encryption.** Relay connections are already encrypted. For direct connections, the server is given a
   certificate and key at launch (`UnityTransport.SetServerSecrets`) and the client checks the server's
   name against our own certificate authority (`SetClientSecrets`). The key is a secret (environment
   setting).
5. **A rate budget for every call.** A small `RpcBudget` helper in Core: each client has an allowance per
   kind of call; a call over the allowance is dropped and logged; a client that keeps flooding is
   disconnected. The numbers live in a new `NetworkSafetySettings` asset, not in code. Chat's own limit
   moves onto it.
6. **The audit.** Work through the 100-row table in `online-security-model.md`, in this order: Economy
   (25 calls), Items, Forge, Progression, Combat and Player, Social, the rest. Each call is checked
   against the five questions, fixed, moved to `[Rpc(SendTo.Server, InvokePermission = Owner)]` if it is
   one of the 14 old ones, and its row marked. Three are known before starting:
   - `PlayerJumpMotor.RequestJumpServerRpc` takes the obstacle layers and the model offset from the
     client. The server uses its own.
   - `PlayerMeleeCombat.RequestMeleeStrikeDamageServerRpc` is the client reporting a hit. Keep the swing
     window, step and range checks; review `hitLatencyGrace`, `serverMaxValidationRange` and the skills'
     `impactRangeLeniency = 1.5`; log refusals. Whether the server should test hits itself is an owner
     decision (it costs animation-accurate hit shapes on the server).
   - `PlayerAutopilot.SetPreferencesServerRpc(string json)` and `PlayerKiosk.OpenRpc(..., KioskOffer[]
     offers, ...)` take free-size data: cap the size before reading it.
7. **GM by account.** The `-gm` list from Phase 1 now names verified accounts. A `bans` world document,
   with `gm.kick`, `gm.ban` and `gm.unban`. Every GM command writes a log line.
8. **The log lines** listed under "Logging" in the security model.
9. **Harden the client build.** IL2CPP for the client. Use Multiplayer Roles to leave server-only data
   (drop tables, Hostile brains, the GM tools) out of the client build where it is practical.
10. **Attack tests.** A test assembly that connects a client and sends what a modified one would: no
    token, a stale build, another player's object, negative amounts, out-of-range cells, a thousand calls
    a second. Each must be refused, change nothing and leave a log line. These stay in the test suite.

## Tunables

`NetworkSafetySettings`: allowance per call kind, the flood threshold that disconnects, the approval time
limit, the leniency values for melee and skill reach.

## Done when

- A client with no token, a wrong build or a banned account is refused with a readable reason.
- The old client-made account id is no longer accepted anywhere.
- Traffic between a client and the server is encrypted on both the Relay and the direct path.
- All 100 rows of the audit are marked reviewed; no `[ServerRpc]` attribute is left.
- Every attack test passes.
- Two real clients still play normally (second client, not only the owner).

## Risks

- Anonymous sign-in belongs to one machine and profile: reinstalling loses the account. Acceptable for
  testers; fixed by real accounts in Phase 4.
- Rate limits set too tight punish fast legitimate play (Autopilot, quick sorting). Set from measurements
  of real play, with a margin.

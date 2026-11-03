# M8 group 4, Chat on DOTS: NGO rules and the DOTS design (DOTS 11, 3 Nov 2026; design only, nothing coded)

Read with `m7m8-rules-tables/M8-g4.md` section 1 (the NGO rules table) and the port pattern in
`dots-mini-boss-11-notes.md` HANDOVER. Everything here was read from the DOTS clone's `Gameplay/Social/Runtime/PlayerChat.cs`
(501 lines), `ChatRouting.cs`, `ChatCommands.cs`. The rules stay NGO's; only the edges move. Risk MEDIUM (abuse, rate limits;
no saves, no Creds, no ledger).

## 1. NGO facts the port must keep (file:line in the clone)

| Rule | NGO | DOTS consequence |
| --- | --- | --- |
| `Send(channel, text, whisper, links)`: `!IsSpawned` -> `ServerSend` locally; else `SendRpc` / `SendLinkedRpc` (`CallKind.Chat`, `TextFits` x2) | `PlayerChat.cs:117-155` | A DOTS view is unspawned: today it runs `ServerSend` on its own stub. Must ask the server instead. |
| `ServerSend`: `ChatRouting.Clean` (200 chars), `ServerResolveLinks` (links read from the sender's own Pack/gear), own rate limit 5 / 5 s, `Gather` + `ChatRouting.Recipients`, Whisper `SocialGate.Refusal`, GM channel -> `DeliverAnnounced`, other channels `PlayerGm.Label` + `Deliver` | `:202-264` | Runs unchanged on the server body once reached (`HasAuthority` true there). |
| `Gather`: `NetworkManager.ConnectedClientsList` players' `PlayerChat` | `:361-377` | Never fills on DOTS: use `BodyRoleTag.ServerBodies` (server bodies only, leavers skipped). |
| `Describe`: `ClientId = IsSpawned ? OwnerClientId : 0` (used for `Name#id` whisper disambiguation, `ChatRouting.IdSeparator`) | `:350-359` | DOTS: `ClientId = tag.Key.Value` (PlayerKey; never a NetworkId, as 8.2.3 trade). |
| **`IsGameMaster => !IsSpawned \|\| OwnerClientId == ServerClientId \|\| gm.IsGm`** | `:64` | **Hazard:** every DOTS server body is unspawned, so EVERY player would pass the GM-channel check (and get the GM label rules). On a DOTS server body it must be `GmRoster.IsGm(serial)` (the 7.4 GM bind by connection serial; `PlayerGm`'s DOTS answer), never the `!IsSpawned` shortcut. Test it. |
| `Deliver` / `DeliverAnnounced` / combat line: `!IsSpawned` -> `Show` locally, else owner RPCs | `:381-405, 475-480` | On a DOTS server body they would show on the SERVER's screen. Must go to the player's connection (link by PlayerKey). |
| `ServerSystem(text)`, `ServerCombatNotice`, `ServerAnnounceNearby` (40 m) | `:307-328` | Every other system's chat lines go through these: fixing `Deliver` fixes all of them at once. |
| `ServerAnnounce` (static): `NetworkManager.IsListening` -> each connected player; else the local player | `:280-304` | DOTS server: NGO is not listening and there is no local player -> 0 reached. Must iterate the DOTS server bodies (`NetRole.Process == ServerBody`). |
| `Guild => IsSpawned ? netGuild : offlineGuild`; `ServerSetGuild` writes `offlineGuild` when unspawned | `:60, 331-339` | Works on the server body as is (Crew writes it). Observers' namecards need it later as a ghost field (Crew port). |
| Party id from `PlayerParty` | `:356` | Party is not ported: Party channel answers "You are not in a squad" until it is. Expected, not a bug. |
| Slash commands check `mine.IsSpawned` and answer "Offline" (e.g. `TradeChatCommands.cs:22`) | `ChatCommands.cs`, `*ChatCommands.cs` | On a DOTS view every command answers Offline. Relax to "a DOTS view or spawned" (`BodyRoleTag.Of(view)`), per command, as each system lands. |

## 2. DOTS design (as the earlier ports)

- **Requests:** Core `IBodyRequests.Chat(byte channel, string text, string whisperTarget, byte[] linkContainers, byte[] linkCells)`.
  `PlayerChat.Send` on a DOTS view (`BodyRoleTag.RequestsOf`) asks the server instead of `ServerSend`. RPC `ChatSendRpc { byte Channel;
  FixedString512Bytes Text; FixedString64Bytes Target; byte LinkCount; byte C0, C1, C2, I0, I1, I2 }` (`ChatLinks.Max` = 3, Core `IChatSink.cs:51`). Client refuses text whose UTF-8 is over 509 bytes (NGO's 200 chars can be 600 bytes in UTF-8: cut on a char
  boundary with `CopyFromTruncated`, and `Clean` again on the server).
- **Server:** `ChatServerSystem` (Persistence.Entities), main thread: `PlayerRequestServerSystem.TryBody(World, conn, CallKind.Chat, ...)`
  -> `IBodyChat.RunSend(...)` -> NGO's `ServerSend` unchanged. `IBodyChat` (and `IChatOwnerLink`) go in Items: Social references
  Persistence (checked 3 Nov), so Persistence.Entities -> Social would be a two-way pair.
- **PlayerChat changes:** `EntityView` / `EntityServer` (BodyRoleTag); `HasAuthority` excludes a view; `Gather` from `BodyRoleTag.ServerBodies`
  on a DOTS server; `Describe.ClientId = Key.Value` there; `IsGameMaster` on a DOTS server body = the GM roster by its serial (no `!IsSpawned`
  shortcut); `Deliver` / `DeliverAnnounced` / the combat line on a DOTS server body -> `ChatOwnerLink.Current` (by PlayerKey ->
  connection, as `EntityTradeOwnerLink.TryConnection`); `ServerAnnounce` iterates DOTS server bodies when `NetRole.Process == ServerBody`.
- **To the owner:** one `ChatLineRpc { byte Kind (Line / Announce / Combat); byte Channel; byte Outgoing; FixedString64Bytes Sender;
  FixedString512Bytes Text; FixedString64Bytes Highlight; byte Rarity; float Damage, Reflected; byte Flags; three link items (ushort id + ulong data each) }`,
  targeted at the connection (never a ghost ring: chat must not depend on relevancy). Client `ChatClientSystem` -> local view
  `PlayerChat.MirrorLine(...)` -> NGO's `Show` / `ShowAnnounced` / `ShowCombatLine` (the chat window and ticker read those).
- **OwnerMessages:** today every ported system tells its player through `OwnerMessages` (the message line). NGO sent most of those as
  System chat lines (`chat.ServerSystem`). Owner choice for later (not needed to port chat): keep the message line, or route
  `EntityOwnerMessages` through `ServerSystem` too so they appear in the chat log as on NGO. Ask the Master with both options.
- **Not in this step:** cross-map routing (world service, not built in NGO either), Party / Crew channels' membership (their ports),
  Contacts' block list (`SocialGate` stays null until Contacts), the Crew name ghost field for others.

## 3. Tests (plan them before the run)

- B-test, two admitted real-prefab bodies 1 m apart and a third 60 m away (`Admitted`, positions set before the sends), a fake
  `IChatOwnerLink` capturing (body, kind, channel, sender, text):
  1. General: A says "hello": A (outgoing) and B get it, C (60 m) does not.
  2. Whisper by name and `Name#<PlayerKey>` when two share a name; whisper to self refused; unknown name refused with NGO's line.
  3. GM channel: a non-GM is refused ("Only a GM can use this channel.") **on the DOTS server body** (the `IsGameMaster` hazard);
     a bound GM (GmRoster by serial, as B5) is not.
  4. Rate limit: the 6th line within 5 s is refused with NGO's text.
  5. `ServerAnnounce` reaches every server body (count == bodies) and nothing reaches a leaver.
  6. `ServerSystem` from another system (e.g. a Crate refusal routed through chat) reaches only its owner.
  7. The view only asks (exact `Chat` request), never `ServerSend`s itself; `MirrorLine` shows a line.
- EditMode: the fixed link fields round-trip; text over 509 UTF-8 bytes is cut on a char boundary; `RpcSizeLintTests` for the two RPCs.
- Thursday hand test: two clients say / whisper / announce; a GM line; the chat log on both.

## 4. Contacts (next, not designed in detail here)

`PlayerContacts.cs` 317 + `ContactsService.cs` 383: 12 owner->server RPCs (Social limit 8/3), `world/contacts` world document (no player
save key), cross-player lookups by `NetworkObjectReference` -> by name / PlayerKey on DOTS, `OnlinePlayers` static -> server bodies.
Fills `SocialGate` (whisper / trade / party blocks). Design it like trade's by-name requests (8.2.3) once chat is in.

# Krytical Nova II online: the going-live runbook

Part of `online-roadmap.md`. Written 11 Oct 2026. **Nothing here exists yet.** These are the checklists
for Phase 8 (moving onto rented machines) and for running the game afterwards. Each list is filled in
with real names, addresses and commands as its phase is built; until then it says what must be true.

## 1. Before renting anything

- [ ] Phases 1 to 7 are done and their "done when" lists hold.
- [ ] The Linux server build starts from the command line with every setting given from outside.
- [ ] A search of the repository and of the client build finds no password, key or token.
- [ ] The save import tool has moved the test saves into a database and back.
- [ ] The caps from Phase 7 say how many map servers the first beta needs.

## 2. The machines (first beta)

| Machine | Runs | Faces the internet |
| --- | --- | --- |
| Game machine | The map servers and Lines, one process each | Yes: the game ports only |
| Service machine | The world service and the database | The world service's web port only. The database: never |
| Storage | Client downloads for the launcher; backups | Downloads: yes, read-only. Backups: no |

The two machines talk over the host's private network.

## 3. Firewall

| From | To | What | Allowed |
| --- | --- | --- | --- |
| Anyone | Game machine | The game ports (UDP, one per map server) | Yes |
| Anyone | Service machine | The world service's public web port (encrypted) | Yes |
| Game machine | Service machine | The world service's private port | Yes, private network only |
| Service machine | Database | Local only | Yes |
| The owner's address | Both machines | Remote login, by key | Yes |
| Anyone | Anything else | — | No |

- [ ] A scan from outside shows only the first two rows answering.
- [ ] Remote login accepts keys only; no password login; no login as the administrator account.
- [ ] The machines install security updates.

## 4. Floods (DDoS)

- [ ] The host's flood protection covers UDP on the product bought, in writing.
- [ ] The owner knows what the host does during an attack and how to reach them.
- [ ] The world service's public port sits behind a standard web protection service.
- [ ] Connection approval has a time limit and a limit on connects per address (Phase 2).
- [ ] The game machine's address is given to clients by the world service, so it can be changed without
      a new client build.

## 5. Secrets

| Secret | Lives on | Never in |
| --- | --- | --- |
| Database password | Service machine | Repository, client, map servers |
| The key that signs travel tickets | Service machine (map servers hold only what is needed to check a ticket) | Repository, client |
| Unity service-account key | The machines that need it | Repository, client |
| Transport certificate key | Game machine | Repository, client |
| Payment keys (Phase 10) | Service machine | Repository, client, map servers |
| The key that signs the launcher's update list | Offline, with the owner | Any server |

- [ ] Each secret can be replaced without a new build, and the steps are written here.

## 6. Start and stop order

Start: database, world service, map servers, then open arrivals.
Stop: close arrivals, announce a countdown, stop map servers (each saves everyone), world service,
database.

- [ ] A supervisor restarts any process that dies.
- [ ] Stopping a map server always saves its players first.

## 7. Backups

- [ ] A full database copy every night, kept 14 days, stored off both machines.
- [ ] A restore onto staging has been done, timed, and its steps written here.
- [ ] Before every patch day, an extra copy.

## 8. Watching it

For each map server: up or down, players, frame time, memory. For the world service: up or down, sign-ins,
refused connects, failed saves. For the database: disk space, last backup.

- [ ] An alert reaches the owner's phone for: a process restarting, a server missing 30 ticks for a
      minute, a failed save, a failed backup, disk nearly full, a jump in refused connects.
- [ ] Logs are kept off the game machine for 30 days.

## 9. Patch day

1. Build once; number the build; run it on staging; run the attack tests and a short bot soak.
2. Notice in game and on the status page.
3. Extra backup.
4. Close arrivals; countdown; stop map servers.
5. Roll the server build; run save migrations; start; sign in with a test account on each rail.
6. Publish the client build to Steam and to the launcher's storage; raise the required build number.
7. Open arrivals; watch for an hour.

Rollback: stop; previous numbered build; restore the extra backup if a migration ran; start.

## 10. When something goes wrong

| What | First | Then |
| --- | --- | --- |
| A flood | Confirm with the host; do nothing rash to the servers | Change the game address through the world service if one machine is singled out |
| An item or Creds duplication | Close the affected feature (trade, Kiosk, the shop) by settings | Find it in the ledger; fix; remove the copies; say what happened |
| Data loss or a bad migration | Stop arrivals | Restore; tell players what was lost and to when |
| A stolen account | Lock it | Check the ledger; return what can be shown; it was the sign-in provider's password, not ours |
| A leaked secret | Replace it (section 5) | Find how it leaked |
| A break-in to a machine | Take it off the network | Rebuild from a clean image; replace every secret; legal advice on what must be reported and to whom |
| The world service is down | Map servers keep their players playing | Restart it; cross-server chat and travel return |

## 11. Before the first stranger plays

- [ ] Every list above is ticked.
- [ ] `online-security-model.md`: every row of the audit is reviewed.
- [ ] Terms and privacy policy reviewed and shown in game.
- [ ] The live GM list contains no command that creates wealth.
- [ ] The owner has read this document through once with the machines in front of him.

# Moonkov Long Term Project Plan

Version: v0.3.1 Markdown synchronization  
Date: 2026-10-10. All milestone dates are in 2026, Beijing time (UTC+8), except explicitly labeled verification timestamps.  
Companion: [简体中文](Moonkov_Project_Plan.zh-CN.md) · [Reading index](README.md)

This is the complete in-project design and execution reference for developers and agents. Converted from the 24-page Moonkov Long Term Project Plan v0.3, it retains the goals, rationale, system requirements, milestones and daily plan, acceptance, team roles, risks, long-term roadmap and sources, with the subsequently confirmed alien wall/ceiling requirement. It is not a progress summary or a claim that all planned features have been implemented.

The recommended maintenance policy is to use Chinese as the canonical version for requirement meaning. If the languages conflict, record the discrepancy, ask the project owner to resolve it, then update both files; do not expand scope unilaterally. This is a document-maintenance recommendation, not a new gameplay decision. Implementation must also consult current repository code, applicable project guidance and module documentation. Report drift between older documentation and code. Audit conclusions linked to fixed commits apply only to those versions.

## MK-STATUS Status and evidence conventions

- [APPROVED]: A goal, constraint or authorization explicitly confirmed by the user; not an implementation-complete claim.
- [PROPOSED]: A recommended implementation, schedule, parameter or responsibility. Except for explicitly delegated first-prototype rule design, this does not authorize undecided gameplay changes.
- [OPEN]: A decision or validation condition remains unresolved.
- [USER-REPORTED]: A successful real-machine test reported by the user. State when its exact build and conditions still need archiving; do not present it as a cloud reproduction.
- [REPO-REPORTED]: Local runtime results reported in repository commits or documentation, not independently reproduced for this plan; retain their precise coverage and limitations.
- [VERIFIED-STATIC]: Source, remote state or static checks have been inspected; this does not establish runtime behavior.
- [VERIFIED-DOTNET]: Cloud .NET compilation or pure-C# checks actually executed. Coverage is limited to the stated source, commit and environment; this is not Unity, scene, multiplayer or performance acceptance.
- [HISTORICAL]: An audit or status from the original v0.3 fixed baseline. For changing facts, use the dated snapshot and subsequent evidence.
- [SUPERSEDED]: Explicitly rejected or replaced; not a candidate that can simply be resumed.

Stable requirement IDs below are for issues, PRs, tests and bilingual comparison. The same ID has the same meaning in both files; section IDs locate the complete discussion. Wording such as “recommended,” “pending confirmation” and “confirmed by the user” remains binding even without a repeated tag. See MK-BASELINE for E0–E4 evidence levels. Evidence strength and approval status are separate dimensions.

## MK-REQUIREMENTS Stable requirement index

| ID | Status | Requirement and boundary | Full section |
| --- | --- | --- | --- |
| MK-G01 | APPROVED | Unity client-programming portfolio, long-term personal work and club project; no near-term commercial release, with release-quality presentation and reliability | MK-GOALS |
| MK-G02 | APPROVED | Interim presentation October 24; final live project presentation November 8 | MK-MILESTONES, MK-ACCEPTANCE |
| MK-G03 | APPROVED | Windows, first person, solo PvE and LAN co-op for up to 5 players; full raids capped at 30 minutes | MK-NETWORK, MK-ENEMIES |
| MK-G04 | APPROVED | Free assets, no outsourcing; paid plugins above CNY 50 excluded, with purchase approval still required below that cap | MK-GOALS |
| MK-G05 | APPROVED / OPEN | Open-source approaches may be studied or carefully reused after checking specific licenses, attribution and redistribution boundaries; no particular unchecked dependency is approved | MK-GOALS |
| MK-M01 | APPROVED | A, equipment/weapon progression, as the main motivation, plus a small amount of C, exploration/collection; reuse existing inventory, shop and settlement | MK-MOTIVATION, MK-PROGRESSION |
| MK-M02 | APPROVED | Death loses all equipment and supplies carried into that raid; fallback revolvers can be claimed without a claim-count limit | MK-ENEMIES, MK-PROGRESSION |
| MK-M03 | PROPOSED / OPEN | Non-tradable/zero resale value for fallback items, initial charge and transfer restrictions remain undecided | MK-QUALITY, MK-DECISIONS |
| MK-M04 | APPROVED | No seasonal wipes, player trading or competitive rankings; quests can come later and halo modification remains design-only for now | MK-MOTIVATION, MK-LONGTERM |
| MK-W01 | APPROVED | One lunar map with at least two building complexes; start with replaceable logistics and research/mining grayboxes | MK-QUALITY |
| MK-W02 | APPROVED | 3 MMD characters with potentially different skeletons; verify retargeting, attachment points, collision and physics for each | MK-QUALITY |
| MK-W03 | APPROVED | Five halo weapon types: revolver, assault rifle, shotgun, sniper and large-area explosive rocket; polish the existing four and integrate the new fifth | MK-QUALITY |
| MK-W04 | PROPOSED / OPEN | Ammunition-conversion modules are only a candidate; rocket radius, occlusion, falloff, self/friendly damage and body-part allocation need decisions | MK-QUALITY, MK-DECISIONS |
| MK-E01 | APPROVED | A fully combat-capable alien: black, four long tapered legs, minimal or no visible torso, low posture and rapid ambush/flanking | MK-ENEMIES |
| MK-E02 | APPROVED | Floor, wall and ceiling locomotion and transitions are core alien goals; ground-only motion, prescribed routes or a gait showcase cannot substitute for them | MK-ENEMIES, MK-ACCEPTANCE |
| MK-E03 | PROPOSED | Validate staged sandboxes, then integrate real terrain, perception, attacks, damage, death and LAN; confirm any reduction first | MK-ENEMIES, MK-RISKS |
| MK-H01 | APPROVED | Seven parts: head, chest, abdomen, left/right arms, left/right legs, plus hunger, thirst and outdoor oxygen; high priority | MK-HEALTH |
| MK-H02 | APPROVED / PROPOSED | First-prototype consequences for depleted body parts/oxygen have been delegated; the documented rules are configurable prototypes, with values and intensity to be tuned through play | MK-HEALTH |
| MK-H03 | PROPOSED / OPEN | Initialize health on deployment for the first version; persistent injuries across raids are not approved, and item retention does not imply injury persistence | MK-HEALTH, MK-DECISIONS |
| MK-H04 | APPROVED / PROPOSED | Sensory feedback supports immersion and readable danger; validate specific HUD/audio intensity and accessibility treatment under the first-prototype recommendations | MK-HEALTH |
| MK-N01 | APPROVED | Preserve successful solo and 2-player Host paths and per-player saves; separately accept 5-player scale and failure paths | MK-NETWORK |
| MK-N02 | PROPOSED | One server authority, exactly-once death/settlement and atomic medical consumption; reuse rather than rewrite existing persistence | MK-NETWORK, MK-HEALTH, MK-TESTING |
| MK-P01 | APPROVED / OPEN | Laptop RTX 4060, 1440p, 80 fps target, DLSS allowed; CPU/power/quality/DLSS availability and mode/frame generation remain to be specified | MK-PERFORMANCE |
| MK-D01 | PROPOSED | Apart from the two fixed presentation dates, phases, daily sequencing and November 2–7 feature freeze are recommendations, not completion commitments | MK-MILESTONES, MK-DAILY |
| MK-T01 | APPROVED | Low-risk fixes may be merged autonomously after verification; gameplay and major architecture changes need user confirmation; runtime-unverified PRs await acceptance | MK-TESTING |
| MK-T02 | APPROVED | Daily reports at 10:00 Beijing time, plus milestone-completion updates; progress must be evidence-backed | MK-MAINTENANCE |
| MK-T03 | PROPOSED / OPEN | Team roles, deliverables and handover dates need owner confirmation and are not assignments already sent to teammates | MK-TEAM |
| MK-A01 | PROPOSED | Playable build, live demo script, backup video, technical explanation and evidence; distinguish individual/team/AI/third-party contributions | MK-ACCEPTANCE |

## MK-SNAPSHOT Dated status snapshot

Verification window: 2026-10-10 06:12–06:20 UTC (14:12–14:20 Beijing time). This section records changing facts separately. It does not rewrite the goals below or promote proposals into approved requirements. Recheck the remote repository and exact tested version before subsequent work.

- [VERIFIED-STATIC] main is [f749a56](https://github.com/MorrowHome/Moonkov/commit/f749a56767bf411b9160c9477b0a4afe3ae36ba7). Since the original a4ede5e audit baseline, three commits first added weapon gestures/aim smoothing, lunar terrain/lighting and weapon audio/impact feedback (through d9fd944), followed by two commits adding the Cinemachine third-person camera and background depth of field. Code being on main does not mean the author of this plan personally verified Unity runtime behavior or target-machine performance.
- [USER-REPORTED] The complete solo loot-fight-extract loop, item retention, 2-player Host while the host also plays, and per-player extraction/death saves succeeded. This does not automatically validate 5 players, new health, new aliens or combined paths.
- [SUPERSEDED] [Camera PR #2](https://github.com/MorrowHome/Moonkov/pull/2) is closed and unmerged. The user [explicitly rejected](https://github.com/MorrowHome/Moonkov/pull/2#issuecomment-6094373539) its camera embedding in the head and exposing the model interior, and reported that a local Codex Cinemachine approach would take over. The subsequent Cinemachine implementation is now on main, but the old PR must not remain a mergeable fix candidate.
- [REPO-REPORTED] [Current camera documentation](https://github.com/MorrowHome/Moonkov/blob/f749a56767bf411b9160c9477b0a4afe3ae36ba7/Docs/DollSingerThirdPersonCamera.md) and commits report successful Unity compilation, focused collision/near-plane/self-filtering/recovery checks, focus-volume isolation and cleanup, and local DollSinger Play checks. Formal lunar-map operation, Host/Client, crouch/prone feel and GPU performance still need actual acceptance. This local report is recorded separately from the pure-.NET evidence below.
- [VERIFIED-STATIC] PRs #3–#12 remain open, unmerged drafts. None of the isolated modules or sandboxes below constitutes a claim that production gameplay is integrated.

| Work | Exact version and evidence | Established result | Outstanding gates |
| --- | --- | --- | --- |
| Backend checks PR #3 | [a7e3073 and build report](https://github.com/MorrowHome/Moonkov/pull/3#issuecomment-6094517301) | [VERIFIED-DOTNET] All 6 backend projects on d9fd944 compiled; MoonPersistence and ShopChecks also compiled on main plus this PR patch, all with 0 warnings/errors. 9 static checks passed and 10 negative source mutations were detected | Not validation of a complete PR-head checkout; no services, Checks executables, PowerShell, PostgreSQL, SQL cleanup or HTTP integration ran; no Unity execution |
| Two-complex graybox PR #4 | [9369891](https://github.com/MorrowHome/Moonkov/pull/4) | [VERIFIED-STATIC] Reversible Editor-tool candidate with reference geometry checks | No Unity import/generation, real-terrain placement or playable-map integration; terrain and production lunar-scene integration await acceptance |
| Seven-part/oxygen pure rules PR #5 | [96f1c28 and execution report](https://github.com/MorrowHome/Moonkov/pull/5#issuecomment-6094379351) | [VERIFIED-DOTNET] 214 actual C# checks passed | No runtime callers; Unity, hit mapping, medical, replication, UI/audio and save integration remain unaccepted |
| Alien sandboxes PRs #6, #7, #8 | [Ground foundation](https://github.com/MorrowHome/Moonkov/pull/6), [prescribed adhesion route](https://github.com/MorrowHome/Moonkov/pull/7), [bounded observed-information ambush](https://github.com/MorrowHome/Moonkov/pull/8) | [VERIFIED-STATIC] Staged source and reference-geometry candidates; dependency chain #6→#7→#8 | Prescribed routes are not autonomous navigation; Unity import/Play, real physics/visual quality, actual map and LAN are unverified |
| Alien attack contract PR #9 | [5508fb8](https://github.com/MorrowHome/Moonkov/pull/9) | [VERIFIED-DOTNET] 35 groups of actual C# checks passed; depends on #8 | Only isolated attack timing and a dummy-health receiver, not proof of real-player combat or server authority; Unity/multiplayer unverified |
| Targeted medical PR #10 | [63e3d9b](https://github.com/MorrowHome/Moonkov/pull/10) | [VERIFIED-DOTNET] 59 medical checks and 214 health regressions passed; based on #5 | Pure shared transaction wrapper without RPC/ECS runtime integration; Unity, concurrent commits, network replay protection and real medical use remain unaccepted |
| Alien dummy-target combat PR #11 | [db83e7e](https://github.com/MorrowHome/Moonkov/pull/11) | [VERIFIED-STATIC] Isolated dummy-target combat connected to the sandbox; depends on #9 | Unity, real damage/death, actual map, server and LAN remain unaccepted |
| Hunger/thirst pure rules PR #12 | [4bfd86d](https://github.com/MorrowHome/Moonkov/pull/12) | [VERIFIED-DOTNET] 181 nutrition and 214 health checks passed; a separate C# review passed another 2,010 checks; based on #5, independent of #10 | No runtime callers; combined oxygen/nutrition scheduling is not implemented, so module passes are not a complete survival-system pass; Unity/UI/audio/items/saves remain unaccepted |

These pure-C# executions used cloud Linux x64 and .NET SDK 10.0.401. Each check count belongs to its module/commit; do not add them together and label the total a whole-game pass. Source review, Python reference geometry, actual C# execution, Unity runtime behavior and user experience are different evidence.

## MK-CHANGELOG Version changes and provenance

- v0.3, 2026-10-10: Complete 24-page plan incorporating user confirmations on goals, the existing loop, equipment progression plus light exploration, map, health, five weapon types, characters, aliens and performance. Original fixed audit baseline: a4ede5e.
- v0.3.1 Markdown, 2026-10-10: The user requested complete Chinese and English Markdown copies in the project for agents. This preserves v0.3 substance and adds stable IDs, status distinctions and a separate dated snapshot.
- Confirmed by the user after v0.3: black, four long tapered legs, minimal or no visible torso, rapid ambush/flanking; floor, wall and ceiling movement and transitions are core scope. MK-E01, MK-E02, alien gates and final acceptance are updated accordingly.
- The user permitted careful study/reuse of open-source approaches. Specific license-check boundaries are added; this does not approve arbitrary dependencies, purchases or redistribution.
- Changing facts corrected: the old camera PR was rejected and closed; a local Cinemachine direction took over; the replacement is on main with limited local Unity checks reported in the repository; draft publication and .NET checks supersede the original historical “no PRs/no C# execution/write blocked” status. Only the relevant evidence is updated; focused checks are not generalized into full Unity, formal-map or production-integration acceptance.
- No feature scope, effort estimates or implicit approvals have been added.

## MK-SUMMARY Conclusions First

Moonkov is a high-quality portfolio project aimed at Unity client-side development roles, with an emphasis on demonstrating programming ability. It is also a long-term personal project and a club project. It should demonstrate broad technical ability, but the portfolio narrative should center on client-side implementation and integration. There are no plans for a commercial release in the near term; the goal is release-quality presentation and reliability. The midterm sharing session is on October 24, and the final project presentation is on November 8. Prepare for a live, hands-on demonstration.

The user has successfully tested the complete single-player loot-fight-extract loop, item retention, two-player Host multiplayer, and independent saves for each player after extraction or death. Next, establish a reason to loot through equipment progression as the main focus, supplemented by a small amount of exploration and collection, and provide tactical space across at least two building complexes. Health, hunger, thirst, and outdoor oxygen are high-priority risk systems. Do not rebuild the existing loop or multiplayer from scratch.

This version records the user-reported success of the complete single-player loop, item retention, two-player Host multiplayer, and per-player saving after extraction or death. Confirmed decisions include equipment progression as the main focus with a small amount of exploration and collection, losing all items carried in the current raid on death, unlimited claims of a fallback revolver, grayboxes for two base complexes, a seven-body-part survival system, a fully functional ambushing and flanking alien, and a fifth weapon type: a large-area explosive rocket weapon. The assistant will define the first prototype rules for health consequences; exact values remain tunable, while the ammunition-type scheme and some explosion rules still need confirmation. The schedule is a proposal, not a claim that code is complete.

| Confirmed Goal | Treatment in This Version |
| --- | --- |
| October 24 and November 8 | Hard milestones for the midterm sharing session and final project presentation, respectively. |
| Solo and team PvE | The primary gameplay direction; LAN cooperation is required for the finished project. |
| Existing runtime progress | The user has verified the single-player loop and item retention. Two-player Host play, with the host playing while hosting, and independent saving after extraction/death have been tested. Five players remain to be verified. |
| Map and content | A lunar base with two building complexes, three characters, five types comprising revolver/assault rifle/shotgun/sniper/large-area explosive rocket, and a complete combat-capable alien. |
| Up to 5 players; up to 30 minutes per raid | Upper limits for scale and pacing; prepare a separate short route for the demonstration. |
| Core gameplay decisions | First person; equipment progression as the main focus with a small amount of exploration and collection; death loses all items carried in the current raid; unlimited claims of a fallback revolver; ammunition/charging rules still need detail. |
| Health priority | Seven body parts: head, chest, abdomen, left and right arms, and left and right legs, plus hunger, thirst, and outdoor oxygen, are major near-term work. |
| Performance target | Windows, laptop RTX 4060, 1440p, DLSS allowed, target 80 fps; CPU, quality preset, frame generation, and validation conditions still need clarification. |
| Low-cost collaboration | Free assets, no outsourcing, LAN service; plugins over CNY 50 are excluded, and purchases need separate confirmation. |
| Engineering authorization | Low-risk fixes may be merged autonomously after verification; gameplay and major architecture changes require user confirmation; PRs not verified at runtime await acceptance. |

### Core Recommendations for Version 0.3

- Connect equipment progression and a small amount of exploration and collection to the existing inventory, shop, and raids, giving resources clear uses. Death loses all equipment and supplies carried in that raid; the fallback revolver can be claimed without limit. Specify charging and ammunition-type rules separately so the fallback does not undermine the economy.

- Before October 24, prioritize turning grayboxes of the logistics building complex and the research/mining building complex into a playable base, while advancing a minimal but complete implementation of seven-body-part health, hunger, thirst, and outdoor oxygen. Motivation, health risks, and the map must be tested together rather than becoming isolated demonstrations.

- A fully combat-capable non-humanoid IK alien is within the target scope. First validate locomotion and attacks in a separate experimental scene or sandbox, then integrate it into the main project, map, and multiplayer. A standalone gait demonstration cannot substitute for a complete enemy.

- Aim to complete content acceptance by November 1, then freeze new features from November 2 through 7 to focus on stability and portfolio preparation. Any reduction in scope or deferral of content that fails to meet the target must first be put to the user for a decision.

## MK-GOALS 1 Goals, Constraints, and Scope

### 1.1 Player Experience Goals

Players should experience solid-feeling, immersive combat with rich sound, fluid and elegant movement, and beautiful visuals in a believable lunar environment, while engaging in an ongoing psychological contest with the environment and enemies. The overall tone is hardcore and tense, taking gameplay inspiration from Escape from Tarkov PvE and considering elements of the cooperative experience in Helldivers 2. Art direction references Arknights and Arknights: Endfield. These are creative references, not commitments to reproduce their systems one by one.

The project's distinctive identity is not a new theme still to be found: work has already gone into the Moon, anime-style characters, halos, and gestures. The near-term priority is to bring these three pillars to a consistent level of quality within the same playable experience, rather than continuing to add unvalidated distinguishing features.

### 1.2 Established Constraints

- Start with Windows. Plan multiplayer for LAN only; do not make commitments to ongoing public-internet operation.

- The user can invest substantial time, but reliably available working hours have not yet been quantified. The modeling collaborator is learning while working and has relatively low output. Building concept art already exists, but deliverables and delivery dates are not yet certain. A separate 2D design collaborator handles UI, weapon design, and illustration. Most other work is being done by the user in collaboration with AI.

- No outsourcing, no non-free assets, and no consideration of paid plugins costing more than CNY 50. CNY 50 is a screening cap, not purchase authorization; the cumulative budget remains to be clarified.

- According to the user, the existing audio comes from official free Sonniss bundles. The characters make extensive use of free MMD resources licensed for noncommercial use. Keep records of each resource's actual license for demonstrations, recordings, and portfolio use. Do not expand near-term work into a commercial-release process.

[APPROVED / OPEN] Open-source approaches may be referenced or carefully reused, but first check their specific licenses, attribution requirements, modification/redistribution conditions, and compatibility with the project. No license does not mean unrestricted use. Do not directly include code or assets with unclear licensing in the public repository. Check licenses for noncommercial MMD resources separately from code licenses. Making source code public does not automatically grant redistribution rights for all assets. No specific third-party dependency or implementation with unclear licensing is currently approved.

### 1.3 What Release-Quality Means at This Stage

The recommendation is to apply release-quality standards to the selected demonstration slice: reliable entry and exit, clear controls and feedback, consistent primary visuals and sound, no conspicuous character or camera flaws, no lost or duplicated asset settlement across a complete raid, and recorded performance on the target device. The extensive content, platform coverage, and ongoing operation required of a full commercial product are not implicitly promised within these 29 days.

The final project presentation target retains one map containing two building complexes, three characters, five mechanically distinct weapon types, humanoid and complete non-humanoid enemies, and the high-priority health system. This is already a substantial scope. The level of polish and allocation of effort must be reassessed against the midterm results. Any scope reduction or deferral remains the user's decision; do not silently turn it into a placeholder delivery.

## MK-BASELINE 2 Implementation Baseline and Review Scope

| Item | Current Baseline | Planning Implication |
| --- | --- | --- |
| Code repository | MorrowHome/Moonkov; main verified as f749a56767bf411b9160c9477b0a4afe3ae36ba7 at 2026-10-10 06:19 UTC | The detailed audit in this section comes from the older a4ede5e baseline; see the separate dated snapshot for subsequent commits and PR status. |
| Client | Unity 6000.5.10f1; URP 17.5; Input System 1.20 | Keep the current versions; evaluate and regression-test upgrades separately. |
| Networking | NetCode for Entities 6.5; GhostBridge | An architecture of authoritative ECS simulation and GameObject presentation already exists. |
| Backend | A separate .NET 10 and PostgreSQL persistence backend | Transactions, row locks, idempotent receipts, and an outbox are already implemented. |
| Runtime verification | [USER-REPORTED] The user has tested the complete single-player loop, item retention, two-player Host multiplayer, and per-player saving after extraction/death | Use successful paths as the regression baseline; verify five-player, new-health, and new-enemy paths separately. |
| Repository governance | [HISTORICAL] At the initial audit: one open Issue, zero PRs, no workflows or run records; no protection or ruleset on main | These counts are not current status. See the dated snapshot for current draft PRs and verification boundaries. There is not yet evidence that a trustworthy quality gate for automatic merging has been established. |
| Camera Issue 1 / PR #2 | [SUPERSEDED] The user rejected the old candidate: the camera was embedded in the head and exposed the inside of the model. The local Codex Cinemachine solution has taken over | The old patch is no longer a directly mergeable fix candidate. The replacement Cinemachine approach is now on main; the repository reports successful local Unity checks, while formal-map/Host/Client acceptance remains outstanding. |

The detailed system inventory and gaps below come from a static review of fixed commit a4ede5ee67d799808162cc39e5e2930ce2a75045, not a new audit of all subsequent commits. The user later reported successful tests of the complete single-player loop, item retention, two-player Host multiplayer, and per-player saving after extraction or death. See the dated snapshot for the pure C# and compilation checks subsequently run in the cloud; they are not Unity or multi-client retests. The specific builds and conditions used for the user's tests still need to be recorded.

### 2.1 Source-Code System Inventory

| Existing Implementation | Known Scope | Evidence Entry Point |
| --- | --- | --- |
| Game flow | MainMenu enters the lunar MoonGameScene; GameScene is used for menu previews | Gameplay/GameManager and UI/Game |
| Online and offline | Host, Client, dedicated server; separate offline raids and local saves | Networking and Docs/SinglePlayer.md |
| Map and AI | Lunar map; AI PMC looting, combat, and extraction | MoonEnvironment; DollSinger; Docs/AI/DollSingerEnemies.md |
| Combat and medical systems | Four halo weapon types; player- and medical-related implementations | Gameplay/Player and Gameplay/Weapon |
| Items and corpses | Nested containers, equipment, shared crates, death corpses, and corpse looting | Docs/ContainerInventory.md; Docs/DeathLoot.md |
| Settlement and economy | Extraction, settlement, shop, and emergency packs | Docs/Shop.md; Docs/PostgreSQLPersistence.md |
| Persistence | Server authority, shared rules, transactions, row locks, idempotency, and outbox | Backend/MoonPersistence |
| Checking tools | 13 Editor Checks scripts and five backend Checks projects; a test framework package is present | Assets/Editor; Backend; no first-party test assemblies or CI gate were found |

The main source code is under Assets/Scripts/Networking/{Client,Server,Shared}, Assets/Scripts/Gameplay/{Player,Weapon,GameManager}, and Assets/Scripts/UI/Game. DollSinger and MoonEnvironment have separate Runtime, Editor, and other assemblies. The existing architecture already contains substantial real implementation; there is no evidence supporting a rewrite merely to make the plan look complete.

### 2.2 Gaps Found in the Initial Static Audit

- [HISTORICAL] Static mismatches were found in the checks at a4ede5e: ShopChecks asserted four products while ShopModel already had six; Smoke-Test cleanup still deleted stashes, even though the schema had already defined it as an aggregate view, and did not cover the new foreign-key tables. PR #3 proposes fixes. See the dated snapshot for compilation and static-check progress; integration with real services and the database still needs verification. Make the checks trustworthy before establishing the test baseline.

- [HISTORICAL / OPEN] The initial audit identified a risk in public-internet Direct multiplayer: a long-lived account token was placed in the join-game RPC, and the existing driver did not configure corresponding transport protection. HTTPS on the account service does not mean that game transport is protected. A remediation plan must be approved and verified before public multiplayer. This plan does not publish actual credentials.

- [HISTORICAL / OPEN] The initial audit identified a production identity-validation risk: when moon-server.local.json is missing, persistence is disabled and the corresponding join branch does not validate LoginToken. This is a prototype fallback. Production deployment needs an explicit rule rejecting incomplete configuration rather than retaining an unauthenticated downgrade.

- Recovery and end-to-end evidence still need to be completed. The user has since reported successful runtime tests of two-player Host raids and per-player saving after extraction or death. The outbox depends on the original server's disk; cross-device recovery, abnormal restarts, load testing, target performance, and backup restoration have not yet been fully validated. Storing bearer tokens in PlayerPrefs and the absence of cancellation/timeouts during scene loading are risks found through static review; they have not yet been reproduced at runtime.

- [VERIFIED-STATIC] As of d9fd944, Docs/AI/UnityProjectContext.md still describes the project as having no database or AI, and README still contains FPS-template information. The initial audit also recorded template information in the build identity. Documentation updates must match code facts. This plan does not replace checking the runtime instructions item by item.

### 2.3 Verification Evidence Levels

- E0 Documented intent: a README, design draft, or comment describes a goal, but there is no implementation evidence.

- E1 Static implementation: code, assets, or configuration exist, and static review has either passed or identified specific problems.

- E2 Tool verification: compilation, automated tests, service integration, or asset validation pass in a recorded environment.

- E3 Scenario verification: a specified gameplay path is completed in the target build, with logs, recordings, or test records.

- E4 Delivery verification: target users complete the agreed experience validation on target devices, and release and rollback procedures are executable.

The same system can have different evidence levels. For example, the user has verified single-player extraction and item retention, while settlement after disconnections in a five-player session is still unconfirmed. AI capabilities present in source code also need an in-map experience review. For each result, record who tested it, which version was tested, and under what conditions; do not hide these differences behind an overall completion percentage.

## MK-MOTIVATION 3 Looting Motivation and Demonstration Scope

The existing loop is already playable. Equipment progression as the main focus, a small amount of exploration and collection, two building complexes, and health risks are intended to supply the missing motivation. The proposed P0 is these changes to the complete loop plus fixes for blockers. P1 is characters, refinement of the four existing weapon types, and the complete IK enemy, followed by integration of the fifth type: the large-area explosive rocket weapon. The five-weapon and complete-enemy targets remain. Advancing health, the new weapon, and the alien at the same time significantly raises scheduling risk.

### 3.1 Confirmed Looting Motivation

[APPROVED] The chosen motivation is mainly A with a small amount of C: A is weapon and equipment progression, and C is exploration and collection. Weapon and equipment progression is the main focus, with exploration and collection as a lightweight supplement. First use the existing stash, shop, items, and settlement to give loot uses that players can understand. Show a goal before departure, let risk and resource distribution influence choices during the raid, and turn extracted loot into visible gains and preparation for the next raid. Confirm the specific items, unlock or purchase relationships, and values in a pre-implementation design checklist; do not automatically expand this into complex quest or modification systems.

Acceptance should focus on whether players can explain what they are looking for before departure, whether value, injuries, hunger, thirst, and oxygen cause them to change routes during a raid, and whether they use their gains after extraction and develop a goal for the next raid. Exploration and collection should provide the pleasure of recognition and discovery. Personal performance records are part of the feedback, but cannot alone provide a use for loot.

| Area | User Goal | Proposed Near-Term Acceptance |
| --- | --- | --- |
| Map | A lunar surface reconstructed from public NASA height data; at least two building complexes | Use a lunar-base concept for the first prototype, with routes, risks, and rewards linking the complexes. The actual playable area should serve the experience rather than target terrain area. |
| Characters | Three MMD-sourced characters, potentially with different skeletons | Validate animation compatibility, hand attachment points, collision, and physics individually. Treat script generality as unverified, and do not assume import is simple. |
| Halo weapons | Revolver, assault rifle, shotgun, and sniper designs already exist; the fifth is a large-area explosive rocket weapon | Continue refining the four existing designs; the rocket weapon retains the halo/gesture system and requires validation of explosions, feedback, and multiplayer. |
| Enemies | Humanoid AI and a complete combat-capable multi-legged alien | Migrate the alien from a separate sandbox into the main project and complete verification of perception, locomotion, attacks, hit reactions, death, and networking. |
| Health | Seven body parts, hunger, thirst, and outdoor oxygen | Body-part damage, medical treatment, status consequences, indoor/outdoor transitions, HUD and audio, synchronization, and saving must be consistent. Values remain parameters for tuning through runtime tests. |
| Modes | First person; solo PvE and LAN cooperation for up to five players | Regression-test the already tested two-player Host flow and per-player saves. Test new health, weapons, and enemies across multiple clients, then verify five-player scale. |
| Progression | Equipment progression as the main focus, a small amount of exploration and collection, and personal career records | Clarify uses for loot and goals for the next raid. Keep modification at the design stage for now, and make health resources consistent with the economy. |
| Technical showcase | Unity client-side programming ability and broad technical integration | Center on real client-side problems, implementation, analysis, and verification; use the backend as evidence of understanding system boundaries and integration. |

### 3.2 Quality Benchmark

The recommendation is to establish a quality benchmark with one representative map route, one main character, and one polished halo with its gestures. It should include movement, observation, combat, pickup, and extraction feedback together, and serve as the acceptance reference for other content. This is a production method, not a reduction of the three-character, five-weapon target to one.

### 3.3 Explicitly Deferred Directions

Seasonal wipes, player trading, and competitive rankings have been explicitly ruled out. Quests can be added later. Halo modification will remain at the design-reservation stage this period and be decided after the motivation of the main loop stabilizes. Special items may be considered, while shooting remains the focus. Public-internet hosting, paid outsourcing, multiple platforms, and storefront release are outside this plan.

## MK-QUALITY 4 Three Core Quality Pillars

### 4.1 A Believable Moon

According to the user, the existing lunar surface was reconstructed from public NASA height data, and the terrain can be expanded. The actual gap is buildings and gameplay structure within the playable area. The first prototype uses a lunar-base concept with at least two building complexes. First define the roles of the two zones, connecting routes, risks, and resource distribution, then refine materials, lighting, and building presentation.

- The user has approved starting with two replaceable graybox building complexes: a logistics zone and a research/mining zone. They serve different functions and offer different resource attractions, connected by a choice of routes. First validate scale, cover, sightlines, entry and exit options, and risk/reward, then integrate the teammate's final models. Confirm specific rooms, resource tables, and gameplay details through the design checklist.

- The building collaborator already has concept art, but deliverables and progress are uncertain. Grayboxes can proceed first, using shared specifications for scale, collision, navigation, and resource interfaces so they remain replaceable later. Integration of final models requires another regression pass; do not assume that changing only the appearance has no other effects.

- Aim to make the main playable routes across both building complexes usable before October 20, leaving time for regression testing of sightlines, navigation, camera behavior, damage, indoor/outdoor oxygen, and sound transitions. Prioritize meaningful looting routes across the two zones rather than map area.

- The audio direction is established: indoor spaces contain air and use corresponding realistic sound feedback. Outdoors, equipment processing provides auditory information, with breathing, equipment sounds, and contact vibrations to strengthen immersion. The equipment need not be limited to a spacesuit and can fit the halo setting. There are currently no interiors, so zone transitions, occlusion, and mixing feedback must be designed alongside the buildings.

### 4.2 Anime-Style Characters and Physics

The user has completed MMD-to-Unity conversion and invested work in hair, skirt, and breast physics and clipping prevention. Different downloaded models are not guaranteed to share a skeleton. The existing scripts appear generic, but must still be checked for each character. First establish checklists for bones, animation retargeting, attachment points, collision, and physics configuration; do not treat adding characters as a simple model swap.

- Check skeleton compatibility and animation retargeting for each of the three characters, then cover standing, running, abrupt stops, turns, falling, attacking, taking hits, death, equipment switching, and close-up inspection. Save recordings, issues, and configuration differences for each character.

- Check bone and animation mappings, hand and halo positions, body/clothing intersections, physics jitter, resets, and scene transitions. In multiplayer, verify consistency between remote-character and local presentation.

- Performance testing must include multiplayer character physics and enemies together, rather than only a single-character preview scene. Distance-based degradation, update frequency changes, or simplification strategies are technical recommendations; confirm visually perceptible changes first.

- The 2D design collaborator should prioritize the layout and asset specifications most needed by the current UI, avoiding a redesign of every page within the short window. Character introductions, cover art, or illustrations may be used in the portfolio, but cannot replace playable quality.

### 4.3 Halos, Gestures, and Combat Feedback

The existing four types are revolver, assault rifle, shotgun, and sniper weapon. Their designs exist, but polish is uneven, with approximately one mature gesture. The fifth type is confirmed as a large-area explosive rocket weapon, retaining the halo-and-gesture presentation system. Separate the work into refinement of the existing four types and new integration of the fifth, rather than treating all five as designs starting from zero.

- Create design and quality cards for each of the five types covering mechanics, control cadence, resource costs, hit feedback, sound, gestures, effects, and shortcomings. Verify the implementation and quality of the existing four; complete explosion and networking rules for the new rocket weapon. Record the existence of a type, the existence of code, completion of polish, and runtime acceptance separately.

- Benchmark acceptance covers input-to-action response, coordination between gestures and halos, attack release, hit and damage reactions, stopping, and switching. Check fluidity, elegant poses, audiovisual timing, and enemy readability.

- Keep shooting as the primary focus. Halo modification has been left at the design-reservation stage pending confirmation after the main loop stabilizes. Do not currently include component slots, random affixes, recipes, or a modification economy in execution.

- For this period, record only the interfaces and data-version requirements that halo modification might affect; do not refactor preemptively for future modification. Prioritize the five weapons' existing mechanics, action quality, and playable differences.

### 4.4 Weapon Types and Polish Status

| Type | Design Status | Work This Period |
| --- | --- | --- |
| Revolver | Design exists; the fallback revolver can be claimed without limit | Refine controls and presentation. Recommend making the fallback item non-tradable with a sale price of zero to prevent arbitrage; exact rules and initial charge need confirmation. |
| Assault rifle | The user confirms a design already exists | Review sustained firing, gestures, feedback, and differentiation from other types. |
| Shotgun | The user confirms a design already exists | Review close-range use, hit presentation, gestures, and sound effects. |
| Sniper | The user confirms a design already exists | Review precision-shooting cadence, visual feedback, and fit with map sightlines. |
| Large-area explosive rocket | The direction of the fifth type is confirmed | Continue the halo/gesture expression; clarify blast radius, occlusion, falloff, self-damage/friendly fire, and resource cost. |

Type names describe gameplay design categories, not a claim of final production quality. Track separately the four types found in the source audit, the four types designed by the user, and the approximately one mature gesture. The fifth type, the rocket weapon, is additional scope. Confirm which mature gesture will serve as the benchmark through the user's actual demonstration.

### 4.5 Energy, Ammunition Types, and Explosions

The user wants halo colors to communicate different ammunition types, but the weapons are charged by an energy source, and how ammunition types fit into this remains undecided. One candidate is to separate energy supply from a conversion module that determines projectile properties, with colors and icons conveying the type together. This is a discussion proposal, not an approved implementation. Do not turn conventional ammunition boxes or a module system into settled decisions merely to meet the schedule.

Unlimited claims of the fallback revolver are confirmed. The recommendation is to exclude fallback weapons from selling or trading and give them a sale price of zero, preventing unlimited claims from generating unlimited profit. Initial charge and whether that charge can be transferred to other weapons also need clarification. These anti-arbitrage rules are recommendations, not user-approved economic rules.

The rocket weapon's large-area explosion requires validation of radius boundaries, line-of-sight occlusion, damage falloff, self-damage, and friendly fire. The server adjudicates hits and damage. A single explosion must not apply its full damage package repeatedly to the same character because multiple body-part colliders are hit. Define body-part allocation and occlusion strategies in configuration and tests. Specific gameplay rules still need confirmation.

There are currently no throwable items. Throwables are candidates for later planning; if needed, the recommendation is to start with one grenade and reuse the explosion foundation. No specific implementation is approved, and a complete throwable system must not be added as an implicit commitment for this period.

## MK-ENEMIES 5 Enemies, Pacing, and Psychological Play

### 5.1 Humanoid Enemies

AI PMC looting, combat, and extraction are already implemented. The near-term priority is to validate on the actual map whether they create understandable pressure: players should be able to observe, infer, detour, lure, and retreat, rather than being interrupted by attacks without warning or obviously stuck AI.

- Check transitions among perception, losing a target, pursuit, cover, movement, looting, and extraction in actual scenes. Use the existing implementation as the starting point for specific capabilities; additional tactics require user confirmation.

- Record each encounter along a fixed route: how the enemy detected the player, what clues the player had, which choices were available, and whether failure was explainable.

- Hardcore difficulty and tension should primarily come from risk, information, and the cost of decisions. Do not replace experience design with increased health, reduced resources, or higher accuracy without confirmation.

### 5.2 Procedural Multi-Legged Alien

[APPROVED] The fully combat-capable alien has a black form, four long tapering legs, and an extremely small or visually negligible torso. It stays low, moves quickly, ambushes, and uses complex terrain to flank from behind. The user later explicitly added that movement on floors, walls, and ceilings, and transitions between them, are all core goals. The final target cannot be reduced to floor-only walking. Its IK gait must be fluid, believable, and intimidating; it is not merely a procedural animation to watch. Research can begin in a separate sandbox, followed by integration of perception, locomotion, attacks, hit reactions, death, terrain navigation, and LAN state.

| Gate | Proposed Completion Criteria | Decision Needed If It Fails |
| --- | --- | --- |
| Feasibility | A graybox quadruped with an extremely small torso; contact, gait, turning, slopes, floor/wall/ceiling movement, and continuity around corners; record CPU cost. Staged prototypes may begin with partial capabilities, but unmet goals must be clearly labeled. | Whether to narrow supported terrain, gait, or enemy count; it must not automatically become a static enemy. |
| Combat integration | Fully connect perception, fast ambushes and flanking from behind, movement, attacks, hit reactions, and death; migrate into the main project and validate LAN results. Preset surface-attachment routes are not equivalent to autonomous navigation or flanking. | If time is insufficient, present specific schedule or complexity tradeoffs; do not default to a non-combat demonstration. |
| Presentation polish | Center of gravity, step frequency, foot sliding, slope and wall/ceiling transitions, sound, and attack cues should form a coherent feel. | Make explicit tradeoffs among the effect, performance, and covered scenarios. |

[PROPOSED] Validate low, fast locomotion, ambush initiation, navigation around complex terrain, floor/wall/ceiling transitions, and attacks in a separate sandbox as early as possible. First ensure one complete enemy behavior, then refine the sense of horror and gait. Focus on migration into the main map and multiplayer regression from October 25 through 29, with acceptance before November 1. Clarify interfaces, assets, and network dependencies between the sandbox and main project in advance.

### 5.3 Raid and Demonstration Pacing

The regular experience has a maximum duration of 30 minutes and a team size of up to five. For the demonstration, prepare a shorter route that reliably triggers the main highlights while keeping the full raid playable. A short demonstration does not change the regular raid duration, and editing must not conceal an unusable core loop.

The death rule is settled: all equipment and supplies carried in the current raid are lost, and the fallback revolver can be claimed without limit. The out-of-raid stash and successfully extracted assets retain the existing persistence path. Unlimited claims do not automatically mean unlimited sale proceeds or unlimited transferable energy; the relevant economic rules must be clarified.

## MK-NETWORK 6 Single Player, LAN Cooperation, and Data

The single-player PvE loop and item retention have been runtime-tested. Cooperation has been tested with two players, with the Host also playing, and both players' extraction and death results saved independently. The next priority is to preserve this successful baseline, validate new systems, and expand to up to five players, rather than rebuild the cooperative flow. No public-internet service will be deployed this period.

- Turn the successful two-player Host flow into a regression case: record the build, machines, actions, extraction/death outcomes, and independent save results. The scale, load, and disconnection scenarios for up to five players still require runtime tests. Do not extrapolate two-player results into complete coverage.

- LAN acceptance covers joining a room, character spawning, movement and combat, AI, inventory, shared containers, death and corpse looting, extraction, and result persistence.

- Validate client departure, disconnection, host or service shutdown, repeated requests, and recovery after restart. Define what players see when errors occur and how characters and assets are handled.

- Existing transactions, row locks, idempotent receipts, and the outbox provide the foundation. Focus on actual boundaries and failure paths; do not rewrite persistence merely to make the plan look complete.

Limiting play to LAN reduces deployment scope, but does not automatically establish identity or data security. The current risks of long-lived token transmission, validation bypass when configuration is missing, and local credential storage still need to be handled according to severity. If they affect the demonstration environment, isolated test accounts and a controlled LAN can reduce exposure, with the acceptable scope confirmed by the user. The corresponding remediation must be completed before public multiplayer.

## MK-PROGRESSION 7 Long-Term Progression and Economy

Equipment and weapon progression are the main focus, with exploration and collection as a supplement. The existing stash, shop, supplies, and settlement provide a foundation. Full loss on death and the fallback revolver affect economic low points and must be tested together. Personal career records track total raids, extraction rate, kills, deaths, and achievements. Quests may be added later, and halo modification remains at the design-reservation stage rather than becoming mandatory implementation this period.

| Direction | Current Decision | Recommendation for This Period |
| --- | --- | --- |
| Stash and economy | Must be retained | Verify bringing items in, extracting items, losses, purchases, and logging back in to prevent asset duplication or loss. |
| Quests | Not a priority; may be added later | Do not make them a dependency of the October 24 critical path. |
| Special items | May be considered; shooting remains the focus | First confirm a small number of clear uses, then decide whether to include them in the November 8 scope. |
| Halo modification | Reserve the design for now; decide after the main loop stabilizes | Do not make implementation mandatory this period or use an undecided modification system to solve the current looting-motivation problem. |
| Personal career history | Personal records such as total raids, extraction rate, kills, deaths, and achievements | Define statistical events, denominators, and duplicate-settlement boundaries; keep this to personal records rather than competitive rankings. |
| Long-term progression such as a hideout | Needs to be broken down in conjunction with the overall progression goals | Priority and scope after the final project presentation still need confirmation; do not assume all of it will be completed this period. |

Economic acceptance prioritizes consistency: each persistent resource has explainable sources and uses, repeated submission of the same result does not grant duplicate rewards, death and extraction boundaries are clear, and there is a save-data migration strategy. Balance data should first come from repeatable play; do not treat prototype values as conclusions.

## MK-HEALTH 8 High-Priority Health and Survival

The health system is a major requirement emphasized by the user. In the static audit at a4ede5e, the runtime had only one overall health value; the seven-body-part interface was a placeholder, hunger and thirst were unimplemented, and oxygen appeared only in interface text. Existing medical treatment has server-authoritative validation and atomic consumption, which should be reused. The subsequent standalone pure-rules draft does not mean that these states have been integrated into the production runtime. The new target is seven body parts: head, chest, abdomen, left and right arms, and left and right legs, plus hunger, thirst, and outdoor oxygen. Merely replacing the UI is insufficient.

### 8.1 Audited Code Foundation and Migration Boundaries

| Existing or missing | Verified findings | Planning implications |
| --- | --- | --- |
| Overall health | PredictionComponents stores CurrentHealth/MaxHealth; death, weapons, AI, and the HUD read these values. | Migrate consumers consistently, or retain explicitly derived compatibility values; there must not be two sources of authority. |
| Damage paths | Projectiles and hitscan each directly subtract overall health; the head sphere/body capsule ultimately map only to an entity. | Add seven-region hit identifiers and server-side pose mapping; do not treat the existing head collider as body-part damage. |
| Medical treatment | Validates the connection, character, raid, request sequence number, and inventory version; healing and consumption complete atomically. | Extend treatment to target body parts while preserving ownership validation, replay protection, and inventory consistency. |
| Interface and survival | The seven body parts are placeholders awaiting telemetry; hunger and thirst have no implementation, and oxygen has no actual resource state. | Add state, HUD, sound, and interactions together, rather than only updating placeholder labels. |
| Persistence boundary | Raid settlement saves results, resources, and inventory, but not injuries; health resets when the character spawns. | Saving items does not mean saving injuries; persistent injuries between raids require an explicit decision and save-data migration. |

### 8.2 First-Prototype Health Rules

| System aspect | Confirmed direction | Rules needed before implementation |
| --- | --- | --- |
| Body parts and damage | Express injuries separately for seven body parts. | In the first prototype, head or chest health reaching zero triggers death; other body parts reaching zero cause functional consequences without directly declaring the entire character dead. |
| State consequences | Injuries should influence tension and decisions. | Arm damage affects weapon handling, leg damage restricts sprinting and movement, and abdominal damage increases survival pressure; effect magnitudes are configurable. |
| Hunger and thirst | Two survival resources are required. | Rules for consumption, replenishment, depletion consequences, interface warnings, and state across raids. |
| Outdoor oxygen | Consumed outdoors; oxygenated interiors change the danger state. | Provide graduated visual and audio warnings first; after depletion, enter hypoxia and gradually take damage. Instant death without warning is prohibited. |
| Medical treatment and items | Extend the existing medical and inventory foundations. | Eligible body parts, consumption, cancellation, repeated requests, interruption by death, and effect consistency. |
| Information feedback | Communicate through both the first-person HUD and immersive sound. | Priorities and warnings for injuries, hunger, thirst, and oxygen, plus breathing/equipment feedback, without obscuring primary combat information. |
| Synchronization and saving | Integrate with the existing Host and per-player inventory saving. | Server authority within each raid; for this phase, the recommendation is to retain health initialization on deployment. Persistent injuries across raids require separate confirmation; do not infer existing support. |

The user has delegated the rules for body parts reaching zero and oxygen depletion to the assistant. The following uses configurable first-prototype rules. Specific thresholds, damage, timings, and penalty magnitudes should first be implemented as test parameters, then tuned through the user's play sessions; they must not be presented as validated balance conclusions.

- Head or chest health reaching zero triggers death, with an explainable damage source, attack telegraphs, and hit feedback. An arm reaching zero slows weapon stability recovery and equipment changes; a leg reaching zero disables sprinting and reduces walking speed; the abdomen reaching zero accelerates hunger and thirst pressure. Consequence magnitudes are configurable, without abruptly taking away input or forcibly shaking the camera. Medical treatment can restore a targeted body part; the first prototype does not additionally introduce surgery or a downed-and-revive system.

- Oxygen feedback has four stages: normal, low, danger, and depleted/hypoxic. Oxygen is continuously consumed outdoors, with breathing and equipment alarms progressively intensifying at low levels. Depletion causes periodic chest damage that remains perceptible, rather than sudden death from a hidden timer. Entering an oxygenated interior immediately stops outdoor consumption and hypoxia damage; oxygen gradually replenishes at the configured rate. On leaving, consumption resumes from the remaining amount, without an instant reset across the boundary. Stage thresholds, damage, and recovery rates are all parameters to tune through actual testing.

- The first-prototype recommendation for hunger and thirst is consumption driven by server time, restoration through supplies, and, after depletion, stronger warnings followed by gradual health pressure, avoiding instant death without warning. Specific damage paths and consumption values are tunable design proposals, rather than values copied from an external game; the acceleration caused by abdominal damage should be clearly shown.

- Audiovisual presentation emphasizes realism, immersion, and danger while preserving readable enemy cues and fair aiming. Heavy blur, flashing, and continuous camera shake are not required effects; provide suitable intensity options and use text/icons to supplement color and sound.

### 8.3 Implementation Sequence

- Establish stable body-part IDs, configuration, and pure rules. Route projectiles, hitscan, and area damage through a unified server damage entry point, keeping death, drops, and raid settlement exactly-once. Migrate old overall-health consumers individually or use derived values.

- Implement authoritative seven-region hit mapping and validate server/headless poses, overlapping regions, and hits during movement. Extend the existing medical intent with a target body part, retaining checks for raid, request sequence number, character ownership, and inventory version.

- Advance hunger, thirst, and oxygen using server time, and determine indoors versus outdoors from authoritative scene regions rather than simply accepting client reports. Complete a minimal HUD and audio in parallel; repeatedly crossing boundaries must not reset resources or grant duplicate replenishment.

- Validate state authority, medical consumption, kill/death statistics, and extraction/death saving in single-player and a two-player Host session, then validate five-player load and abnormal exits.

- Tune values and presentation once rules and correctness are stable, and observe whether health pressure supports looting routes and extraction decisions rather than imposing continuous penalties with no meaningful choices.

### 8.4 Acceptance Gates

Acceptance covers body-part lower and upper bounds, death and healing, invalid inputs; overlapping hit regions, server poses, and movement; duplicate, stale, wrong-raid, or wrong-player medical requests; healing/damage ordering; survival consumption over fixed time; indoor/outdoor round trips; and persistence through reconnection, death, extraction, and resupply. Each treatment consumes supplies only once, without duplicate statistics or results.

This is a significant scope increase and must share development time with motivation, the map, and the alien. The plan brings it into the main workstream early. If measured effort exceeds the available window, the user should promptly decide the specific trade-offs; the health system must not be postponed unilaterally or reduced to a display-only UI.

## MK-PERFORMANCE 9 Performance and Quality Acceptance

The performance target is confirmed as Windows, a laptop RTX 4060, 1440p, DLSS allowed, and a target of 80 FPS, corresponding to an average frame time of approximately 12.5 ms. The CPU, laptop power and cooling conditions, quality preset, DLSS mode, whether frame generation is allowed, and measurement criteria still need confirmation. The actual prerequisites for DLSS integration in the existing Unity/URP setup and project must also be checked; permission to use it is not evidence that it is already supported.

- Lock down the target laptop and test settings early, recording power/performance mode, resolution, quality, DLSS mode, build, scene, and player and enemy counts. Results from a 4060 under different power or rendering conditions are not interchangeable.

- Establish a representative release-build baseline by October 22; complete major optimization regression testing on November 3. Track average FPS, low-FPS metrics, frame-time variation, memory, loading, and resource peaks.

- Major combined loads include lunar-surface and building rendering, multiplayer character physics, AI, the procedural alien, halo effects, audio, and UI. Individual systems looking good separately does not mean they pass when running together.

- Base optimization on profiling evidence, first addressing the most expensive areas or those with the greatest effect on the experience. Perceptible downgrades and architectural changes require confirmation.

Suggested presentation blockers include crashes, inability to enter or finish a raid, obvious camera clipping through the ground, persistent loading stalls, unexplained changes to player assets, severe multiplayer desynchronization, clearly uncontrolled characters or enemies, and the target machine clearly failing the approved performance gate. Classify defects by their impact rather than treating every small imperfection as a blocker.

## MK-MILESTONES 10 Phases from Now to the Final Project Presentation

October 10 to November 8, 2026 spans 29 days, or 30 calendar dates inclusive. The table below and the daily schedule are a proposed backward plan. The two presentation dates are confirmed milestones; all other dates are planning suggestions that need adjustment against the actual runtime baseline and available people. There is currently insufficient evidence to promise that every target can reach the same level of polish on time.

| Phase and dates | Key deliverables | Evidence required to enter the next phase |
| --- | --- | --- |
| M0: October 10–13 | Regress existing paths; document motivation and the two-base design; health migration plan and first-prototype rules. | Camera/check issues have evidence; overall-health and medical dependencies are clear; new rules are testable. |
| M1: October 14–23 | Equipment-progression motivation, two-base graybox, minimal seven-body-part and survival implementation, quality exemplars, and LAN. | Player goals and health risks are readable; both areas are playable; new states do not conflict with existing persistence. |
| Interim presentation: October 24 | Live operation and a Unity client technical presentation. | Record actual feedback on motivation, health, map, and networking, and recalibrate the second-half scope. |
| M2: October 25–November 1 | Polish four weapon categories and integrate rockets; adapt three characters; a complete ambushing/flanking alien; personal career record. | Content is individually playable; explosion/body-part/AI/network integration passes regression testing; statistics and player assets are consistent. |
| Freeze: November 2–7 | Stability, laptop performance at 1440p, recovery, live demonstration, and portfolio. | A fixed release candidate, actual performance evidence, a full rehearsal, and fallback plans. |
| Final project presentation: November 8 | Live presentation and portfolio delivery. | Use only a version verified in rehearsal; introduce no last-minute high-risk changes. |

### 10.1 M0 Gates That Cannot Be Bypassed

M0 protects the features the user has already successfully run, establishes the confirmed motivation, graybox, and health designs, and fills gaps in retest evidence. Record successful versions, configurations, and instance counts; regression-test the new approach addressing the camera issue and the check scripts. Include the scope of the existing overall-health migration, both damage entry points, and medical transactions in the implementation checklist. Unity not having been executed in the cloud does not mean the project cannot run.

The mismatches in the existing shop checks and cleanup script must be fixed, then rerun against an exact commit. Camera-fix testing must cover slopes, wall corners, tight spaces, rapid turns, and actual movement actions, checking clipping, jitter, and recovery rather than merely confirming that the diff looks reasonable.

### 10.2 Different Requirements for the Two Presentations

The October 24 focus is demonstrating that the existing loop has clear motivation and tactical base spaces: players know what to loot, why to take risks, and what they can do after extracting it. Both building complexes are playable; health, hunger, thirst, and oxygen create readable pressure; characters and halos have exemplar quality; and successful cooperative paths remain reliable. The November 8 focus is completing five weapon categories, the full alien, three-character adaptation, target performance, and on-site delivery. If gates are not met, present specific trade-offs to the user.

## MK-DAILY 11 Suggested Daily Deliverables

Each day lists the main focus and the basis for judging it; this does not imply that complex work can be completed in a single day. Feature changes require prior approval; undecided areas are limited to investigation, proposals, and reversible preparation. Independent art, UI, and testing can run in parallel, but user review and integration capacity are limited; tasks that fail their gates should be rescheduled based on evidence.

| Date | Suggested main deliverable | Acceptance or decision for the day |
| --- | --- | --- |
| October 10 | Archive single-player/two-player Host and per-player saving evidence; confirm motivation, two bases, and health scope. | Record completed work separately from new requirements; include five weapon categories and the complete alien in the target list. |
| October 11 | Regression-test the camera and check scripts; break down health migration, medical treatment, and motivation tasks. | Overall-health consumers, hitscan/projectile entry points, medical transactions, and seven-body-part interfaces are clear. |
| October 12 | Prepare pure health rules and the unified damage entry point; a replaceable graybox proposal for both bases. | First-prototype death/limb/oxygen consequences are configurable; advance the graybox along the approved direction. |
| October 13 | Minimal validation of motivation and health; begin the alien sandbox prototype. | Loot uses are clear, health state has a single authority, and interfaces for the alien's complete behavior are defined. |
| October 14 | Two-base layout; seven-region hit mapping and medical targets; character skeleton inventory. | Complete test plans for server poses, overlapping hits, and repeated medical requests. |
| October 15 | Lunar base and IK ambush/flanking experiments; health HUD framework. | Low-profile locomotion and attacks can connect; body-part states have actual data rather than placeholder labels. |
| October 16 | Integrate loot uses into the economy; hunger, thirst, and outdoor oxygen; main halo exemplar. | Players understand their purpose and the danger; first-prototype values remain tunable test parameters. |
| October 17 | Single-player regression of new motivation, seven body parts, full loss on death, and medical treatment. | The existing loop does not regress; guaranteed fallback claims and charging follow explicit rules. |
| October 18 | Two-player Host regression of new health, medical treatment, death, and per-player saving. | Medical supplies are consumed only once; state/statistics/player assets do not diverge; connection success is not a substitute for acceptance. |
| October 19 | Five-player scale and combined-performance testing; health failure paths. | Record actual player counts and conditions honestly; new states and exit outcomes are clear. |
| October 20 | Playable main routes through both bases; integrate indoor/outdoor air and sound. | Crossing regions does not reset resources; breathing/equipment/contact sounds match the danger state. |
| October 21 | Adapt hit regions, actions, and physics for three characters; identify polish gaps in the existing four weapons. | Different skeletons do not break body-part hits or gestures; record the status of each of the five weapon categories separately. |
| October 22 | Interim build, laptop performance baseline, and on-site rehearsal. | Motivation, bases, health, and cooperation can be demonstrated; record unmet targets without overstating support. |
| October 23 | Freeze the interim release candidate, record a backup video, and organize the technical explanation. | The fixed version and demonstration files can be restored; avoid adding new systems that evening. |
| October 24 | Interim presentation. | Collect specific observations and feedback, distinguishing defects, design changes, and new ideas. |
| October 25 | Recalibrate health and content based on interim feedback; prepare rocket and complete-alien integration. | Scope-growth risks are clear; the user decides specific trade-offs; do not independently omit required systems. |
| October 26 | Focused polish of assault-rifle and shotgun mechanics and presentation. | Improve quality according to the existing design rather than reinventing established mechanics. |
| October 27 | Sniper polish, rocket explosion pipeline, and gesture integration. | Large-area explosions have server validation; test occlusion/falloff/self-damage/friendly fire according to confirmed rules. |
| October 28 | Polish the alien's low-profile posture, rapid ambushes, flanking through complex terrain, and complete combat. | Gait and attacks are reliable; the alien can take damage and die; prepare to migrate it into the main map. |
| October 29 | Bring the alien into the base map and LAN; joint regression of explosions and seven body parts. | Multiple colliders do not cause the same character to take duplicate damage; enemies can fully participate in raids. |
| October 30 | Combined regression of three characters, five weapons, the alien, base audiovisuals, and multiplayer. | Correctness extends beyond isolated showcase scenes; significant clipping, performance, and synchronization issues have documented outcomes. |
| October 31 | Regression of the personal career record, medical consumption, raid settlement, and economy. | Raid count, extraction rate, kills, deaths, and achievements are not duplicated; modifications remain design-only. |
| November 1 | Full-content acceptance and pre-freeze decisions. | Accept both bases, health, three characters, five weapons, and the complete alien item by item; explicitly address unmet targets. |
| November 2 | Freeze new features and form the final-presentation release candidate. | Accept only blocker fixes, stability, and necessary performance work; new requests require confirmation. |
| November 3 | Optimize and retest on the laptop 4060 at 1440p with DLSS allowed. | Record the CPU, power, quality preset, DLSS mode and availability, and report actual results openly. |
| November 4 | Regression of LAN, disconnections, service restarts, and data recovery. | Player assets and results are consistent; failure messages are clear and recovery steps are executable. |
| November 5 | Clean-environment startup, distribution package, and save compatibility checks. | The user can follow instructions to launch single-player and LAN modes without implicit development-machine dependencies. |
| November 6 | Unity client portfolio explanation, live-demo script, and backup recordings. | Highlight programming, debugging, profiling, and integration; claims about the complete enemy and five weapons have real evidence. |
| November 7 | Full rehearsal; check equipment, version, backups, and on-site procedure. | Verified alternative demonstrations exist for on-site internet or multiplayer failures; retain a stable package. |
| November 8 | Final project presentation and delivery. | Run the rehearsed version; collect feedback and record later fixes instead of making last-minute major changes. |

## MK-TEAM 12 Team Responsibilities and Capacity Management

| Participant | Known situation | Suggested deliverables |
| --- | --- | --- |
| User | Invests substantial time and is responsible for gameplay and experience. | Direction decisions, quality-exemplar acceptance, key integration, playtesting on actual hardware, demonstration, and technical explanation. |
| Modeling collaborator | Learning while producing work; progress is relatively slow. | A small number of high-value buildings and landmarks; deliver engine-ready versions first, then progressively polish them. |
| 2D design collaborator | Responsible for UI, 2D weapon design, and illustration. | Core interface information hierarchy, halo recognition, and essential presentation visuals; deliver directly usable specifications. |
| Assistant and AI collaboration | Handles most task breakdown, implementation, and technical checks. | Incremental implementation, review, testing, documentation, and risk/progress reports; the user confirms gameplay and major architectural changes. |

These responsibilities are collaboration suggestions, not assignments already sent to teammates. The user needs to confirm each person's deliverables and handoff dates. For low-capacity dependencies, surfacing and accepting early drafts matters more than waiting for final art.

Reference-material status: a teammate has already drawn building concept art, but it has not been provided to the assistant in this round; UI sketches, gesture designs, and task lists have not yet been received either. Not receiving files does not mean these creations do not exist; use the materials actually supplied before implementation.

- The suggestion is to keep one main integration workstream and a small number of independent tasks with clear boundaries at a time. Do not merge character physics, networking, and alien locomotion together without validation.

- For each asset handoff, specify dimensions, coordinates, naming, materials, collision, animation or skeletal requirements, and how to replace the placeholder with the final version.

- Leave a recoverable state every day: branches, patches, test records, and next steps. Report unavailable equipment or services honestly; do not promise that AI or development environments run around the clock.

## MK-ACCEPTANCE 13 Presentation Acceptance and Portfolio Delivery

### 13.1 Suggested October 24 Acceptance Checklist

- A standalone Windows build launches; the user's verified complete single-player loop and item retention have not regressed; regression testing is complete for the new approach addressing the camera issue.

- The two-building-complex graybox forms playable base routes, and players can explain equipment-progression and exploration goals. Seven body parts, hunger, thirst, and outdoor oxygen have a minimal correct implementation with readable feedback, and the lunar-surface/character/halo exemplars have consistent visual quality.

- Single-player successful extraction and failed-raid settlement can both complete, followed by another deployment.

- Cooperative PvE adds coverage of the main paths to the user's existing successful tests, with clear coverage of actual instance counts, AI, inventory, extraction, and persistence, and disclosure of validation results for the five-player target.

- Current technical contributions and the performance baseline can be demonstrated; a fixed-version backup recording and a recoverable package are available.

### 13.2 November 8 Target Acceptance Checklist

The following uses the user's stated targets as its baseline. If any item needs to be delayed or downgraded, obtain an explicit decision before the freeze and update the checklist.

- The lunar base with two building complexes, three adapted characters, and all five weapon categories—revolver, assault rifle, shotgun, sniper, and explosive rocket—are playable; health, survival, and audiovisual feedback meet a consistent standard.

- Humanoid and non-humanoid enemies enter raids within the approved scope. The alien's ground, wall, and ceiling locomotion and transitions, rapid ambushing and flanking, and attacks have been validated in actual terrain, combat, and multiplayer.

- Key paths pass in single-player and LAN cooperation for up to five players; failure and recovery outcomes are explainable; persistence does not duplicate or lose player assets.

- Submit results against the 80 FPS target under the confirmed RTX 4060 test conditions, explaining averages, variation, and exceptions rather than showing only the best screenshot.

- The game can launch in a clean environment by following the instructions; provide the version, runtime requirements, known issues, startup steps, and demonstration controls.

- The user completes a full end-to-end rehearsal and prepares three reliable presentation methods: a playable single-player build, a LAN demonstration, and recordings.

### 13.3 Portfolio Materials

Suggested deliverables are a playable build, live-operation script, backup video, Unity client technical explanation, and supporting evidence. Show comprehensive technical integration with client programming ability as the central thread, avoiding superficial coverage of every domain. The exact presentation duration still needs to be specified.

- Prioritize a live hands-on demonstration, using recordings as backup and for the portfolio. The demonstration should show exploration and trade-offs across both building complexes, character physics, representative moments from the five weapon mechanics, a combat-capable alien, and LAN cooperation. Material must come from an actually runnable build.

- Prioritize the strongest Unity client examples in the technical explanation, such as character physics and adaptation across different skeletons, procedural legs and complete-enemy integration, lunar-surface and indoor/outdoor audiovisuals, weapon actions, and performance optimization. Use server authority and persistence to explain client boundaries and system integration. Each example should include the problem, trade-offs, code, validation, and limitations.

- Retain before/after performance comparisons, key regression cases, network or raid-settlement sequences, and code links that point to actual implementations.

- Accurately distinguish the user's design and integration, teammates' assets, and AI-assisted implementation. Do not describe free assets or AI-generated code as wholly original; demonstrating review, debugging, and technical judgment is valuable in itself.

### 13.4 On-Site Fallback Plan

The recommendation is to prepare a verified stable package, saves or demonstration configuration, recordings, and instructions by November 7. If on-site equipment or LAN is unavailable, switch to the single-player build and genuinely recorded footage, explaining the presentation method. Fallback plans support reliable presentation; they do not replace acceptance testing of the cooperative functionality itself.

## MK-TESTING 14 Testing and Merge Workflow

| Layer | Focus for this phase | Evidence |
| --- | --- | --- |
| Static review and check scripts | Fix known mismatches; review risky states and configuration. | Pinned commit, check results, and diff. |
| Unity and builds | Camera, input, characters, weapons, scenes, and UI. | Environment, commands, Play Mode, and build logs. |
| Backend and saves | Inventory, raid settlement, idempotency, transactions, and recovery. | Request results, database assertions, and restart records. |
| LAN | Two to five players, synchronization, exits, and disconnections. | Actual instance counts, conditions, versions, and logs. |
| Visuals and feel | Exemplars of the three distinguishing features and consistency across content. | Fixed-route recordings, issue list, and user acceptance. |
| Performance | Representative RTX 4060 build scenarios. | Complete test conditions, frame times, memory, and profiling. |
| Presentation | Clean startup, rehearsal, technical claims, and backups. | Build identifier, rehearsal checklist, videos, and sources. |

Confirmed rule: low-risk fixes may be merged autonomously after successful validation; gameplay changes and major architectural changes require user confirmation. PRs affecting runtime behavior that cannot yet be tested remain pending acceptance. Time pressure is not a basis for merging without checks that have actually passed.

For each task, record scope, dependencies, acceptance criteria, risks, and related decisions. Each PR should include the reason for the change, actual tests, unverified areas, impact on the experience, and rollback procedure. After merging, check the target branch and related builds before marking the task complete. The remote-write failure described in the initial document has been superseded by subsequently published PRs; past permission issues must not be treated as current blockers, and publication alone must not be described as acceptance.

## MK-RISKS 15 Risk and Trade-Off Triggers

The following signals should prompt timely decisions from the user. They do not authorize automatic scope cuts. Prioritize the core loop, exemplar quality for the three distinguishing features, and a reliable presentation, then adjust the remaining scope based on evidence.

| Trigger | Main impact | Choice to present |
| --- | --- | --- |
| Health-migration impact remains unclear on October 13. | Overall health, damage, medical treatment, and saves may develop conflicting state representations. | Prioritize unified authority and a migration checklist; validate the smallest complete set of rules first, rather than piling on UI to imply completion. |
| Exemplar quality is not established by October 16. | The other four weapons and three characters may replicate low quality. | The user chooses whether to continue polishing the exemplar or slow content expansion. |
| IK experiments are unstable or too costly. | The alien may undermine raids and performance. | Limit supported scenarios, locomotion complexity, or count; preserve the final target and define explicit phases. |
| Buildings still cannot be integrated by October 20. | Camera, navigation, and level regression testing lose time. | Accept a graybox, alternative layout, or delayed area, subject to user approval. |
| Core LAN paths are still unstable at the interim presentation. | The required release mode cannot be demonstrated as reliable. | Prioritize networking and recovery in the second half, adjusting supplementary progression or content polish. |
| Rocket or ammunition-type details remain undecided. | The new weapon and guaranteed-fallback economy are prone to repeated rework. | Continue polishing the four categories; explicitly mark explosion and energy proposals as candidate or approved. |
| Content or performance fails its gates on November 1. | New features consume the final week. | Decide item by item what to retain, downgrade, or delay, and update the demonstration checklist. |
| A blocker is found on November 7. | The risk of on-site failure rises. | Use the last verified stable version, explain known limitations, and avoid last-minute major merges. |

The main risk is bringing the expansion from overall health to seven body parts and survival states, a fifth large-explosion weapon category, the complete IK alien, adaptation across different skeletons, and two building complexes into the same short window. Other risks include building-production capacity, check scripts, player-asset consistency, token and configuration security, and laptop performance at 1440p. The plan preserves the targets, but new health and explosion systems expand the regression surface and must be reported honestly; untested code cannot offset schedule risk.

## MK-LONGTERM 16 Long-Term Roadmap after November 8

The final project presentation is a showcase milestone, not the end of the user's long-term personal work. Advance the following based on validation results, without setting new hard dates in advance or assuming changes to the LAN and low-budget constraints. Execute each round only after clear goals, scope, and acceptance criteria are approved.

| Phase | Suggested main goal | Basis for completion |
| --- | --- | --- |
| Post-presentation consolidation | Collect feedback, fix issues exposed on-site, and organize the repository and portfolio narrative. | Feedback is categorized, the stable version and technical explanation agree, and remaining defects are trackable. |
| Deepen the core experience | Improve map realism, combat feel, AI interaction, and character actions. | Before/after experience evidence on the same route, with user acceptance of the changes. |
| Build choices and progression | Define halo modifications, special items, personal career records, and other long-term progression. | Rules are confirmed first; choices are meaningful, and the economy and saves are explainable. |
| Content and tools | Improve reusable production workflows for buildings, characters, enemies, and levels. | New content can be created and tested to standards rather than relying on a growing pile of one-off scripts. |
| Cooperation and reliability | Continue improving LAN, failure recovery, save migration, and compatibility. | Multi-instance and failure paths pass repeatedly, and version updates are recoverable. |
| Long-term portfolio | Iterate the strongest technical examples and playable experience to fit the user's job-search direction. | Personal contributions are clear, technical claims have evidence, and materials match the actual version. |

After the final project presentation, deepen quests, special items, and halo modifications based on actual play. Modifications should first address choices and feedback, while personal career statistics remain consistent. Seasonal wipes, player trading, and competitive rankings remain excluded; do not reintroduce them from generic templates.

## MK-DECISIONS 17 Specific Decisions Still Needed

The basic direction is clear. Only details affecting scheduling or acceptance remain below. These do not prevent continued work on approved low-risk fixes and the validation foundation.

| Decision | Currently known | Still to clarify |
| --- | --- | --- |
| Authorized first-prototype health rules | Head/chest health reaching zero causes death; limbs have functional consequences; oxygen creates a gradual crisis. | Tune values and feedback intensity through play; refine hunger/thirst damage paths without claiming validated balance. |
| Energy and ammunition types | Energy sources provide charging; halo colors communicate ammunition types. | Separating energy from attribute modules is only a candidate; initial charge, consumption, and resupply methods remain to be defined. |
| Guaranteed fallback revolver economy | Unlimited claims are confirmed. | Non-tradable/zero resale value and charging limits are anti-arbitrage proposals that have not yet been approved. |
| Rocket explosion rules | Large-area explosive rockets are confirmed. | Specific rules for radius, occlusion, falloff, self-damage, friendly fire, and allocation across body parts. |
| Health boundary between raids | Only results and inventory are currently saved; health resets at character spawn. | Retain deployment initialization in the first prototype, as proposed; persistent injuries require separate approval and save migration. |
| Throwables | Currently absent; included as a planning candidate. | If implemented, start with one grenade type, as proposed; specific implementation and inclusion in this phase are unapproved. |
| Performance and on-site conditions | Laptop 4060 at 1440p, DLSS allowed, live demonstration. | CPU, power, quality settings, actual DLSS support/mode, frame generation, and presentation duration. |

## MK-MAINTENANCE 18 Daily Reports, Decisions, and Ongoing Maintenance

Report daily at 10:00 Beijing time, with additional reports when milestones are completed. Suggested report order: actual completion and evidence; work in progress and next steps; blockers and unblocking conditions; and decisions needed from the user. When there is no progress, explain the actual reason; automated daily reports must not imply round-the-clock development.

Issues maintain actionable tasks, PRs maintain specific diffs, test records substantiate results, and this plan maintains goals, scope, phase gates, and decisions. Record the date, reason, impact, and user confirmation of important changes; if scope is reduced, update acceptance criteria and presentation materials together.

## MK-TEMPLATE Appendix A: Task and Acceptance Templates

Task: name, user value, evidence, scope, exclusions, dependencies, risks, and related decisions. Acceptance: prerequisites, steps, expected results, environment, and evidence. Delivery: diff, documentation, tests, unverified areas, and rollback.

Use pass, conditional pass, fail, or blocked for milestone conclusions. Explain the impact and approval of every exception; do not substitute "mostly complete" for specific results.

## MK-SOURCES Appendix B: Version and Sources

v0.3 consolidates multiple rounds of user answers on October 10, 2026: a Unity client portfolio; actual tests of existing single-player/two-player Host and per-player saving; equipment progression with a small amount of exploration and collection; a two-building-complex graybox; first-person perspective; four existing weapon categories and a fifth explosive-rocket category; full loss on death and an unlimited guaranteed fallback revolver; a seven-body-part survival system with delegated authority to define consequences; a complete ambushing/flanking alien; a personal career record; and the laptop 4060 target at 1440p. The daily schedule and undecided ammunition-type and explosion details remain governed by the statuses in the text. This Markdown edition fully retains the plan's substantive scope, daily schedule, acceptance criteria, risks, and references, and adds the subsequently clarified requirements for four long, tapering black legs, a very small or barely visible torso, wall and ceiling locomotion, and the boundaries for license checks when reusing open source.

The original v0.3 source-audit baseline is a4ede5ee67d799808162cc39e5e2930ce2a75045. At 2026-10-10 06:19 UTC, main was rechecked as f749a56767bf411b9160c9477b0a4afe3ae36ba7. Changing progress is recorded separately in the dated snapshot. “Source exists” does not mean the current commit has passed runtime validation.

- [Moonkov code repository](https://github.com/MorrowHome/Moonkov)

- [Pinned baseline a4ede5e](https://github.com/MorrowHome/Moonkov/tree/a4ede5ee67d799808162cc39e5e2930ce2a75045)

- [Camera issue: Issue 1](https://github.com/MorrowHome/Moonkov/issues/1)

- [Old shop-check assertion](https://github.com/MorrowHome/Moonkov/blob/a4ede5ee67d799808162cc39e5e2930ce2a75045/Backend/ShopChecks/Program.cs#L59-L64)

- [Current shop item definitions](https://github.com/MorrowHome/Moonkov/blob/a4ede5ee67d799808162cc39e5e2930ce2a75045/Assets/Scripts/Networking/Shared/ShopModel.cs#L22-L28)

- [Backend cleanup script](https://github.com/MorrowHome/Moonkov/blob/a4ede5ee67d799808162cc39e5e2930ce2a75045/Backend/Smoke-Test.ps1#L71-L82)

- [Current database view definitions](https://github.com/MorrowHome/Moonkov/blob/a4ede5ee67d799808162cc39e5e2930ce2a75045/Backend/MoonPersistence/schema.sql#L66-L78)

- [Credentials supplied when the client joins a raid](https://github.com/MorrowHome/Moonkov/blob/a4ede5ee67d799808162cc39e5e2930ce2a75045/Assets/Scripts/Networking/Client/ClientGameSystem.cs#L60-L65)

- [Game network driver configuration](https://github.com/MorrowHome/Moonkov/blob/a4ede5ee67d799808162cc39e5e2930ce2a75045/Assets/Scripts/Networking/GameConnection/EntityDriverConstructor.cs#L65-L113)

- [Account credential validity period](https://github.com/MorrowHome/Moonkov/blob/a4ede5ee67d799808162cc39e5e2930ce2a75045/Backend/MoonPersistence/AccountRepository.cs#L83-L96)

- [Persistence fallback when configuration is missing](https://github.com/MorrowHome/Moonkov/blob/a4ede5ee67d799808162cc39e5e2930ce2a75045/Assets/Scripts/Networking/Server/RaidPersistenceContext.cs#L109-L124)

- [Server-side raid-join branch](https://github.com/MorrowHome/Moonkov/blob/a4ede5ee67d799808162cc39e5e2930ce2a75045/Assets/Scripts/Networking/Server/ServerGameSystem.cs#L376-L399)

- [Persistence acceptance checklist](https://github.com/MorrowHome/Moonkov/blob/a4ede5ee67d799808162cc39e5e2930ce2a75045/Docs/PostgreSQLPersistence.md#L105-L113)

- [Client-side local credential storage](https://github.com/MorrowHome/Moonkov/blob/a4ede5ee67d799808162cc39e5e2930ce2a75045/Assets/Scripts/Networking/Client/AccountClient.cs#L61-L104)

- [Scene-loading wait logic](https://github.com/MorrowHome/Moonkov/blob/a4ede5ee67d799808162cc39e5e2930ce2a75045/Assets/Scripts/Gameplay/GameManager/SceneLoader.cs#L63-L91)

- [Offline mode documentation](https://github.com/MorrowHome/Moonkov/blob/a4ede5ee67d799808162cc39e5e2930ce2a75045/Docs/SinglePlayer.md)

- [AI system documentation](https://github.com/MorrowHome/Moonkov/blob/a4ede5ee67d799808162cc39e5e2930ce2a75045/Docs/AI/DollSingerEnemies.md)

- [Inventory and container documentation](https://github.com/MorrowHome/Moonkov/blob/a4ede5ee67d799808162cc39e5e2930ce2a75045/Docs/ContainerInventory.md)

- [Existing overall health and network state](https://github.com/MorrowHome/Moonkov/blob/a4ede5ee67d799808162cc39e5e2930ce2a75045/Assets/Scripts/GhostBridge/Player/PredictionComponents.cs#L54-L74)

- [Existing medical-intent validation](https://github.com/MorrowHome/Moonkov/blob/a4ede5ee67d799808162cc39e5e2930ce2a75045/Assets/Scripts/Networking/Server/ServerGameSystem.Inventory.cs#L40-L67)

- [Seven-body-part interface placeholder state](https://github.com/MorrowHome/Moonkov/blob/a4ede5ee67d799808162cc39e5e2930ce2a75045/Assets/Scripts/UI/Game/StashScreen.Interaction.cs#L138-L146)

- [Raid-settlement and saving boundaries](https://github.com/MorrowHome/Moonkov/blob/a4ede5ee67d799808162cc39e5e2930ce2a75045/Assets/Scripts/Networking/Server/RaidPersistenceContext.cs#L172-L185)

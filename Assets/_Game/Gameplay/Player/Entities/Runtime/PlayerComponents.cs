using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Player.Entities
{
    // =================================================================================================================
    // The player ghost (M3a-1). Plan: Plans/netcode-for-entities-migration.md M3a; design: ghosts-design.md section 3.
    // Rule 2 of the design: prediction state is INTEGERS (millimetres, mm per second, centi-degrees, ticks). Positions and
    // rotations in LocalTransform are derived outputs, written from PlayerPose every tick.
    // =================================================================================================================

    /// <summary>Marks the player ghost (queries use it so other ghosts with a MoveState-like part never match).</summary>
    public struct PlayerGhostTag : IComponentData { }

    /// <summary><see cref="MoveState.Flags"/> bits.</summary>
    public static class MoveFlags
    {
        public const byte Moving = 1;
        /// <summary>The order was started at run speed (the Chi for it is paid by the server, never predicted).</summary>
        public const byte Run = 2;
    }

    /// <summary><see cref="ServerOrder.Flags"/> bits.</summary>
    public static class OrderFlags
    {
        /// <summary>The server already paid for running: the order moves at run speed.</summary>
        public const byte RunPaid = 1;
        /// <summary>The planned path was cut at <see cref="PlayerLimits.MaxCorners"/>; the goal is farther than the last corner.</summary>
        public const byte Truncated = 2;
        public const byte KeepFacing = 4;
    }

    public static class PlayerLimits
    {
        /// <summary>Most corners of a planned path (design: a click is usually under 12; the cap is 32).</summary>
        public const int MaxCorners = 32;
        /// <summary>Client orders count 1..0x7FFF; server orders have the high bit set, so the two spaces never meet.</summary>
        public const ushort ServerSeqBit = 0x8000;
        public const ushort ClientSeqMax = 0x7FFF;
        /// <summary>Most corners of a wall walk (its node feet are many; a longer route goes on as a server order from the cut).</summary>
        public const int MaxWallCorners = 64;
    }

    /// <summary>
    /// What the owner asks for this tick (command stream, <see cref="IInputComponentData"/>). A click carries the raw ground
    /// point in millimetres and the client's order number; the path itself is planned ONCE by the gather system, never in
    /// the predicted loop. Events are <see cref="InputEvent"/> so the server sees each exactly once.
    /// </summary>
    public struct PlayerInput : IInputComponentData
    {
        public int GoalXMm, GoalYMm, GoalZMm;
        public ushort ClickSeq;
        /// <summary>1 = the click asks to run.</summary>
        public byte Run;
        /// <summary>1 = the click asks to jump there (M3b); the walk never starts from it.</summary>
        public byte Jump;
        /// <summary>
        /// The tick the owner pressed the click on (<c>NetworkTick.SerializedData</c>, 0 = none): a jump the server receives late starts
        /// on it, the owner's own start, so the server's plan never restarts the owner's arc. A claim only: the server clamps it
        /// (<see cref="JumpFlight.ClickStartTick"/>).
        /// </summary>
        public uint ClickTick;
        /// <summary>
        /// How far into its tick the click was pressed, permille (0..999; owner pick P1, 1 Nov): the walk starts there, not at the tick's
        /// start, so the drawn body does not stand still for the rest of the tick and then jump a whole tick's run. A claim, clamped.
        /// </summary>
        public ushort PressPermille;
        /// <summary><see cref="WallClick"/> bits: the click is Wallstride's wall walk (M3c); then <see cref="Jump"/> is 0 (the jump systems leave it).</summary>
        public byte Wall;
        /// <summary>The clicked surface's normal in permille (a wall click: NGO finds the wall's node from the point and the normal).</summary>
        public short NormalX, NormalY, NormalZ;
        public InputEvent Click;
        public InputEvent Stop;
        /// <summary>The ghost id the owner attacks (M4.4c; 0 = none): NGO's RequestAttackEntityServerRpc target (PointAndClickPlayer:1881).</summary>
        public int AttackGhostId;
        /// <summary>The second click on a hostile: start the basic attack on <see cref="AttackGhostId"/> (NGO StartAttackTarget).</summary>
        public InputEvent Attack;
        /// <summary>The owner cancels the attack under way (NGO RequestCancelAttackServerRpc, PointAndClickPlayer:1872).</summary>
        public InputEvent StopAttack;
        /// <summary>The draw/sheathe key (NGO PointAndClickPlayer toggleWeaponAction -> RequestToggleWeaponDraw).</summary>
        public InputEvent ToggleWeapon;
    }

    /// <summary>
    /// A walk the server issues (skill walk-in, follow, housing, bench...). Replicated and predicted: the start system treats
    /// a change of <see cref="Seq"/> exactly like a click. It wins over a click of the same tick.
    /// </summary>
    public struct ServerOrder : IComponentData
    {
        [GhostField] public ushort Seq;
        [GhostField] public NetworkTick StartTick;
        [GhostField] public int GoalXMm, GoalYMm, GoalZMm;
        [GhostField] public byte Flags;
    }

    /// <summary>
    /// The prediction state of a walk. <see cref="ProgressMm"/> is the arc length travelled along the polyline
    /// (start point + corners), advanced by speed x tick length; <see cref="RemMicro"/> keeps the part of a millimetre a tick
    /// could not use, so the sum over ticks is exact. Nothing here is a float.
    /// </summary>
    public struct MoveState : IComponentData
    {
        [GhostField] public byte Flags;
        /// <summary>The order being walked (client numbers 1..0x7FFF, server numbers with the high bit). 0 = none yet.</summary>
        [GhostField] public ushort OrderSeq;
        /// <summary>The last <see cref="ServerOrder"/> number this walk logic has taken up (so a later click does not restart it).</summary>
        [GhostField] public ushort ServerSeq;
        /// <summary>A click number the server turned down; the start system will not start it again after a rollback.</summary>
        [GhostField] public ushort RefusedSeq;
        [GhostField] public int ProgressMm;
        [GhostField] public int RemMicro;
        [GhostField] public int SpeedMmps;
        /// <summary>Where the walk began; the first point of the polyline (the planner's first corner is ignored).</summary>
        [GhostField] public int StartXMm, StartYMm, StartZMm;
        /// <summary>
        /// 1 = on the water (Waterstride, M3c): running on a <see cref="WaterPlan"/> leg, or standing at its end. Set and cleared by the
        /// walk; a teleport and a jump's take-off clear it, a jump that stands on the water sets it. The server pays Flux while it is 1.
        /// </summary>
        [GhostField] public byte Water;
        /// <summary>
        /// 2 = perched in a tree (Wallstride part 3: a stop does nothing, every click is a drop, no Grip).
        /// 1 = on a wall (Wallstride's wall walk, M3c): on a segment of a <see cref="WallPlan"/> on the wall, or standing at its end. Set and
        /// cleared by the walk; a teleport clears it. The server takes Grip while it is 1; a stop does nothing.
        /// </summary>
        [GhostField] public byte Wall;
        /// <summary>The part of the walk's first tick before the press (microseconds): the first step moves only the rest. 0 after it.</summary>
        [GhostField] public int DelayMicros;
    }

    /// <summary>The integer truth of where the player stands and which way it faces; LocalTransform is written from it.</summary>
    public struct PlayerPose : IComponentData
    {
        [GhostField] public int XMm, YMm, ZMm;
        /// <summary>Hundredths of a degree, 0..35999, rotation about Y.</summary>
        [GhostField] public int YawCdeg;

        public int3 Mm => new int3(XMm, YMm, ZMm);
    }

    /// <summary>Bumped by the server on every placement; the presentation body snaps instead of easing when it changes.</summary>
    public struct TeleportSeq : IComponentData
    {
        [GhostField] public byte Value;
    }

    /// <summary>
    /// Which look the player wears (M3a-2b step 8): a number the server bumps whenever the look changes. The look itself (name and
    /// recipe as text) is fetched once per number by the client and cached, so the snapshot carries four bytes, not the recipe.
    /// 0 = no look yet (the body shows the prefab's own).
    /// </summary>
    public struct PlayerLook : IComponentData
    {
        [GhostField] public uint Version;
    }

    /// <summary>The player's level, for everyone (their nameplate; NGO's progression variable was read by everyone). Written by the server.</summary>
    public struct PlayerLevelTag : IComponentData
    {
        [GhostField] public short Level;
    }

    /// <summary>The player's Nova (Ascension, Standings, the one worn, the Weave) for everyone: the halo and the title (NGO's netState was read
    /// by everyone; M8 8.3.5). <c>NovaState.Packed</c>; written by the server.</summary>
    public struct PlayerNovaTag : IComponentData
    {
        [GhostField] public ulong Packed;
    }

    /// <summary>The player's Squad id for everyone, 0 = none (NGO's netPartyId was read by everyone: the minimap, namecards; M8 8.4.3). Written by the server.</summary>
    public struct PlayerPartyTag : IComponentData
    {
        [GhostField] public ulong Id;
    }

    /// <summary>The pet's growth percent and fed for everyone (NGO's PetNetState, read by everyone; M8 8.4.7). Written by the server.</summary>
    public struct PlayerPetTag : IComponentData
    {
        [GhostField] public byte Growth;
        [GhostField] public byte Fed;
    }

    /// <summary>
    /// How the player went down, for everyone (M4 6b; NGO's netPose plus DeathClientRpc / DisintegrateClientRpc / RevivedClientRpc):
    /// written by the server from its body's death motor, played once per change on every view. Server-written.
    /// </summary>
    public struct DeathState : IComponentData
    {
        /// <summary>Bumped on every death and every return.</summary>
        [GhostField] public byte Seq;
        /// <summary>0 alive, 1 on its back, 2 face down, 3 de-rezzed (NGO PlayerDeathMotor.Pose).</summary>
        [GhostField] public byte Pose;
        [GhostField] public byte Critical;
        /// <summary>The blow's flat direction, attacker to victim, in thousandths.</summary>
        [GhostField] public short DirXPermille;
        [GhostField] public short DirZPermille;
        /// <summary>The killer's ghost, 0 = none (a Hostile, a GM kill).</summary>
        [GhostField] public int KillerGhostId;
    }

    /// <summary>
    /// The server's PvP switch between factions (gm.pvp; NGO PlayerIdentity.netCrossFactionPvp on every player), on every player ghost
    /// (M4 8c): clients take it into FactionRules for namecards, targets and the Callout strip. Server-written.
    /// </summary>
    public struct PvpSwitch : IComponentData
    {
        [GhostField] public byte CrossFactionPvp;
    }

    /// <summary>A client applies the switch only when its ghost value changes (first sight included), never every frame: on a host the
    /// server and the client share the switch, and an older snapshot must not set back a GM's change.</summary>
    public static class PvpSwitchWatch
    {
        public const int Unseen = -1;

        public static bool Changed(ref int seen, byte now)
        {
            if (seen == now) return false;
            seen = now;
            return true;
        }
    }

    /// <summary>A player's Callout opponent for everyone (M4 8b; NGO PlayerDuel.netOpponent): the opponent's ghost, 0 = none. Server-written.</summary>
    public struct DuelState : IComponentData
    {
        [GhostField] public int OpponentGhostId;
    }

    /// <summary>What a view does with a player's <see cref="DeathState"/> (M4 6b): a pure rule, so it is tested without a world.</summary>
    public enum DeathShow : byte { Nothing, LieDown, Fall, GetUp }

    public static class DeathStates
    {
        /// <summary>
        /// The first look at a player (<paramref name="met"/> false) shows one already down lying there (NGO's late joiner), never the fall;
        /// afterwards each new <see cref="DeathState.Seq"/> is a fall or a return.
        /// </summary>
        public static DeathShow What(bool met, byte seenSeq, in DeathState now)
        {
            if (!met) return now.Pose != 0 ? DeathShow.LieDown : DeathShow.Nothing;
            if (now.Seq == seenSeq) return DeathShow.Nothing;
            return now.Pose != 0 ? DeathShow.Fall : DeathShow.GetUp;
        }
    }

    /// <summary>A player's HP and Max HP in thousandths, for everyone (NGO's PlayerResources netHealth / netMaxHealth; M4.2a). Written by the server.</summary>
    public struct PlayerVitals : IComponentData
    {
        [GhostField] public int HealthMilli;
        [GhostField] public int MaxHealthMilli;
    }

    /// <summary>
    /// The melee strike timing (M4.4b): NGO PlayerMeleeCombat's numbers with the lengths of the same three combo clips, baked on the
    /// player ghost (a strike lasts clip length / strike speed, NGO GetStrikeDuration). Read by the server; never sent.
    /// </summary>
    public struct MeleeTimingData : IComponentData
    {
        public MeleeTiming Value;
    }

    /// <summary>
    /// The basic attack under way (M4.4c; ghosts-design.md 4.2, the cast fields come with the skills): predicted, so the owner shows the
    /// strike on its press and the server confirms it. NGO keeps this in PlayerMeleeCombat (currentTarget, serverSwingStep, the
    /// strike's start); here it is ticks.
    /// </summary>
    public struct CombatState : IComponentData
    {
        /// <summary>The combo step under way: 0 none, 1..3.</summary>
        [GhostField] public byte Step;
        [GhostField] public NetworkTick StepStartTick;
        /// <summary>The first tick the next step may start (NGO minCadenceTolerance x the strike's length, PlayerMeleeCombat:462-468).</summary>
        [GhostField] public NetworkTick NextStepEarliest;
        /// <summary>The ghost id being attacked, 0 none (NGO currentTarget).</summary>
        [GhostField] public int TargetGhostId;
        /// <summary>Server only: the strike of this step has landed (NGO serverSwingHitTargets: one hit per target per swing).</summary>
        public byte SwingHit;
        /// <summary>The weapon is in the hand (NGO PlayerAnimancerMotor.isWeaponDrawn): drawn by an attack, an attack skill or the key.</summary>
        [GhostField] public byte WeaponDrawn;
        /// <summary>When the draw began: strike 1 waits for the draw (NGO's combo loop waits while Equipping).</summary>
        [GhostField] public NetworkTick DrawnTick;
    }

    /// <summary>The selected mount's box (0 = none), ride phase, growth percent and fed, for everyone (NGO's MountNetState; M8 8.4.8). Written by the server.</summary>
    public struct PlayerMountTag : IComponentData
    {
        [GhostField] public byte Selected;
        [GhostField] public byte Phase;
        [GhostField] public byte Growth;
        [GhostField] public byte Fed;
    }

    /// <summary>The emote the player shows, for everyone: an empty key = none, the serial goes up on every change (NGO's netState; M8 8.4.6). Written by the server.</summary>
    public struct PlayerEmoteTag : IComponentData
    {
        [GhostField] public FixedString64Bytes Key;
        [GhostField] public byte Serial;
        [GhostField] public byte Stance;
    }

    /// <summary>The player's Crew tag for everyone, an empty name = none (NGO's netTag was read by everyone: the namecards; M8 8.4.5). Written by the server.</summary>
    public struct PlayerCrewTag : IComponentData
    {
        [GhostField] public FixedString32Bytes Crew;
        [GhostField] public FixedString32Bytes Title;
        [GhostField] public byte Rank;
        [GhostField] public byte Symbol;
        [GhostField] public byte Colour;
        [GhostField] public byte TagColour;
        [GhostField] public byte TagBackground;
    }

    /// <summary>
    /// The player is a Game Master (M7.4): everyone sees the [GM] tag on their name, as NGO showed it from <c>PlayerGm</c>'s network variable.
    /// Written by the server from the GM roster; 1 = GM.
    /// </summary>
    public struct PlayerGmTag : IComponentData
    {
        [GhostField] public byte On;
    }

    /// <summary>Walk and run speed in mm/s, written by the server (rank, mount, gear), never recomputed by the client in the loop.</summary>
    public struct MoveStats : IComponentData
    {
        [GhostField] public int WalkMmps;
        [GhostField] public int RunMmps;
    }

    /// <summary>
    /// The learned Run and Jump ranks (0 = not learned), written by the server from the body's skills. Owner only: the owner's client
    /// plans and checks its own jumps with them, as the NGO client did with its mirrored ranks; the server checks again.
    /// </summary>
    [GhostComponent(OwnerSendType = SendToOwnerType.SendToOwner)]
    public struct MoveRanks : IComponentData
    {
        [GhostField] public byte Run;
        [GhostField] public byte Jump;
        /// <summary>The character's level (Waterstride opens at 60, M3c: the owner checks it as the server does).</summary>
        [GhostField] public ushort Level;
    }

    /// <summary>
    /// The player's Flux and most Flux in thousandths, written by the server from the body's resources. Owner only: the owner's
    /// client checks the cost of its own jumps and runs with it (as the NGO client did) and its HUD shows it; the server pays.
    /// </summary>
    [GhostComponent(OwnerSendType = SendToOwnerType.SendToOwner)]
    public struct OwnerFlux : IComponentData
    {
        [GhostField] public int Milli;
        [GhostField] public int MaxMilli;
        /// <summary>The HP and Max HP in thousandths (owner, 2 Nov, item 5: a heal the server made shows on the owner's HUD).</summary>
        [GhostField] public int HealthMilli;
        [GhostField] public int MaxHealthMilli;
    }

    /// <summary>
    /// What the owner's windows show (M7/M8 system 0, owner Q1, 2 Nov 2026: "just like the current game"): the Creds of the Pack window and the
    /// level, XP, points, attributes and Renown of the Character window, as NGO's owner-only wallet variable and progression variable carry
    /// them. Written by the server from the body; owner only.
    /// </summary>
    [GhostComponent(OwnerSendType = SendToOwnerType.SendToOwner)]
    public struct OwnerProgress : IComponentData
    {
        [GhostField] public long Creds;
        [GhostField] public int Level, Experience, StatPoints, SkillPoints, Renown;
        [GhostField] public int Strength, Vitality, Agility, Spirit;
    }

    /// <summary>
    /// The Serums the player has taken, kind by kind (M8 8.3.4): NGO's <c>PlayerSerums.netState</c>, for the owner's stat sheet. Written by
    /// the server from the body; owner only (others see only the stats, which they do not compute).
    /// </summary>
    [GhostComponent(OwnerSendType = SendToOwnerType.SendToOwner)]
    public struct OwnerSerums : IComponentData
    {
        [GhostField] public ushort K0, K1, K2, K3, K4, K5, K6, K7;
    }

    /// <summary>One corner of the server's planned path (millimetres). Owner only: observers never see the path.</summary>
    [GhostComponent(OwnerSendType = SendToOwnerType.SendToOwner)]
    public struct PathCorner : IBufferElementData
    {
        [GhostField] public int XMm, YMm, ZMm;
    }

    /// <summary>Says which order the <see cref="PathCorner"/> buffer belongs to and how many corners it holds.</summary>
    [GhostComponent(OwnerSendType = SendToOwnerType.SendToOwner)]
    public struct PathPlan : IComponentData
    {
        [GhostField] public ushort Seq;
        [GhostField] public byte Count;
    }

    /// <summary>
    /// The path the owner planned itself for its latest click. Client only in practice (the server never fills it), NOT a
    /// ghost field, so a rollback to a snapshot leaves it alone (proved by spike S3).
    /// </summary>
    public struct ClientPlanCorner : IBufferElementData
    {
        public int XMm, YMm, ZMm;
    }

    /// <summary>Which click <see cref="ClientPlanCorner"/> holds; <see cref="Seq"/> is also the last number issued. Not replicated.</summary>
    public struct ClientPlanHeader : IComponentData
    {
        public ushort Seq;
        public byte Count;
        /// <summary>Times a plan was written (diagnostics and tests: one per click, rollback must not add any).</summary>
        public int Writes;
    }

    /// <summary>Server only: ask for a walk. The server plan system plans it, fills the corner buffer and writes <see cref="ServerOrder"/>.</summary>
    public struct ServerOrderRequest : IComponentData, IEnableableComponent
    {
        public int GoalXMm, GoalYMm, GoalZMm;
        public byte Flags;
    }

    /// <summary>Server only: place the player. The pose is set, the walk is dropped and <see cref="TeleportSeq"/> is bumped.</summary>
    public struct TeleportRequest : IComponentData, IEnableableComponent
    {
        public int XMm, YMm, ZMm;
        public int YawCdeg;
    }

    /// <summary>Counters for tests and the divergence log (not replicated).</summary>
    public struct PlayerMoveDiag : IComponentData
    {
        /// <summary>Every run of the predicted move for this entity, re-predictions included.</summary>
        public int Runs;
        /// <summary>Runs flagged as the first full prediction of a tick.</summary>
        public int FirstRuns;
        public int MaxRunsOneTick;
        public uint LastTick;
        public int RunsThisTick;
        public int OrderStarts;
    }

    /// <summary>
    /// One line of the optional per-tick history (ring, slot = tick % length). Present only on ghosts whose authoring asks
    /// for <see cref="PlayerGhostAuthoring.TraceTicks"/>; tests compare the server's ring with the owner's.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct PlayerMoveTrace : IBufferElementData
    {
        public uint Tick;
        public int ProgressMm, SpeedMmps, XMm, YMm, ZMm, YawCdeg;
        public byte Flags;
        public ushort OrderSeq;
        public int RunsForTick;
    }

    /// <summary>
    /// The numbers of the walk, in integers (mm, mm/s, permille). Taken from the first singleton of this type when the world
    /// has one (baked from the movement settings by the later wiring), else <see cref="Default"/>; the defaults are the
    /// current <c>MovementSettings</c> values.
    /// </summary>
    public struct PlayerMoveSettings : IComponentData
    {
        /// <summary>Acceleration floor in mm/s^2 (settings.acceleration 25 m/s^2).</summary>
        public int AccelMmps2;
        /// <summary>Acceleration is at least speed x this (settings.accelerationPerSpeed).</summary>
        public int AccelPerSpeed;
        public int ArrivalSlowdownMs;
        public int ArrivalMinRadiusMm;
        public int ArrivalMaxRadiusMm;
        /// <summary>Share of full speed kept at the destination, in permille (arrivalMinSpeedFactor 0.55).</summary>
        public int ArrivalMinPermille;
        /// <summary>The walk ends this far before the end of the path (settings.stoppingDistance 0.2 m); 0 = exactly at the end.</summary>
        public int StopMm;
        /// <summary>Farthest a click may be from the player; the server refuses a farther one. 0 = no limit.</summary>
        public int MaxClickRangeMm;
        /// <summary>
        /// How fast the body turns toward its way, hundredths of a degree a second (settings.turnSpeed 540 deg/s = 54000). 0 = it faces
        /// the way at once (the tests' default). Integer per tick on server and owner alike.
        /// </summary>
        public int TurnCdegPerSecond;

        public static PlayerMoveSettings Default => new PlayerMoveSettings
        {
            AccelMmps2 = 25000,
            AccelPerSpeed = 8,
            ArrivalSlowdownMs = 300,
            ArrivalMinRadiusMm = 1500,
            ArrivalMaxRadiusMm = 8000,
            ArrivalMinPermille = 550,
            StopMm = 0,
            MaxClickRangeMm = 0,
            TurnCdegPerSecond = 0,
        };
    }

    /// <summary>Singleton in a subscene: the player ghost prefab the server spawns for a joining player.</summary>
    public struct PlayerGhostPrefab : IComponentData
    {
        public Entity Prefab;
    }
}

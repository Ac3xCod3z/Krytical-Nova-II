using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Player.Entities
{
    // =================================================================================================================
    // Jump as a tick plan (M3b). Design: Plans/netcode-for-entities-designs/m3b-design.md; acceptance: plan section 53.
    // The ghost owns the root (owner answer Q3 a): the pose at any tick is a function of the plan and the tick, the same
    // shape the NGO jump's warp gave (PlayerJumpMotor.EvaluateWarpedFeet): even travel from take-off to the apex and on to
    // the landing, the height following the jump clip's own rise and fall. Integers only, like the walk.
    // =================================================================================================================

    /// <summary><see cref="MotionPlan.Kind"/> values.</summary>
    public static class MotionKind
    {
        public const byte None = 0;
        public const byte Jump = 1;
    }

    /// <summary><see cref="MotionPlan.Flags"/> bits.</summary>
    public static class MotionFlags
    {
        /// <summary>The server took the jump's Flux at take-off (never predicted).</summary>
        public const byte Paid = 1;
    }

    /// <summary>
    /// A jump (later: death knock and fall) planned by the server, replicated to everyone so observers animate from it.
    /// <see cref="StartTick"/> is the tick of the press; the clip's take-off, apex and touchdown times count from there.
    /// While <see cref="Kind"/> is not <see cref="MotionKind.None"/> the walk is skipped for this ghost.
    /// </summary>
    public struct MotionPlan : IComponentData
    {
        [GhostField] public byte Kind;
        [GhostField] public byte Flags;
        /// <summary>The click number that asked for it (the walk's <see cref="PlayerInput.ClickSeq"/> space).</summary>
        [GhostField] public ushort PlanSeq;
        [GhostField] public NetworkTick StartTick;
        [GhostField] public int FromXMm, FromYMm, FromZMm;
        [GhostField] public int ToXMm, ToYMm, ToZMm;
        /// <summary>Height of the feet at the apex.</summary>
        [GhostField] public int ApexYMm;
        /// <summary>The way it faces through the jump and after the landing.</summary>
        [GhostField] public int YawCdeg;

        public int3 FromMm => new int3(FromXMm, FromYMm, FromZMm);
        public int3 ToMm => new int3(ToXMm, ToYMm, ToZMm);
    }

    /// <summary>
    /// The owner's own plan for its latest jump click (owner answer Q1 a: the jump is predicted on the press). Client only and
    /// NOT a ghost field, so a rollback leaves it alone; the predicted loop uses it until the server's <see cref="MotionPlan"/>
    /// with the same <see cref="MotionPlan.PlanSeq"/> arrives, or drops it when the server refused that click.
    /// </summary>
    public struct ClientMotionPlan : IComponentData
    {
        public MotionPlan Plan;
    }

    /// <summary>
    /// A vault over an obstacle as one leg of a walk's path (M3c, design `m3c-vault-design.md`): the walk's corners run through the
    /// take-off and the landing, and while the walk's progress is on that leg the body moves at the leg's own speed and rises over the
    /// obstacle on NGO's curve (<see cref="VaultLeg"/>). Planned by the server with the walk (all players see it); integers only.
    /// </summary>
    public struct VaultPlan : IComponentData
    {
        /// <summary>The walk's order number this vault belongs to (0 = none).</summary>
        [GhostField] public ushort Seq;
        /// <summary>The walk's progress at the take-off and the leg's length, millimetres along the path.</summary>
        [GhostField] public int LegStartMm, LegLengthMm;
        /// <summary>The vault's air time, microseconds (NGO's <c>VaultTiming</c>).</summary>
        [GhostField] public int AirMicros;
        /// <summary>How high the body rises over the leg's straight line, millimetres.</summary>
        [GhostField] public int RaiseMm;
        /// <summary>Where the rise ends and the fall starts, permille of the leg.</summary>
        [GhostField] public short RiseEndsPermille, FallStartsPermille;
        /// <summary>The body's vault style for this obstacle (presentation: which clip).</summary>
        [GhostField] public byte Style;
    }

    /// <summary>The owner's own vault for its own click's path (not a ghost field: a rollback leaves it alone).</summary>
    public struct ClientVaultPlan : IComponentData
    {
        public VaultPlan Plan;
    }

    /// <summary>The vault leg in integers (pure, Burst-safe; the server and the owner call it with the same plan and progress).</summary>
    public static class VaultLeg
    {
        /// <summary>The walk of <paramref name="orderSeq"/> is on <paramref name="v"/>'s leg at <paramref name="progressMm"/>.</summary>
        public static bool On(in VaultPlan v, ushort orderSeq, int progressMm) =>
            v.Seq != 0 && v.Seq == orderSeq && v.LegLengthMm > 0 && v.AirMicros > 0
            && progressMm >= v.LegStartMm && progressMm < v.LegStartMm + v.LegLengthMm;

        /// <summary>The leg's own speed: its length over the air time (NGO's warp: even travel along the ground).</summary>
        public static int SpeedMmps(in VaultPlan v) =>
            v.AirMicros > 0 ? (int)math.min((long)v.LegLengthMm * 1000000L / v.AirMicros, int.MaxValue) : 0;

        /// <summary>
        /// The rise over the leg's straight line at <paramref name="progressMm"/>: NGO's four keys with flat tangents (a smoothstep up to
        /// <see cref="VaultPlan.RaiseMm"/> by the rise's end, level, a smoothstep down from the fall's start). 0 off the leg.
        /// </summary>
        public static int HeightMm(in VaultPlan v, int progressMm)
        {
            if (v.LegLengthMm <= 0) return 0;
            long s = (long)(progressMm - v.LegStartMm) * 1000 / v.LegLengthMm;
            if (s <= 0 || s >= 1000) return 0;
            int rise = math.clamp((int)v.RiseEndsPermille, 1, 999), fall = math.clamp((int)v.FallStartsPermille, rise, 999);
            if (s < rise) return (int)((long)v.RaiseMm * Smooth((int)(s * 1000 / rise)) / 1000);
            if (s <= fall) return v.RaiseMm;
            return (int)((long)v.RaiseMm * Smooth((int)((1000 - s) * 1000 / (1000 - fall))) / 1000);
        }

        /// <summary>Smoothstep in permille: 3t^2 - 2t^3 (a Hermite with flat tangents, as Unity's curve between flat keys).</summary>
        public static int Smooth(int tPermille)
        {
            long t = math.clamp(tPermille, 0, 1000);
            return (int)(t * t * (3000 - 2 * t) / 1000000);
        }
    }

    /// <summary><see cref="WaterPlan.Flags"/> bits.</summary>
    public static class WaterFlags
    {
        /// <summary>The walk ends on the water: the player stands there (NGO's "Standing").</summary>
        public const byte EndsOnWater = 1;
        /// <summary>Out of Flux, carried back to the shore by the server (NGO's "Stranded"): clicks are refused on the way.</summary>
        public const byte Stranded = 2;
    }

    /// <summary>
    /// Waterstride as one leg of a walk's path (M3c, design `m3c-waterstride-design.md`): the walk's corners run to the shore, over the
    /// water by the zone's water map, and ashore. While the walk's progress is on the leg the body runs at the leg's own speed (no
    /// acceleration, no slowing at the end) and turns at its own rate. Planned by the server with the walk (all players see it: the wake);
    /// integers only. Whether the body is on the water now is <see cref="MoveState.Water"/>.
    /// </summary>
    public struct WaterPlan : IComponentData
    {
        /// <summary>The walk's order number this leg belongs to (0 = none).</summary>
        [GhostField] public ushort Seq;
        /// <summary>The walk's progress where the run onto the water starts (0 = from the water), millimetres along the path.</summary>
        [GhostField] public int LegStartMm;
        /// <summary>The walk's progress where it steps ashore; ignored with <see cref="WaterFlags.EndsOnWater"/>.</summary>
        [GhostField] public int LegEndMm;
        /// <summary>The speed on the water, mm/s (NGO: the ground speed x the settings' share, at least 1 m/s; stranded, its own).</summary>
        [GhostField] public int SpeedMmps;
        /// <summary>How fast the body turns on the water, hundredths of a degree a second (NGO 720 deg/s).</summary>
        [GhostField] public int TurnCdegPerSecond;
        [GhostField] public byte Flags;
    }

    /// <summary>The owner's own water leg for its own click's path (not a ghost field: a rollback leaves it alone).</summary>
    public struct ClientWaterPlan : IComponentData
    {
        public WaterPlan Plan;
    }

    /// <summary>Server only: the Flux owed for the time on the water, in millionths of a point (paid a whole point at a time, as NGO).</summary>
    public struct WaterFluxOwed : IComponentData
    {
        public long Micro;
    }

    /// <summary>The water leg in integers (pure, Burst-safe; the server and the owner call it with the same plan and progress).</summary>
    public static class WaterLeg
    {
        /// <summary>The walk of <paramref name="orderSeq"/> is on <paramref name="w"/>'s leg at <paramref name="progressMm"/>.</summary>
        public static bool On(in WaterPlan w, ushort orderSeq, int progressMm) =>
            w.Seq != 0 && w.Seq == orderSeq && w.SpeedMmps > 0 && progressMm >= w.LegStartMm
            && ((w.Flags & WaterFlags.EndsOnWater) != 0 || progressMm < w.LegEndMm);

        /// <summary>The leg is stranded (out of Flux, on the way back to the shore).</summary>
        public static bool Stranded(in WaterPlan w, ushort orderSeq) => w.Seq != 0 && w.Seq == orderSeq && (w.Flags & WaterFlags.Stranded) != 0;

        /// <summary>The leg of the walk under way: the server's for its own order, else the owner's.</summary>
        public static WaterPlan Of(in WaterPlan server, in WaterPlan client, ushort orderSeq) =>
            server.Seq != 0 && server.Seq == orderSeq ? server : client;

        /// <summary>The Flux owed for <paramref name="micros"/> on the water at <paramref name="fluxPerSecondMilli"/> thousandths a second, in millionths.</summary>
        public static long OwedMicro(int fluxPerSecondMilli, int micros) => (long)fluxPerSecondMilli * micros / 1000;
    }

    /// <summary>
    /// Plans a jump where the world is known (NavMesh, water, PhysX sweep, the rank's limits from <c>JumpMotionData</c>):
    /// filled by the layer that knows the body, on the server and (for the owner's prediction) on the client.
    /// </summary>
    public interface IJumpPlanner
    {
        /// <summary>The landing feet and the apex height for a jump of <paramref name="ghost"/> from its feet to the clicked point; else why not.</summary>
        bool TryPlan(Entity ghost, int3 fromFeetMm, int3 clickedMm, out int3 landingFeetMm, out int apexYMm, out string reason);
    }

    /// <summary>Server: the jump's Flux (5 at every rank). Checked on the click, taken at take-off (owner answer Q2 a).</summary>
    public interface IJumpCost
    {
        bool CanPay(Entity ghost, out string reason);
        void Pay(Entity ghost);
    }

    /// <summary>
    /// The jump clip's timing and height shape, baked from <c>Warp_Player_DoubleJump.asset</c> (its two warp phases and its
    /// root Y curve) by the Editor baker, so the server and the owner use the same integers. Times count from the press.
    /// <see cref="Rise"/> is the share (permille) of the climb from take-off to apex made by each of <see cref="Samples"/>
    /// equal steps of that phase; <see cref="Fall"/> the share of the drop from apex to touchdown. Both start at 0 and end at 1000.
    /// </summary>
    public struct PlayerMotionCurves : IComponentData
    {
        public const int Samples = 32;

        public int TakeoffMicros, ApexMicros, LandMicros;
        /// <summary>Landing cooldown, microseconds (JumpMotion.asset landingCooldown 0.25 s).</summary>
        public int CooldownMicros;
        /// <summary>
        /// How far back the server starts a jump on the click's own tick when the click reaches it late, microseconds: NGO's own
        /// take-off hold (the owner waited at most this long for its server, <c>PlayerJumpMotor.OwnTakeoffHoldMax</c>, 0.25 s).
        /// </summary>
        public int ClickWindowMicros;
        public FixedList128Bytes<short> Rise;
        public FixedList128Bytes<short> Fall;
        /// <summary>
        /// The clip's root height before take-off, millimetres from its start, at <see cref="Samples"/> equal steps from the press to the
        /// take-off: the crouch. Drawn only (the NGO warp lowered the body by it, S7 31 Oct: 20.5 cm); the ghost keeps the feet on the
        /// start. Empty = no crouch dip.
        /// </summary>
        public FixedList128Bytes<short> Crouch;

        /// <summary>
        /// The clip's numbers (take-off 0.183 s, apex 0.767 s, touchdown 1.407 s, cooldown 0.25 s, click window 0.25 s) with
        /// straight-line shares. Only for worlds that have no baked singleton (tests); the game bakes the real curve.
        /// </summary>
        public static PlayerMotionCurves Default
        {
            get
            {
                var curves = new PlayerMotionCurves
                {
                    TakeoffMicros = 183333, ApexMicros = 766667, LandMicros = 1406667, CooldownMicros = 250000, ClickWindowMicros = 250000,
                };
                for (int i = 0; i <= Samples; i++)
                {
                    short share = (short)(i * 1000 / Samples);
                    curves.Rise.Add(share);
                    curves.Fall.Add(share);
                }
                return curves;
            }
        }

        /// <summary>
        /// Bakes the tables from the clip: <paramref name="rootY"/> is the clip's root height at a clip time (seconds). Each share
        /// is NGO's: (height(t) - height(phase start)) / (height(phase end) - height(phase start)), clamped to 0..1, or 1 when the
        /// phase hardly changes height (under 0.01).
        /// </summary>
        public static PlayerMotionCurves FromClip(float takeoffSeconds, float apexSeconds, float landSeconds, float cooldownSeconds, Func<float, float> rootY,
            float clickWindowSeconds = 0.25f)
        {
            if (rootY == null) throw new ArgumentNullException(nameof(rootY));
            if (!(takeoffSeconds >= 0f && apexSeconds > takeoffSeconds && landSeconds > apexSeconds))
                throw new ArgumentException($"the clip's phases are not in order: take-off {takeoffSeconds}, apex {apexSeconds}, landing {landSeconds}");
            var curves = new PlayerMotionCurves
            {
                TakeoffMicros = (int)math.round(takeoffSeconds * 1e6f),
                ApexMicros = (int)math.round(apexSeconds * 1e6f),
                LandMicros = (int)math.round(landSeconds * 1e6f),
                CooldownMicros = (int)math.round(math.max(0f, cooldownSeconds) * 1e6f),
                ClickWindowMicros = (int)math.round(math.max(0f, clickWindowSeconds) * 1e6f),
            };
            Fill(ref curves.Rise, takeoffSeconds, apexSeconds, rootY);
            Fill(ref curves.Fall, apexSeconds, landSeconds, rootY);
            float rest = rootY(0f);
            for (int i = 0; i <= Samples; i++)
            {
                float mm = (rootY(takeoffSeconds * i / Samples) - rest) * 1000f;
                curves.Crouch.Add((short)math.clamp(math.round(mm), short.MinValue, short.MaxValue));
            }
            return curves;
        }

        static void Fill(ref FixedList128Bytes<short> list, float from, float to, Func<float, float> rootY)
        {
            float start = rootY(from), total = rootY(to) - start;
            list.Clear();
            for (int i = 0; i <= Samples; i++)
            {
                float t = math.lerp(from, to, (float)i / Samples);
                float share = math.abs(total) > 0.01f ? math.saturate((rootY(t) - start) / total) : 1f;
                if (i == 0) share = 0f;
                if (i == Samples) share = 1f;
                list.Add((short)math.round(share * 1000f));
            }
        }
    }

    /// <summary>The integer jump flight. Pure, Burst-safe; the server and the owner call it with the same plan and tick.</summary>
    public static class JumpFlight
    {
        /// <summary>A jump pressed on <paramref name="pressTick"/> from <paramref name="fromFeet"/>, facing the landing.</summary>
        public static MotionPlan NewJump(ushort seq, NetworkTick pressTick, int3 fromFeet, int3 landingFeet, int apexYMm) => new MotionPlan
        {
            Kind = MotionKind.Jump,
            PlanSeq = seq,
            StartTick = pressTick,
            FromXMm = fromFeet.x, FromYMm = fromFeet.y, FromZMm = fromFeet.z,
            ToXMm = landingFeet.x, ToYMm = landingFeet.y, ToZMm = landingFeet.z,
            ApexYMm = apexYMm,
            YawCdeg = YawCdeg(fromFeet, landingFeet),
        };

        /// <summary>
        /// The tick the server starts a jump on: the owner's press tick (<paramref name="claimed"/>), so a click that reaches the server
        /// late still starts where the owner's own plan started (GUI run predict-2, 31 Oct: client tick 113, server 117, the arc restarted
        /// 133 ms late). A claim only, clamped: never in the future, never more than the click window back (an older claim is not
        /// trusted: the server's own tick), and never before the previous jump's end (touchdown and cooldown) or the start of a server
        /// order (<paramref name="notBefore"/>, invalid = none), so a jump never reaches back over another action.
        /// </summary>
        public static NetworkTick ClickStartTick(NetworkTick now, NetworkTick claimed, in MotionPlan previous, NetworkTick notBefore,
            in PlayerMotionCurves curves, int tickMicros)
        {
            if (!now.IsValid || !claimed.IsValid || claimed.IsNewerThan(now)) return now;
            int windowTicks = tickMicros > 0 ? curves.ClickWindowMicros / tickMicros : 0;
            if (now.TicksSince(claimed) > windowTicks) return now;
            NetworkTick start = claimed;
            if (previous.Kind != MotionKind.None && previous.StartTick.IsValid && tickMicros > 0)
            {
                NetworkTick end = previous.StartTick;
                end.Add((uint)(((long)curves.LandMicros + curves.CooldownMicros + tickMicros - 1) / tickMicros));
                if (end.IsNewerThan(start)) start = end;
            }
            if (notBefore.IsValid && notBefore.IsNewerThan(start)) start = notBefore;
            return start.IsNewerThan(now) ? now : start;
        }

        /// <summary>The plan moves the body on this tick: a jump from its press to its touchdown (the touchdown tick included).</summary>
        public static bool Active(in MotionPlan plan, in PlayerMotionCurves curves, NetworkTick tick, int tickMicros)
        {
            if (plan.Kind == MotionKind.None || !plan.StartTick.IsValid || !tick.IsValid) return false;
            if (tick.TicksSince(plan.StartTick) < 0) return false;
            long elapsed = ElapsedMicros(in plan, tick, tickMicros);
            return elapsed < curves.LandMicros + tickMicros;    // the first tick at or after touchdown still places the landing
        }

        /// <summary>
        /// The plan places the body on this tick, evaluated at the tick itself (stateless, so a skipped or folded tick can never leave
        /// the body short; GUI run 1, 31 Oct): from the press through the flight, and on the landing from the touchdown on, until
        /// something newer moves the player: a walk clicked after the jump (<paramref name="walkSeq"/>, not a refused one) or a server
        /// order started after the press. A teleport clears the plan itself.
        /// </summary>
        public static bool Places(in MotionPlan plan, in PlayerMotionCurves curves, NetworkTick tick, int tickMicros,
            ushort walkSeq, ushort refusedSeq, NetworkTick orderStart)
        {
            if (plan.Kind == MotionKind.None || !plan.StartTick.IsValid || !tick.IsValid) return false;
            if (tick.TicksSince(plan.StartTick) < 0) return false;
            if (Active(in plan, in curves, tick, tickMicros)) return true;
            if (walkSeq != refusedSeq && SeqNewer(walkSeq, plan.PlanSeq)) return false;
            if (orderStart.IsValid && orderStart.IsNewerThan(plan.StartTick)) return false;
            return true;
        }

        /// <summary>
        /// The click number of the walk the player is on, or 0: <see cref="MoveState.OrderSeq"/> holds a server order's number (another
        /// counter) after a server order, and those are judged by their start tick instead.
        /// </summary>
        public static ushort ClickWalkSeq(in MoveState move) =>
            move.ServerSeq != 0 && move.OrderSeq == move.ServerSeq ? (ushort)0 : move.OrderSeq;

        /// <summary>Click number <paramref name="a"/> came after <paramref name="b"/> (numbers 1..0x7FFF that wrap; 0 = none).</summary>
        public static bool SeqNewer(ushort a, ushort b)
        {
            if (a == 0 || b == 0 || a == b) return false;
            int ahead = (a - b + 0x7FFF) % 0x7FFF;
            return ahead > 0 && ahead < 0x4000;
        }

        /// <summary>A new jump is refused until the landing cooldown after this plan's touchdown is over.</summary>
        public static bool CoolingDown(in MotionPlan plan, in PlayerMotionCurves curves, NetworkTick tick, int tickMicros)
        {
            if (plan.Kind == MotionKind.None || !plan.StartTick.IsValid || !tick.IsValid) return false;
            return ElapsedMicros(in plan, tick, tickMicros) < (long)curves.LandMicros + curves.CooldownMicros;
        }

        /// <summary>Microseconds of the plan's clip at <paramref name="tick"/> (0 on the press tick).</summary>
        public static long ElapsedMicros(in MotionPlan plan, NetworkTick tick, int tickMicros)
        {
            if (!plan.StartTick.IsValid || !tick.IsValid) return 0;
            int ticks = tick.TicksSince(plan.StartTick);
            return ticks <= 0 ? 0 : (long)ticks * tickMicros;
        }

        /// <summary>True once the clip has reached its touchdown.</summary>
        public static bool Landed(in MotionPlan plan, in PlayerMotionCurves curves, long elapsedMicros) => elapsedMicros >= curves.LandMicros;

        /// <summary>True from the take-off on (the Flux is paid then; an abort before it is free).</summary>
        public static bool TakenOff(in PlayerMotionCurves curves, long elapsedMicros) => elapsedMicros >= curves.TakeoffMicros;

        /// <summary>Where the apex is across the ground: travel is even over the air time, so it sits where the apex falls in time.</summary>
        public static int3 ApexMm(in MotionPlan plan, in PlayerMotionCurves curves)
        {
            long air = curves.LandMicros - curves.TakeoffMicros, toApex = curves.ApexMicros - curves.TakeoffMicros;
            int3 apex = Lerp(plan.FromMm, plan.ToMm, toApex, air);
            apex.y = plan.ApexYMm;
            return apex;
        }

        /// <summary>
        /// The feet at a clip time: the start until take-off; then even travel to the apex with the height following the clip's
        /// rise; then even travel to the landing with the height following its fall; the landing from touchdown on.
        /// </summary>
        public static int3 FeetAt(in MotionPlan plan, in PlayerMotionCurves curves, long elapsedMicros)
        {
            int3 from = plan.FromMm, to = plan.ToMm;
            if (elapsedMicros <= curves.TakeoffMicros) return from;
            if (elapsedMicros >= curves.LandMicros) return to;
            int3 apex = ApexMm(in plan, in curves);
            if (elapsedMicros <= curves.ApexMicros)
            {
                long num = elapsedMicros - curves.TakeoffMicros, den = curves.ApexMicros - curves.TakeoffMicros;
                int3 feet = Lerp(from, apex, num, den);
                feet.y = from.y + (int)((long)(apex.y - from.y) * Share(curves.Rise, num, den) / 1000);
                return feet;
            }
            else
            {
                long num = elapsedMicros - curves.ApexMicros, den = curves.LandMicros - curves.ApexMicros;
                int3 feet = Lerp(apex, to, num, den);
                feet.y = apex.y + (int)((long)(to.y - apex.y) * Share(curves.Fall, num, den) / 1000);
                return feet;
            }
        }

        /// <summary>
        /// How far the clip moves the body off the ghost's arc at a clip time (millimetres): its crouch before take-off, then what is left
        /// of it at the take-off fading out as the rise goes on (the clip leaves the ground a little above or below its start), none from
        /// the apex on. Drawn only, on top of <see cref="FeetAt"/>; the owner's answer (31 Oct): a look, not a timing rule.
        /// </summary>
        public static int CrouchMm(in PlayerMotionCurves curves, long elapsedMicros)
        {
            if (curves.Crouch.Length < 2 || elapsedMicros < 0 || elapsedMicros >= curves.ApexMicros) return 0;
            if (elapsedMicros < curves.TakeoffMicros) return Share(curves.Crouch, elapsedMicros, curves.TakeoffMicros);
            int atTakeoff = curves.Crouch[curves.Crouch.Length - 1];
            int risen = Share(curves.Rise, elapsedMicros - curves.TakeoffMicros, curves.ApexMicros - curves.TakeoffMicros);
            return atTakeoff * (1000 - risen) / 1000;
        }

        /// <summary>The way from <paramref name="from"/> to <paramref name="to"/>, hundredths of a degree 0..35999 (0 = +Z).</summary>
        public static int YawCdeg(int3 from, int3 to)
        {
            int dx = to.x - from.x, dz = to.z - from.z;
            if (dx == 0 && dz == 0) return 0;
            int cdeg = (int)math.round(math.degrees(math.atan2(dx, dz)) * 100.0);
            return ((cdeg % 36000) + 36000) % 36000;
        }

        /// <summary>The table's share (permille) at <paramref name="num"/>/<paramref name="den"/> of the phase, straight between samples.</summary>
        public static int Share(in FixedList128Bytes<short> table, long num, long den)
        {
            int last = table.Length - 1;
            if (last < 1 || den <= 0) return 1000;
            if (num <= 0) return table[0];
            if (num >= den) return table[last];
            long scaled = num * last;           // position in samples x den
            int i = (int)(scaled / den);
            long frac = scaled - (long)i * den; // 0..den-1
            return table[i] + (int)((table[i + 1] - table[i]) * frac / den);
        }

        static int3 Lerp(int3 a, int3 b, long num, long den)
        {
            if (den <= 0) return b;
            return new int3(
                a.x + (int)((long)(b.x - a.x) * num / den),
                a.y + (int)((long)(b.y - a.y) * num / den),
                a.z + (int)((long)(b.z - a.z) * num / den));
        }
    }
}

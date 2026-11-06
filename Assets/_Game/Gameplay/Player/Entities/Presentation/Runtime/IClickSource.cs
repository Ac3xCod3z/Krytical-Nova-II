using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace KryticalNova.Player.Entities.Presentation
{
    /// <summary>One thing the player did with the mouse or keys, already converted to the world: a click on the ground, or a stop.</summary>
    public struct WorldClick
    {
        /// <summary>The ground point in metres (the camera ray was already cast by whoever implements the source).</summary>
        public Vector3 Point;
        /// <summary>The click asks to run.</summary>
        public bool Run;
        /// <summary>A stop; <see cref="Point"/> is ignored.</summary>
        public bool Stop;
        /// <summary>The click asks to jump to <see cref="Point"/> (the Jump movement skill is selected).</summary>
        public bool Jump;
        /// <summary>A click on a wall for Wallstride's wall walk (M3c), and the surface's normal there.</summary>
        public bool Wall;
        public Vector3 Normal;
        /// <summary>An attack on this ghost (M4.4j; 0 = not an attack): the second click on a hostile body.</summary>
        public int AttackGhostId;
        /// <summary>The draw/sheathe key.</summary>
        public bool ToggleWeapon;
    }

    /// <summary>
    /// Where the player's clicks come from, as world points. The mouse/camera reader of the game implements it later (this assembly
    /// holds no UI and no camera code); <see cref="FakeClickSource"/> is the one for tests and dev bots. One click per frame at most.
    /// </summary>
    public interface IClickSource
    {
        bool TryTake(out WorldClick click);
    }

    /// <summary>A queue of clicks filled in code. Plain C#.</summary>
    public sealed class FakeClickSource : IClickSource
    {
        readonly Queue<WorldClick> m_Queue = new Queue<WorldClick>();

        public int Pending => m_Queue.Count;

        public void Click(Vector3 point, bool run = false) => m_Queue.Enqueue(new WorldClick { Point = point, Run = run });
        public void Stop() => m_Queue.Enqueue(new WorldClick { Stop = true });
        public void Jump(Vector3 point) => m_Queue.Enqueue(new WorldClick { Point = point, Jump = true });
        public void Clear() => m_Queue.Clear();

        public bool TryTake(out WorldClick click)
        {
            if (m_Queue.Count > 0) { click = m_Queue.Dequeue(); return true; }
            click = default;
            return false;
        }
    }

    /// <summary>
    /// Feeds an <see cref="IClickSource"/> to the existing input gather of the player ghost: metres become the millimetres the gather and
    /// the walk use. Install with <see cref="PlayerPresentationSetup.Install"/>.
    /// </summary>
    public sealed class ClickCommandSource : IPlayerCommandSource
    {
        readonly IClickSource m_Source;

        public ClickCommandSource(IClickSource source) => m_Source = source;

        public bool TryTake(out PlayerCommand command)
        {
            command = default;
            if (m_Source == null || !m_Source.TryTake(out var click)) return false;
            if (click.Stop) { command.Kind = PlayerCommandKind.Stop; return true; }
            if (click.ToggleWeapon) { command.Kind = PlayerCommandKind.ToggleWeapon; return true; }
            if (click.AttackGhostId != 0) { command.Kind = PlayerCommandKind.Attack; command.TargetGhostId = click.AttackGhostId; return true; }
            command.Kind = PlayerCommandKind.Click;
            command.Run = click.Run && !click.Jump;
            command.Jump = click.Jump;
            command.Wall = click.Wall;
            if (click.Wall) command.Run = click.Run;   // a wall click keeps Run with Jump (NGO's wall walk reads both)
            command.NormalPermille = new int3(Mm(click.Normal.x), Mm(click.Normal.y), Mm(click.Normal.z));
            command.GoalMm = new int3(Mm(click.Point.x), Mm(click.Point.y), Mm(click.Point.z));
            return true;
        }

        static int Mm(float metres) => (int)math.round(metres * 1000f);
    }
}

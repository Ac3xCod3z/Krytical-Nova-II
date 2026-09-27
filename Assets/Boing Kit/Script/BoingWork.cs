/******************************************************************************/
/*
  Project   - Boing Kit
  Publisher - Long Bunny Labs
              http://LongBunnyLabs.com
  Author    - Ming-Lun "Allen" Chou
              http://AllenChou.net
*/
/******************************************************************************/

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace BoingKit
{
  public static class BoingWork
  {
    /// <summary>
    /// Per-effector bit flags packed into <code>BoingEffector.Params.Bits</code>.
    /// </summary>
    public enum EffectorFlags
    {
      /// <summary>Treat the effector as continuously moving; tracks linear and angular impulses across frames.</summary>
      ContinuousMotion,
    }

    /// <summary>
    /// Per-reactor bit flags packed into <code>BoingWork.Params.Bits</code>.
    /// </summary>
    public enum ReactorFlags
    {
      /// <summary>Use 2D distance against <code>TwoDPlane</code> when testing effector influence.</summary>
      TwoDDistanceCheck,
      /// <summary>Restrict positional reaction to <code>TwoDPlane</code>.</summary>
      TwoDPositionInfluence,
      /// <summary>Restrict rotational reaction to <code>TwoDPlane</code>.</summary>
      TwoDRotationInfluence,
      /// <summary>Enable positional bouncy effect.</summary>
      EnablePositionEffect,
      /// <summary>Enable rotational bouncy effect.</summary>
      EnableRotationEffect,
      /// <summary>Enable scale bouncy effect.</summary>
      EnableScaleEffect,
      /// <summary>Interpret <code>RotationReactionUp</code> in world space instead of local space.</summary>
      GlobalReactionUpVector,
      /// <summary>Propagate reactor field values to neighboring cells.</summary>
      EnablePropagation,
      /// <summary>Hold propagation at the field's outer border (no wrap or leak).</summary>
      AnchorPropagationAtBorder,
      /// <summary>Tick this reactor in <code>FixedUpdate</code>.</summary>
      FixedUpdate,
      /// <summary>Tick this reactor early in <code>Update</code>.</summary>
      EarlyUpdate,
      /// <summary>Tick this reactor in <code>LateUpdate</code>.</summary>
      LateUpdate,
    }

    /// <summary>
    /// Bouncy VFX parameters.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public struct Params
    {
      public static readonly int Stride = //   352 bytes
          20 * sizeof(float)              // =  80 bytes
        +  8 * sizeof(int)                // +  32 bytes
        +  1 * InstanceData.Stride;       // + 240 bytes

      public static void Copy(ref Params from, ref Params to)
      {
        to.PositionParameterMode = from.PositionParameterMode;
        to.RotationParameterMode = from.RotationParameterMode;

        to.PositionExponentialHalfLife = from.PositionExponentialHalfLife;
        to.PositionOscillationHalfLife = from.PositionOscillationHalfLife;
        to.PositionOscillationFrequency = from.PositionOscillationFrequency;
        to.PositionOscillationDampingRatio = from.PositionOscillationDampingRatio;
        to.MoveReactionMultiplier = from.MoveReactionMultiplier;
        to.LinearImpulseMultiplier = from.LinearImpulseMultiplier;

        to.RotationExponentialHalfLife = from.RotationExponentialHalfLife;
        to.RotationOscillationHalfLife = from.RotationOscillationHalfLife;
        to.RotationOscillationFrequency = from.RotationOscillationFrequency;
        to.RotationOscillationDampingRatio = from.RotationOscillationDampingRatio;
        to.RotationReactionMultiplier = from.RotationReactionMultiplier;
        to.AngularImpulseMultiplier = from.AngularImpulseMultiplier;

        to.ScaleExponentialHalfLife = from.ScaleExponentialHalfLife;
        to.ScaleOscillationHalfLife = from.ScaleOscillationHalfLife;
        to.ScaleOscillationFrequency = from.ScaleOscillationFrequency;
        to.ScaleOscillationDampingRatio = from.ScaleOscillationDampingRatio;
      }

      // bytes 0-15 (16 bytes)
      public int InstanceID;
      public Bits32 Bits;
      [BoingDoc("2D plane on which distance is checked (if 2D Distance Check is enabled) and effector influence is applied (if 2D Influence is enabled).")]
      public TwoDPlaneEnum TwoDPlane;
      private int m_padding0;

      // bytes 16-31 (16 bytes)
      [BoingDoc("Each mode exposes a different set of parameters.\n\nExponential - Object position approaches its desired location exponentially at a specific half-life.\n\nOscillation by Half-Life - Object position approaches its desired location in an oscillating fashion at a specific half-life.\n\nOscillation by Damping Ratio - Object position approaches its desired location in an oscillating fashion at a specific damping ratio.")]
      public ParameterMode PositionParameterMode;
      [BoingDoc("Mode in which object's rotation approaches its desired rotation. Each mode exposes a different set of parameters.\n\nExponential - Object rotation approaches its desired rotation exponentially at a specific half-life.\n\nOscillation by Half-Life - Object rotation approaches its desired rotation in an oscillating fashion at a specific half-life.\n\nOscillation by Damping Ratio - Object rotation approaches its desired rotation in an oscillating fashion at a specific damping ratio.")]
      public ParameterMode RotationParameterMode;
      [BoingDoc("Each mode exposes a different set of parameters.\n\nExponential - Object scale approaches its desired value exponentially at a specific half-life.\n\nOscillation by Half-Life - Object scale approaches its desired value in an oscillating fashion at a specific half-life.\n\nOscillation by Damping Ratio - Object scale approaches its desired value in an oscillating fashion at a specific damping ratio.")]
      public ParameterMode ScaleParameterMode;
      private int m_padding1;

      // bytes 32-95 (64 bytes)
      [BoingDoc("Exponential half-life duration (in seconds). Every time this duration has elapsed, if the desired location maintains stationary, the distance between the object and its desired location is halved.")]
      [Range(0.0f,  5.0f)] public float PositionExponentialHalfLife;
      [BoingDoc("Oscillation half-life duration (in seconds). Every time this duration has elapsed, if the desired location maintains stationary, the position oscillation magnitude is halved.")]
      [Range(0.0f,  5.0f)] public float PositionOscillationHalfLife;
      [BoingDoc("Oscillation frequency (in hertz, i.e. one full back-and-forth oscillation per second).")]
      [Range(0.0f, 10.0f)] public float PositionOscillationFrequency;
      [BoingDoc("Damping ratio for oscillating positional approach effect.\n\nA value of 0.0 means no damping at all, and the object will oscillate for an extremely long time before settling at its desired location.\n\nA value of 1.0 means full damping, and the object will not oscillate at all, smoothly approaching its desired location (this is essentially the same as exponential mode, except less intuitive than tuning the half-life duration).\n\nAny value between 0.0 and 1.0 will cause the object to overshoot its desired location, oscillating back and forth before eventually settling down.")]
      [Range(0.0f,  1.0f)] public float PositionOscillationDampingRatio;
      [BoingDoc("Extra multiplier applied to influences from effectors. 1.0 means 100%.\n\ne.g. If an effector at maximum influence were to push the reactor's desired position away by 10.0, and MoveReactionMultiplier is set to 0.5, the desired position will only be 5.0 away.")]
      [Range(0.0f, 10.0f)] public float MoveReactionMultiplier;
      [BoingDoc("Extra multiplier applied to impulse influences from effectors. 1.0 means 100%.\n\ne.g. If an effector at maximum influence were to maintain a reactor's minimum movement speed at 10.0 units per second, and LinearImpulseMultiplier is set to 0.5, the minimum speed will only be maintained at 5.0 units per second.")]
      [Range(0.0f, 10.0f)] public float LinearImpulseMultiplier;
      [BoingDoc("Exponential half-life duration (in seconds). Every time this duration has elapsed, if the desired rotation maintains stationary, the angle between the object rotation and its desired rotation is halved.")]
      [Range(0.0f,  5.0f)] public float RotationExponentialHalfLife;
      [BoingDoc("Oscillation half-life duration (in seconds). Every time this duration has elapsed, if the desired rotation maintains stationary, the rotation oscillation magnitude is halved.")]
      [Range(0.0f,  5.0f)] public float RotationOscillationHalfLife;
      [BoingDoc("Oscillation frequency (in hertz, i.e. one full back-and-forth oscillation per second).")]
      [Range(0.0f, 10.0f)] public float RotationOscillationFrequency;
      [BoingDoc("Damping ratio for oscillating rotational approach effect.\n\nA value of 0.0 means no damping at all, and the object will oscillate for an extremely long time before settling at its desired rotation.\n\nA value of 1.0 means full damping, and the object will not oscillate at all, smoothly approaching its desired rotation (this is essentially the same as exponential mode, except less intuitive than tuning the half-life duration).\n\nAny value between 0.0 and 1.0 will cause the object to overshoot its desired rotation, oscillating back and forth before eventually settling down.")]
      [Range(0.0f,  1.0f)] public float RotationOscillationDampingRatio;
      [BoingDoc("Extra multiplier applied to influences from effectors.\n\ne.g. If an effector at maximum influence were to offset the reactor's desired rotation by 20 degrees, and RotationReactionMultiplier is set to 0.5, the desired rotation will only offset by 10 degrees.")]
      [Range(0.0f, 10.0f)] public float RotationReactionMultiplier;
      [BoingDoc("Extra multiplier applied to impulse influences from effectors.\n\ne.g. If an effector at maximum influence were to maintain a reactor's minimum rotation speed at 20.0 degrees per second, and AngularImpulseMultiplier is set to 0.5, the minimum rotation speed will only be maintained at 10.0 degrees per second.")]
      [Range(0.0f, 10.0f)] public float AngularImpulseMultiplier;
      [BoingDoc("Exponential half-life duration (in seconds). Every time this duration has elapsed, if the desired scale maintains stationary, the distance between the object's scale and its desired scale is halved.")]
      [Range(0.0f,  5.0f)] public float ScaleExponentialHalfLife;
      [BoingDoc("Oscillation half-life duration (in seconds). Every time this duration has elapsed, if the desired scale maintains stationary, the scale oscillation magnitude is halved.")]
      [Range(0.0f,  5.0f)] public float ScaleOscillationHalfLife;
      [BoingDoc("Oscillation frequency (in hertz, i.e. one full back-and-forth oscillation per second).")]
      [Range(0.0f, 10.0f)] public float ScaleOscillationFrequency;
      [BoingDoc("Damping ratio for oscillating scale approach effect.\n\nA value of 0.0 means no damping at all, and the object will oscillate for an extremely long time before settling at its desired scale.\n\nA value of 1.0 means full damping, and the object will not oscillate at all, smoothly approaching its desired scale (this is essentially the same as exponential mode, except less intuitive than tuning the half-life duration).\n\nAny value between 0.0 and 1.0 will cause the object to overshoot its desired scale, oscillating back and forth before eventually settling down.")]
      [Range(0.0f,  1.0f)] public float ScaleOscillationDampingRatio;

      // bytes 96-111 (16 bytes)
      [BoingDoc("Up vector (defined in object's local space) used for rotational reactions.\n\nIf an object's rotation is influenced by an effector, the up vector will be pushed away from the effector (and/or pulled in the direction of the effector's movement at the presence of angular impulse influence).")]
      public Vector3 RotationReactionUp;
      private float m_padding2;

      // bytes 112-351 (240 bytes)
      public InstanceData Instance;

      [StructLayout(LayoutKind.Sequential, Pack = 0)]
      public struct InstanceData
      {
        public static readonly int Stride = //   240 bytes
          + 32 * sizeof(float)              // = 128 bytes
          +  4 * sizeof(int)                // +  16 bytes
          +  2 * Vector3Spring.Stride       // +  64 bytes
          +  1 * QuaternionSpring.Stride;   // +  32 bytes

        // bytes 0-79 (80 bytes)
        public Vector3 PositionTarget;
        private float m_padding0;
        public Vector3 PositionOrigin; // for accumulated target
        private float m_padding1;
        public Vector4 RotationTarget;
        public Vector4 RotationOrigin; // for accumulated target
        public Vector3 ScaleTarget;
        private float m_padding2;

        // bytes 80-95 (16 bytes)
        private int m_numEffectors;
        private int m_instantAccumulation;
        private int m_padding3;
        private int m_padding4;

        // bytes 96-111 (16 bytes)
        private Vector3 m_upWs;
        private float m_minScale;

        // bytes 112-207 (96 bytes)
        public Vector3Spring PositionSpring; // input / output
        public QuaternionSpring RotationSpring; // input / output
        public Vector3Spring ScaleSpring; // input / output

        // bytes 208-239 (32 bytes)
        public Vector3 PositionPropagationWorkData; // temp for propagation
        private float m_padding5;
        public Vector4 RotationPropagationWorkData; // temp for propagation

        public void Reset()
        {
          PositionSpring.Reset();
          RotationSpring.Reset();
          ScaleSpring.Reset(Vector3.one, Vector3.zero);
          PositionPropagationWorkData = Vector3.zero;
          RotationPropagationWorkData = Vector3.zero;
        }

        public void Reset(Vector3 position, bool instantAccumulation)
        {
          PositionSpring.Reset(position);
          RotationSpring.Reset();
          ScaleSpring.Reset(Vector3.one, Vector3.zero);
          PositionPropagationWorkData = Vector3.zero;
          RotationPropagationWorkData = Vector3.zero;
          m_instantAccumulation = instantAccumulation ? 1 : 0;
        }

        // for BoingBehavior & BoingReactor
        public void PrepareExecute(ref Params p, Vector3 position, Quaternion rotation, Vector3 scale, bool accumulateEffectors)
        {
          PositionTarget = 
          PositionOrigin = position;

          RotationTarget = 
          RotationOrigin = QuaternionUtil.ToVector4(rotation);

          ScaleTarget = scale;

          m_minScale = VectorUtil.MinComponent(scale);

          if (accumulateEffectors)
          {
            // make relative
            PositionTarget = Vector3.zero;
            RotationTarget = Vector4.zero;

            m_numEffectors = 0;
            m_upWs = 
              p.Bits.IsBitSet((int) ReactorFlags.GlobalReactionUpVector) 
              ? p.RotationReactionUp 
              : rotation * VectorUtil.NormalizeSafe(p.RotationReactionUp, Vector3.up);
          }
          else
          {
            m_numEffectors = -1;
            m_upWs = Vector3.zero;
          }
        }

        // for BoingReactorFied
        public void PrepareExecute(ref Params p, Vector3 gridCenter, Quaternion gridRotation, Vector3 cellOffset)
        {
          PositionOrigin = gridCenter + cellOffset;
          RotationOrigin = QuaternionUtil.ToVector4(Quaternion.identity);
          
          // make relative
          PositionTarget = Vector3.zero;
          RotationTarget = Vector4.zero;

          m_numEffectors = 0;
          m_upWs = 
            p.Bits.IsBitSet((int) ReactorFlags.GlobalReactionUpVector) 
            ? p.RotationReactionUp 
            : gridRotation * VectorUtil.NormalizeSafe(p.RotationReactionUp, Vector3.up);

          m_minScale = 1.0f;
        }

        public void AccumulateTarget(ref Params p, ref BoingEffector.Params effector, float dt)
        {
          Vector3 effectRefPos = 
            effector.Bits.IsBitSet((int) BoingWork.EffectorFlags.ContinuousMotion) 
              ? VectorUtil.GetClosestPointOnSegment(PositionOrigin, effector.PrevPosition, effector.CurrPosition) 
              : effector.CurrPosition;

          Vector3 deltaPos = PositionOrigin - effectRefPos;

          Vector3 deltaPos3D = deltaPos;
          if (p.Bits.IsBitSet((int) ReactorFlags.TwoDDistanceCheck))
          {
            switch (p.TwoDPlane)
            {
              case TwoDPlaneEnum.XY: deltaPos.z = 0.0f; break;
              case TwoDPlaneEnum.XZ: deltaPos.y = 0.0f; break;
              case TwoDPlaneEnum.YZ: deltaPos.x = 0.0f; break;
            }
          }

          bool inRange = 
               Mathf.Abs(deltaPos.x) <= effector.Radius 
            && Mathf.Abs(deltaPos.y) <= effector.Radius 
            && Mathf.Abs(deltaPos.z) <= effector.Radius 
            && deltaPos.sqrMagnitude <= effector.Radius * effector.Radius;

          if (!inRange)
            return;

          float deltaDist = deltaPos.magnitude;
          float tDeltaDist =
            effector.Radius - effector.FullEffectRadius > MathUtil.Epsilon
            ? 1.0f - Mathf.Clamp01((deltaDist - effector.FullEffectRadius) / (effector.Radius - effector.FullEffectRadius))
            : 1.0f;

          Vector3 upWsPos = m_upWs;
          Vector3 upWsRot = m_upWs;
          Vector3 deltaDirPos = VectorUtil.NormalizeSafe(deltaPos3D, m_upWs);
          Vector3 deltaDirRot = deltaDirPos;

          if (p.Bits.IsBitSet((int) ReactorFlags.TwoDPositionInfluence))
          {
            switch (p.TwoDPlane)
            {
              case TwoDPlaneEnum.XY: deltaDirPos.z = 0.0f; upWsPos.z = 0.0f; break;
              case TwoDPlaneEnum.XZ: deltaDirPos.y = 0.0f; upWsPos.y = 0.0f; break;
              case TwoDPlaneEnum.YZ: deltaDirPos.x = 0.0f; upWsPos.x = 0.0f; break;
            }

            if (upWsPos.sqrMagnitude < MathUtil.Epsilon)
            {
              switch (p.TwoDPlane)
              {
                case TwoDPlaneEnum.XY: upWsPos = Vector3.up; break;
                case TwoDPlaneEnum.XZ: upWsPos = Vector3.forward; break;
                case TwoDPlaneEnum.YZ: upWsPos = Vector3.up; break;
              }
            }
            else
            {
              upWsPos.Normalize();
            }

            deltaDirPos = VectorUtil.NormalizeSafe(deltaDirPos, upWsPos);
          }

          if (p.Bits.IsBitSet((int) ReactorFlags.TwoDRotationInfluence))
          {
            switch (p.TwoDPlane)
            {
              case TwoDPlaneEnum.XY: deltaDirRot.z = 0.0f; upWsRot.z = 0.0f; break;
              case TwoDPlaneEnum.XZ: deltaDirRot.y = 0.0f; upWsRot.y = 0.0f; break;
              case TwoDPlaneEnum.YZ: deltaDirRot.x = 0.0f; upWsRot.x = 0.0f; break;
            }

            if (upWsRot.sqrMagnitude < MathUtil.Epsilon)
            {
              switch (p.TwoDPlane)
              {
                case TwoDPlaneEnum.XY: upWsRot = Vector3.up;      break;
                case TwoDPlaneEnum.XZ: upWsRot = Vector3.forward; break;
                case TwoDPlaneEnum.YZ: upWsRot = Vector3.up;      break;
              }
            }
            else
            {
              upWsRot.Normalize();
            }

            deltaDirRot = VectorUtil.NormalizeSafe(deltaDirRot, upWsRot);
          }

          if (p.Bits.IsBitSet((int) ReactorFlags.EnablePositionEffect))
          {
            Vector3 moveVec = tDeltaDist * p.MoveReactionMultiplier * effector.MoveDistance * deltaDirPos;
            PositionTarget += moveVec;

            PositionSpring.Velocity += tDeltaDist * p.LinearImpulseMultiplier * effector.LinearImpulse * effector.LinearVelocityDir * (60.0f * dt);
          }

          if (p.Bits.IsBitSet((int) ReactorFlags.EnableRotationEffect))
          {
            Vector3 rotAxis = VectorUtil.NormalizeSafe(Vector3.Cross(upWsRot, deltaDirRot), VectorUtil.FindOrthogonal(upWsRot));
            Vector3 rotVec = tDeltaDist * p.RotationReactionMultiplier * effector.RotateAngle * rotAxis;
            RotationTarget += QuaternionUtil.ToVector4(QuaternionUtil.FromAngularVector(rotVec));

            Vector3 angularImpulseDir = VectorUtil.NormalizeSafe(Vector3.Cross(effector.LinearVelocityDir, deltaDirRot - 0.01f * Vector3.up), rotAxis);
            float angularImpulseMag = tDeltaDist * p.AngularImpulseMultiplier * effector.AngularImpulse * (60.0f * dt);
            Vector4 angularImpulseDirQuat = QuaternionUtil.ToVector4(QuaternionUtil.FromAngularVector(angularImpulseDir));
            RotationSpring.VelocityVec += angularImpulseMag * angularImpulseDirQuat;
          }

          ++m_numEffectors;
        }

        public void EndAccumulateTargets(ref Params p)
        {
          if (m_numEffectors > 0)
          {
            PositionTarget *= m_minScale / m_numEffectors;
            PositionTarget += PositionOrigin;

            RotationTarget /= m_numEffectors;
            RotationTarget = QuaternionUtil.ToVector4(QuaternionUtil.FromVector4(RotationTarget) * QuaternionUtil.FromVector4(RotationOrigin));
          }
          else
          {
            PositionTarget = PositionOrigin;
            RotationTarget = RotationOrigin;
          }
        }

        public void Execute(ref Params p, float dt)
        {
          bool useAccumulatedEffectors = (m_numEffectors >= 0);

          bool positionSpringNeedsUpdate = 
            useAccumulatedEffectors 
            ? (PositionSpring.Velocity.sqrMagnitude > MathUtil.Epsilon 
               || (PositionSpring.Value - PositionTarget).sqrMagnitude > MathUtil.Epsilon) 
            : p.Bits.IsBitSet((int) ReactorFlags.EnablePositionEffect);
          bool rotationSpringNeedsUpdate = 
            useAccumulatedEffectors 
            ? (RotationSpring.VelocityVec.sqrMagnitude > MathUtil.Epsilon 
               || (RotationSpring.ValueVec - RotationTarget).sqrMagnitude > MathUtil.Epsilon) 
            : p.Bits.IsBitSet((int) ReactorFlags.EnableRotationEffect);
          bool scaleSpringNeedsUpdate = 
            p.Bits.IsBitSet((int) ReactorFlags.EnableScaleEffect) 
            && (ScaleSpring.Value - ScaleTarget).sqrMagnitude > MathUtil.Epsilon;

          if (m_numEffectors == 0)
          {
            bool earlyOut = true;

            if (positionSpringNeedsUpdate)
              earlyOut = false;
            else
              PositionSpring.Reset(PositionTarget);

            if (rotationSpringNeedsUpdate)
              earlyOut = false;
            else
              RotationSpring.Reset(QuaternionUtil.FromVector4(RotationTarget));

            if (earlyOut)
              return;
          }

          if (m_instantAccumulation != 0)
          {
            PositionSpring.Value = PositionTarget;
            RotationSpring.ValueVec = RotationTarget;
            ScaleSpring.Value = ScaleTarget;
            m_instantAccumulation = 0;
          }
          else
          {
            if (positionSpringNeedsUpdate)
            {
              switch (p.PositionParameterMode)
              {
                case ParameterMode.Exponential:
                  PositionSpring.TrackExponential(PositionTarget, p.PositionExponentialHalfLife, dt);
                  break;

                case ParameterMode.OscillationByHalfLife:
                  PositionSpring.TrackHalfLife(PositionTarget, p.PositionOscillationFrequency, p.PositionOscillationHalfLife, dt);
                  break;

                case ParameterMode.OscillationByDampingRatio:
                  PositionSpring.TrackDampingRatio(PositionTarget, p.PositionOscillationFrequency * MathUtil.TwoPi, p.PositionOscillationDampingRatio, dt);
                  break;
              }
            }
            else
            {
              PositionSpring.Value = PositionTarget;
              PositionSpring.Velocity = Vector3.zero;
            }

            if (rotationSpringNeedsUpdate)
            {
              switch (p.RotationParameterMode)
              {
                case ParameterMode.Exponential:
                  RotationSpring.TrackExponential(RotationTarget, p.RotationExponentialHalfLife, dt);
                  break;

                case ParameterMode.OscillationByHalfLife:
                  RotationSpring.TrackHalfLife(RotationTarget, p.RotationOscillationFrequency, p.RotationOscillationHalfLife, dt);
                  break;

                case ParameterMode.OscillationByDampingRatio:
                  RotationSpring.TrackDampingRatio(RotationTarget, p.RotationOscillationFrequency * MathUtil.TwoPi, p.RotationOscillationDampingRatio, dt);
                  break;
              }
            }
            else
            {
              RotationSpring.ValueVec = RotationTarget;
              RotationSpring.VelocityVec = Vector4.zero;
            }

            if (scaleSpringNeedsUpdate)
            {
              switch (p.ScaleParameterMode)
              {
                case ParameterMode.Exponential:
                  ScaleSpring.TrackExponential(ScaleTarget, p.ScaleExponentialHalfLife, dt);
                  break;

                case ParameterMode.OscillationByHalfLife:
                  ScaleSpring.TrackHalfLife(ScaleTarget, p.ScaleOscillationFrequency, p.ScaleOscillationHalfLife, dt);
                  break;

                case ParameterMode.OscillationByDampingRatio:
                  ScaleSpring.TrackDampingRatio(ScaleTarget, p.ScaleOscillationFrequency * MathUtil.TwoPi, p.ScaleOscillationDampingRatio, dt);
                  break;
              }
            }
            else
            {
              ScaleSpring.Value = ScaleTarget;
              ScaleSpring.Velocity = Vector3.zero;
            }
          }

          if (!useAccumulatedEffectors)
          {
            if (!positionSpringNeedsUpdate)
              PositionSpring.Reset(PositionTarget);
            if (!rotationSpringNeedsUpdate)
              RotationSpring.Reset(RotationTarget);
          }
        }

        public void PullResults(BoingBones bones)
        {
          for (int iChain = 0; iChain < bones.BoneData.Length; ++iChain)
          {
            var chain = bones.BoneChains[iChain];
            var aBone = bones.BoneData[iChain];

            if (aBone == null)
              continue;

            // must cache before manipulating bone transforms
            // otherwise, we'd cache delta propagated down from parent bones
            foreach (var bone in aBone)
            {
              bone.CachedPositionWs = bone.Transform.position;
              bone.CachedPositionLs = bone.Transform.localPosition;
              bone.CachedRotationWs = bone.Transform.rotation;
              bone.CachedRotationLs = bone.Transform.localRotation;
              bone.CachedScaleLs = bone.Transform.localScale;
            }

            // blend bone position
            for (int iBone = 0; iBone < aBone.Length; ++iBone)
            {
              var bone = aBone[iBone];

              // skip fixed root
              if (iBone == 0 && !chain.LooseRoot)
              {
                bone.BlendedPositionWs = bone.CachedPositionWs;
                continue;
              }

              bone.BlendedPositionWs =
                Vector3.Lerp
                (
                  bone.Instance.PositionSpring.Value,
                  bone.CachedPositionWs,
                  bone.AnimationBlend
                );
            }

            // rotation delta back-propagation
            {
              for (int iBone = 0; iBone < aBone.Length; ++iBone)
              {
                var bone = aBone[iBone];

                // skip fixed root
                if (iBone == 0 && !chain.LooseRoot)
                {
                  bone.BlendedRotationWs = bone.CachedRotationWs;
                  continue;
                }

                if (bone.ChildIndices == null)
                {
                  if (bone.ParentIndex >= 0)
                  {
                    var parentBone = aBone[bone.ParentIndex];
                    bone.BlendedRotationWs = parentBone.BlendedRotationWs * (parentBone.RotationInverseWs * bone.CachedRotationWs);
                  }

                  continue;
                }

                Vector3 bonePos = bone.CachedPositionWs;
                Vector3 boneBlendedPos = ComputeTranslationalResults(bone.Transform, bonePos, bone.BlendedPositionWs, bones);
                Quaternion boneRot;
                if (bones.TwistPropagation && bone.ParentIndex >= 0)
                {
                  var parentBone = aBone[bone.ParentIndex];
                  Vector3 axis = VectorUtil.NormalizeSafe(bonePos - parentBone.CachedPositionWs, Vector3.forward);
                  Quaternion cachedRotInv = Quaternion.Inverse(bone.CachedRotationWs);
                  Quaternion swing;
                  Quaternion twist;
                  QuaternionUtil.DecomposeSwingTwist(bone.SpringRotationWs * cachedRotInv, axis, out swing, out twist);
                  boneRot = twist * bone.CachedRotationWs;
                }
                else
                {
                  boneRot = bone.CachedRotationWs;
                }
                Quaternion boneRotInv = Quaternion.Inverse(boneRot);

                if (bones.EnableRotationEffect)
                {
                  Vector4 childRotDeltaPsVecAccumulator = Vector3.zero;
                  float totalWeight = 0.0f;
                  foreach (int iChild in bone.ChildIndices)
                  {
                    if (iChild < 0)
                      continue;

                    var childBone = aBone[iChild];

                    Vector3 childPos = childBone.CachedPositionWs;
                    Vector3 childPosDelta = childPos - bonePos;
                    Vector3 childPosDeltaDir = VectorUtil.NormalizeSafe(childPosDelta, Vector3.zero);

                    Vector3 childBlendedPos = ComputeTranslationalResults(childBone.Transform, childPos, childBone.BlendedPositionWs, bones);
                    Vector3 childBlendedPosDelta = childBlendedPos - boneBlendedPos;
                    Vector3 childBlendedPosDeltaDir = VectorUtil.NormalizeSafe(childBlendedPosDelta, Vector3.zero);

                    Quaternion childRotDelta = Quaternion.FromToRotation(childPosDeltaDir, childBlendedPosDeltaDir);
                    Quaternion childRotDeltaPs = boneRotInv * childRotDelta;

                    Vector4 childRotDeltaPsVec = QuaternionUtil.ToVector4(childRotDeltaPs);
                    float weight = Mathf.Max(MathUtil.Epsilon, chain.MaxLengthFromRoot - childBone.LengthFromRoot);
                    childRotDeltaPsVecAccumulator += weight * childRotDeltaPsVec;
                    totalWeight += weight;
                  }

                  if (totalWeight > 0.0f)
                  {
                    Vector4 avgChildRotDeltaPsVec = childRotDeltaPsVecAccumulator / totalWeight;
                    bone.RotationBackPropDeltaPs = QuaternionUtil.FromVector4(avgChildRotDeltaPsVec);
                    bone.BlendedRotationWs = (boneRot * bone.RotationBackPropDeltaPs) * boneRot;
                  }
                  else if (bone.ParentIndex >= 0)
                  {
                    var parentBone = aBone[bone.ParentIndex];
                    bone.BlendedRotationWs = parentBone.BlendedRotationWs * (parentBone.RotationInverseWs * boneRot);
                  }
                }
              }
            }

            // write blended position & adjusted rotation into final transforms
            for (int iBone = 0; iBone < aBone.Length; ++iBone)
            {
              var bone = aBone[iBone];

              // skip fixed root
              if (iBone == 0 && !chain.LooseRoot)
              {
                bone.Instance.PositionSpring.Reset(bone.CachedPositionWs);
                bone.Instance.RotationSpring.Reset(bone.CachedRotationWs);
                continue;
              }

              bone.Transform.position = ComputeTranslationalResults(bone.Transform, bone.Transform.position, bone.BlendedPositionWs, bones);
              bone.Transform.rotation = bone.BlendedRotationWs;
              bone.Transform.localScale = bone.BlendedScaleLs;
            }
          }
        }

        private void SuppressWarnings()
        {
          m_padding0 = 0;
          m_padding1 = 0;
          m_padding2 = 0;
          m_padding3 = 0;
          m_padding4 = 0;
          m_padding5 = 0;
          m_padding0 = m_padding1;
          m_padding1 = m_padding2;
          m_padding2 = m_padding3;
          m_padding3 = m_padding4;
          m_padding4 = (int) m_padding0;
          m_padding5 = m_padding0;
        }
      }

      public void Init()
      {
        InstanceID = ~0;

        Bits.Clear();

        TwoDPlane = TwoDPlaneEnum.XZ;

        PositionParameterMode = ParameterMode.OscillationByHalfLife;
        RotationParameterMode = ParameterMode.OscillationByHalfLife;
        ScaleParameterMode = ParameterMode.OscillationByHalfLife;

        PositionExponentialHalfLife = 0.02f;
        PositionOscillationHalfLife = 0.1f;
        PositionOscillationFrequency = 5.0f;
        PositionOscillationDampingRatio = 0.5f;
        MoveReactionMultiplier = 1.0f;
        LinearImpulseMultiplier = 1.0f;

        RotationExponentialHalfLife = 0.02f;
        RotationOscillationHalfLife = 0.1f;
        RotationOscillationFrequency = 5.0f;
        RotationOscillationDampingRatio = 0.5f;
        RotationReactionMultiplier = 1.0f;
        AngularImpulseMultiplier = 1.0f;

        ScaleExponentialHalfLife = 0.02f;
        ScaleOscillationHalfLife = 0.1f;
        ScaleOscillationFrequency = 5.0f;
        ScaleOscillationDampingRatio = 0.5f;

        Instance.Reset();
      }

      public void AccumulateTarget(ref BoingEffector.Params effector, float dt)
      {
        Instance.AccumulateTarget(ref this, ref effector, dt);
      }

      public void EndAccumulateTargets()
      {
        Instance.EndAccumulateTargets(ref this);
      }

      public void Execute(float dt)
      {
        Instance.Execute(ref this, dt);
      }

      public void Execute(BoingBones bones, float dt)
      {
        float maxCollisionResolutionPushLen = bones.MaxCollisionResolutionSpeed * dt;

        for (int iChain = 0; iChain < bones.BoneData.Length; ++iChain)
        {
          var chain = bones.BoneChains[iChain];
          var aBone = bones.BoneData[iChain];

          if (aBone == null)
            continue;

          // execute boing work
          for (int iBone = 0; iBone < aBone.Length; ++iBone) // skip root
          {
            var bone = aBone[iBone];

            if (chain.ParamsOverride == null)
            {
              bone.Instance.Execute(ref bones.Params, dt);
            }
            else
            {
              bone.Instance.Execute(ref chain.ParamsOverride.Params, dt);
            }
          }

          var rootBone = aBone[0];
          rootBone.ScaleWs = rootBone.BlendedScaleLs = rootBone.CachedScaleLs;

          rootBone.UpdateBounds();
          chain.Bounds = rootBone.Bounds;

          Vector3 rootAnimPos = rootBone.Transform.position;

          // apply length stiffness & volume preservation
          for (int iBone = 1; iBone < aBone.Length; ++iBone) // skip root
          {
            var bone = aBone[iBone];
            var parentBone = aBone[bone.ParentIndex];

            Vector3 toParentVec = parentBone.Instance.PositionSpring.Value - bone.Instance.PositionSpring.Value;
            Vector3 toParentDir = VectorUtil.NormalizeSafe(toParentVec, Vector3.zero);
            float toParentLen = toParentVec.magnitude;
            float fullyStiffLenDelta = toParentLen - bone.FullyStiffToParentLength;
            float toParentAdjustLen = bone.LengthStiffnessT * fullyStiffLenDelta;

            // length stiffness
            {
              bone.Instance.PositionSpring.Value += toParentAdjustLen * toParentDir;
              Vector3 velocityInParentAdjustDir = Vector3.Project(bone.Instance.PositionSpring.Velocity, toParentDir);
              bone.Instance.PositionSpring.Velocity -= bone.LengthStiffnessT  * velocityInParentAdjustDir;
            }

            // bend angle cap
            if (bone.BendAngleCap < MathUtil.Pi - MathUtil.Epsilon)
            {
              Vector3 animPos = bone.Transform.position;
              Vector3 posDelta = bone.Instance.PositionSpring.Value - rootAnimPos;
              posDelta = VectorUtil.ClampBend(posDelta, animPos - rootAnimPos, bone.BendAngleCap);
              bone.Instance.PositionSpring.Value = rootAnimPos + posDelta;
            }

            // volume preservation
            if (bone.SquashAndStretch > 0.0f)
            {
              float toParentLenRatio = toParentLen * MathUtil.InvSafe(bone.FullyStiffToParentLength);
              float volumePreservationScale = Mathf.Sqrt(1.0f / toParentLenRatio);
              volumePreservationScale = Mathf.Clamp(volumePreservationScale, 1.0f / Mathf.Max(1.0f, chain.MaxStretch), Mathf.Max(1.0f, chain.MaxSquash));
              Vector3 volumePreservationScaleVec = VectorUtil.ComponentWiseDivSafe(volumePreservationScale * Vector3.one, parentBone.ScaleWs);

              bone.BlendedScaleLs = 
                Vector3.Lerp
                (
                  Vector3.Lerp
                  (
                    bone.CachedScaleLs,
                    volumePreservationScaleVec, 
                    bone.SquashAndStretch
                  ),
                  bone.CachedScaleLs, 
                  bone.AnimationBlend
                );
            }
            else
            {
              bone.BlendedScaleLs = bone.CachedScaleLs;
            }

            bone.ScaleWs = VectorUtil.ComponentWiseMult(parentBone.ScaleWs, bone.BlendedScaleLs);

            bone.UpdateBounds();
            chain.Bounds.Encapsulate(bone.Bounds);
          }
          chain.Bounds.Expand(0.2f * Vector3.one);

          // Boing Kit colliders
          if (chain.EnableBoingKitCollision)
          {
            foreach (var collider in bones.BoingColliders)
            {
              if (collider == null)
                continue;

              if (!chain.Bounds.Intersects(collider.Bounds))
                continue;

              foreach (var bone in aBone)
              {
                if (!bone.Bounds.Intersects(collider.Bounds))
                  continue;

                Vector3 push;
                bool collided = collider.Collide(bone.Instance.PositionSpring.Value, bones.MinScale * bone.CollisionRadius, out push);
                if (!collided)
                  continue;

                bone.Instance.PositionSpring.Value += VectorUtil.ClampLength(push, 0.0f, maxCollisionResolutionPushLen);
                bone.Instance.PositionSpring.Velocity -= Vector3.Project(bone.Instance.PositionSpring.Velocity, push);
              }
            }
          }

          // Unity colliders
          var sharedSphereCollider = BoingManager.SharedSphereCollider;
          if (chain.EnableUnityCollision && sharedSphereCollider != null)
          {
            sharedSphereCollider.enabled = true;

            foreach (var collider in bones.UnityColliders)
            {
              if (collider == null)
                continue;

              if (!chain.Bounds.Intersects(collider.bounds))
                continue;

              foreach (var bone in aBone)
              {
                if (!bone.Bounds.Intersects(collider.bounds))
                  continue;

                sharedSphereCollider.center = bone.Instance.PositionSpring.Value;
                sharedSphereCollider.radius = bone.CollisionRadius;

                Vector3 pushDir;
                float pushDist;
                bool collided = 
                  Physics.ComputePenetration
                  (
                    sharedSphereCollider, Vector3.zero, Quaternion.identity,
                    collider, collider.transform.position, collider.transform.rotation, 
                    out pushDir, out pushDist
                  );
                if (!collided)
                  continue;

                bone.Instance.PositionSpring.Value += VectorUtil.ClampLength(pushDir * pushDist, 0.0f, maxCollisionResolutionPushLen);
                bone.Instance.PositionSpring.Velocity -= Vector3.Project(bone.Instance.PositionSpring.Velocity, pushDir);
              }
            }

            sharedSphereCollider.enabled = false;
          }

          // self collision
          if (chain.EnableInterChainCollision)
          {
            foreach (var bone in aBone)
            {
              for (int iOtherChain = iChain + 1; iOtherChain < bones.BoneData.Length; ++iOtherChain)
              {
                var otherChain = bones.BoneChains[iOtherChain];
                var aOtherBone = bones.BoneData[iOtherChain];

                if (aOtherBone == null)
                  continue;

                if (!otherChain.EnableInterChainCollision)
                  continue;

                if (!chain.Bounds.Intersects(otherChain.Bounds))
                  continue;

                foreach (var otherBone in aOtherBone)
                {
                  Vector3 push;
                  bool collided = 
                    Collision.SphereSphere
                    (
                      bone.Instance.PositionSpring.Value, 
                      bones.MinScale * bone.CollisionRadius, 
                      otherBone.Instance.PositionSpring.Value, 
                      bones.MinScale * otherBone.CollisionRadius, 
                      out push
                    );
                  if (!collided)
                    continue;

                  push = VectorUtil.ClampLength(push, 0.0f, maxCollisionResolutionPushLen);

                  float pushRatio = otherBone.CollisionRadius * MathUtil.InvSafe(bone.CollisionRadius + otherBone.CollisionRadius);
                  bone.Instance.PositionSpring.Value += pushRatio * push;
                  otherBone.Instance.PositionSpring.Value -= (1.0f - pushRatio) * push;

                  bone.Instance.PositionSpring.Velocity -= Vector3.Project(bone.Instance.PositionSpring.Velocity, push);
                  otherBone.Instance.PositionSpring.Velocity -= Vector3.Project(otherBone.Instance.PositionSpring.Velocity, push);
                }
              }
            }
          }
        } // end: foreach bone chain
      }

      public void PullResults(BoingBones bones)
      {
        Instance.PullResults(bones);
      }

      private void SuppressWarnings()
      {
        m_padding0 = 0;
        m_padding1 = 0;
        m_padding2 = 0.0f;
        m_padding0 = m_padding1;
        m_padding1 = m_padding0;
        m_padding2 = m_padding0;
      }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public struct Output
    {
      public static readonly int Stride = //   80 bytes
          4 * sizeof(int)                 // = 16 bytes
        + 1 * Vector3Spring.Stride        // + 32 bytes
        + 1 * QuaternionSpring.Stride;    // + 32 bytes

      // bytes 0-15 (16 bytes)
      public EntityId EntityId;
      public int m_padding0;
      public int m_padding1;

      // bytes 16-79 (64 bytes)
      public Vector3Spring PositionSpring;
      public QuaternionSpring RotationSpring;
      public Vector3Spring ScaleSpring;

      public Output(EntityId entityId, ref Vector3Spring positionSpring, ref QuaternionSpring rotationSpring, ref Vector3Spring scaleSpring)
      {
        EntityId = entityId;
        m_padding0 = m_padding1 = 0;
        PositionSpring = positionSpring;
        RotationSpring = rotationSpring;
        ScaleSpring = scaleSpring;
      }

      public void GatherOutput(Dictionary<EntityId, BoingBehavior> behaviorMap, BoingManager.UpdateMode updateMode)
      {
        BoingBehavior behavior;
        if (!behaviorMap.TryGetValue(EntityId, out behavior))
          return;

        if (!behavior.isActiveAndEnabled)
          return;

        if (behavior.UpdateMode != updateMode)
          return;

        behavior.GatherOutput(ref this);
        behavior.MarkExecuted();
      }

      public void GatherOutput(Dictionary<EntityId, BoingReactor> reactorMap, BoingManager.UpdateMode updateMode)
      {
        BoingReactor reactor;
        if (!reactorMap.TryGetValue(EntityId, out reactor))
          return;

        if (!reactor.isActiveAndEnabled)
          return;

        if (reactor.UpdateMode != updateMode)
          return;

        reactor.GatherOutput(ref this);
        reactor.MarkExecuted();
      }

      private void SuppressWarnings()
      {
        m_padding0 = 0;
        m_padding1 = 0;
        m_padding0 = m_padding1;
        m_padding1 = m_padding0;
      }
    }

    internal static Vector3 ComputeTranslationalResults(Transform t, Vector3 src, Vector3 dst, BoingBehavior b)
    {
      if (!b.LockTranslationX && !b.LockTranslationY && !b.LockTranslationZ)
      {
        return dst;
      }
      else
      {
        Vector3 delta = dst - src;

        switch (b.TranslationLockSpace)
        {
          case BoingManager.TransformLockSpace.Global:
            if (b.LockTranslationX)
              delta.x = 0.0f;
            if (b.LockTranslationY)
              delta.y = 0.0f;
            if (b.LockTranslationZ)
              delta.z = 0.0f;
            break;

          case BoingManager.TransformLockSpace.Local:
            if (b.LockTranslationX)
              delta -= Vector3.Project(delta, t.right);
            if (b.LockTranslationY)
              delta -= Vector3.Project(delta, t.up);
            if (b.LockTranslationZ)
              delta -= Vector3.Project(delta, t.forward);
            break;
        }

        return src + delta;
      }
    }

    internal static Quaternion ComputeRotationalResults(Transform t, Quaternion src, Quaternion dst, BoingBehavior b)
    {
      if (!b.LockRotationX && !b.LockRotationY && !b.LockRotationZ)
      {
        return dst;
      }
      else
      {
        Quaternion rInv = Quaternion.Inverse(t.rotation);

        switch (b.RotationLockSpace)
        {
          case BoingManager.TransformLockSpace.Local:
            src = src * rInv;
            dst = dst * rInv;
            break;
        }

        Quaternion delta = dst * Quaternion.Inverse(src);
        Vector3 eulerAngles = delta.eulerAngles;
        if (b.LockRotationX)
          eulerAngles.x = 0.0f;
        if (b.LockRotationY)
          eulerAngles.y = 0.0f;
        if (b.LockRotationZ)
          eulerAngles.z = 0.0f;
        delta.eulerAngles = eulerAngles;

        Quaternion res = delta * src;

        switch (b.RotationLockSpace)
        {
          case BoingManager.TransformLockSpace.Local:
            res = t.rotation * res;
            break;
        }

        return res;
      }
    }
  }
}

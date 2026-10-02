/******************************************************************************/
/*
  Project   - Boing Kit
  Publisher - Long Bunny Labs
              http://LongBunnyLabs.com
  Author    - Ming-Lun "Allen" Chou
              http://AllenChou.net
*/
/******************************************************************************/

#if UNITY_EDITOR
using UnityEditor;
#endif

using UnityEngine;

namespace BoingKit
{
  /// <summary>Applies spring-based motion to a single transform.</summary>
  public class BoingBehavior : BoingBase
  {
    [BoingDoc("Match this mode with how you update your object's transform.\n\n"
      + "Fixed Update - Use this mode if you update your object's transform in FixedUpdate(). Also use this mode if the game object is affected by Unity physics (i.e. has a Rigidbody), which uses fixed updates. This uses fixed Time.fixedDeltaTime.\n\n"
      + "Update - Only use this mode if your render logic pulls transforms during Update() and before LateUpdate(), such as Unity's skinned sprite renderer (in certain versions).\n\n"
      + "Late Update - Use this mode if you update your object's transform in Update(). This uses the variable Time.deltaTime.")]
    public BoingManager.UpdateMode UpdateMode = BoingManager.UpdateMode.LateUpdate;

    [BoingDoc("Enable to only check distance on a 2D plane.")]
    public bool TwoDDistanceCheck      = false;
    [BoingDoc("Enable to only apply effector position influence on a 2D plane.")]
    public bool TwoDPositionInfluence  = false;
    [BoingDoc("Enable to only apply effector rotation influence on a 2D plane.")]
    public bool TwoDRotationInfluence  = false;
    [BoingDoc("Check to enable position effects.")]
    public bool EnablePositionEffect   = true;
    [BoingDoc("Check to enable rotation effects.")]
    public bool EnableRotationEffect   = true;
    [BoingDoc("Check to enable scale effects.")]
    public bool EnableScaleEffect      = false;
    [BoingDoc("If checked, Reaction Up is defined in global space. If unchecked, Reaction Up is in local space.")]
    public bool GlobalReactionUpVector = false;

    [BoingDoc("Lock translation in global space or object's local space.")]
    public BoingManager.TransformLockSpace TranslationLockSpace = BoingManager.TransformLockSpace.Global;
    [BoingDoc("When locked, translational effects along the X axis are eliminated.")]
    public bool LockTranslationX = false;
    [BoingDoc("When locked, translational effects along the Y axis are eliminated.")]
    public bool LockTranslationY = false;
    [BoingDoc("When locked, translational effects along the Z axis are eliminated.")]
    public bool LockTranslationZ = false;

    [BoingDoc("Lock rotation in global space or object's local space.")]
    public BoingManager.TransformLockSpace RotationLockSpace = BoingManager.TransformLockSpace.Global;
    [BoingDoc("When locked, rotational effects along the X axis (in terms of Euler angles) are eliminated.")]
    public bool LockRotationX = false;
    [BoingDoc("When locked, rotational effects along the Y axis (in terms of Euler angles) are eliminated.")]
    public bool LockRotationY = false;
    [BoingDoc("When locked, rotational effects along the Z axis (in terms of Euler angles) are eliminated.")]
    public bool LockRotationZ = false;

    public BoingWork.Params Params;
    [BoingDoc("Shared boing parameters can be created as Unity assets by right-clicking in the Project menu and selecting Create > Boing Kit > Shared Boing Params. Useful for avoiding duplicate parameter management when multiple objects share identical parameters.")]
    public SharedBoingParams SharedParams;

    internal bool PositionSpringDirty = false;
    internal bool RotationSpringDirty = false;
    internal bool ScaleSpringDirty    = false;

    public Vector3Spring PositionSpring
    {
      get { return Params.Instance.PositionSpring; }
      set
      {
        Params.Instance.PositionSpring = value;
        PositionSpringDirty = true;
      }
    }

    public QuaternionSpring RotationSpring
    {
      get { return Params.Instance.RotationSpring; }
      set
      {
        Params.Instance.RotationSpring = value;
        RotationSpringDirty = true;
      }
    }

    public Vector3Spring ScaleSpring
    {
      get { return Params.Instance.ScaleSpring; }
      set
      {
        Params.Instance.ScaleSpring = value;
        ScaleSpringDirty = true;
      }
    }

    internal bool CachedTransformValid = false;
    internal Vector3 CachedPositionLs;
    internal Vector3 CachedPositionWs;
    internal Vector3 RenderPositionWs;
    internal Quaternion CachedRotationLs;
    internal Quaternion CachedRotationWs;
    internal Quaternion RenderRotationWs;
    internal Vector3 CachedScaleLs;
    internal Vector3 RenderScaleLs;

    internal bool InitRebooted = false;

    public BoingBehavior()
    {
      Params.Init();
    }

    public virtual void Reboot()
    {
      Params.Instance.PositionSpring.Reset(transform.position);
      Params.Instance.RotationSpring.Reset(transform.rotation);
      Params.Instance.ScaleSpring.Reset(transform.localScale);
      CachedPositionLs = transform.localPosition;
      CachedRotationLs = transform.localRotation;
      CachedPositionWs = transform.position;
      CachedRotationWs = transform.rotation;
      CachedScaleLs = transform.localScale;

      CachedTransformValid = true;
    }

    public virtual void HandleTeleport()
    {
      Params.Instance.PositionSpring.Value = transform.position;
      Params.Instance.RotationSpring.ValueQuat = transform.rotation;
      Params.Instance.ScaleSpring.Value = transform.localScale;
      CachedPositionLs = transform.localPosition;
      CachedRotationLs = transform.localRotation;
      CachedPositionWs = transform.position;
      CachedRotationWs = transform.rotation;
      CachedScaleLs = transform.localScale;

      CachedTransformValid = true;
    }

    public override void OnEnable()
    {
      base.OnEnable();
      CachedTransformValid = false;
      InitRebooted = false;
      Register();
    }

    public void Start()
    {
      InitRebooted = false;
    }

    public override void OnDisable()
    {
      base.OnDisable();
      Unregister();
    }

    protected virtual void Register()
    {
      BoingManager.Register(this);
    }

    protected virtual void Unregister()
    {
      BoingManager.Unregister(this);
    }

    public void UpdateFlags()
    {
      Params.Bits.SetBit((int) BoingWork.ReactorFlags.TwoDDistanceCheck     , TwoDDistanceCheck     );
      Params.Bits.SetBit((int) BoingWork.ReactorFlags.TwoDPositionInfluence , TwoDPositionInfluence );
      Params.Bits.SetBit((int) BoingWork.ReactorFlags.TwoDRotationInfluence , TwoDRotationInfluence );
      Params.Bits.SetBit((int) BoingWork.ReactorFlags.EnablePositionEffect  , EnablePositionEffect  );
      Params.Bits.SetBit((int) BoingWork.ReactorFlags.EnableRotationEffect  , EnableRotationEffect  );
      Params.Bits.SetBit((int) BoingWork.ReactorFlags.EnableScaleEffect     , EnableScaleEffect  );
      Params.Bits.SetBit((int) BoingWork.ReactorFlags.GlobalReactionUpVector, GlobalReactionUpVector);

      Params.Bits.SetBit((int) BoingWork.ReactorFlags.FixedUpdate, (UpdateMode == BoingManager.UpdateMode.FixedUpdate));
      Params.Bits.SetBit((int) BoingWork.ReactorFlags.EarlyUpdate, (UpdateMode == BoingManager.UpdateMode.EarlyUpdate));
      Params.Bits.SetBit((int) BoingWork.ReactorFlags.LateUpdate, (UpdateMode == BoingManager.UpdateMode.LateUpdate));
    }

    public virtual void PrepareExecute()
    {
      PrepareExecute(false);
    }

    protected void PrepareExecute(bool accumulateEffectors)
    {
      if (SharedParams != null)
        BoingWork.Params.Copy(ref SharedParams.Params, ref Params);

      UpdateFlags();

      Params.InstanceID = GetEntityId().GetHashCode();

      Params.Instance.PrepareExecute
      (
        ref Params, 
        CachedPositionWs, 
        CachedRotationWs, 
        transform.localScale, 
        accumulateEffectors
      );
    }

    public void Execute(float dt)
    {
      Params.Execute(dt);
    }

    public void PullResults()
    {
      PullResults(ref Params);
    }

    public void GatherOutput(ref BoingWork.Output o)
    {
        if (BoingManager.UseAsynchronousJobs)
        {
          if (PositionSpringDirty)
            PositionSpringDirty = false;
          else
            Params.Instance.PositionSpring = o.PositionSpring;

          if (RotationSpringDirty)
            RotationSpringDirty = false;
          else
            Params.Instance.RotationSpring = o.RotationSpring;

          if (ScaleSpringDirty)
            ScaleSpringDirty = false;
          else
            Params.Instance.ScaleSpring = o.ScaleSpring;
        }
        else
        {
          Params.Instance.PositionSpring = o.PositionSpring;
          Params.Instance.RotationSpring = o.RotationSpring;
          Params.Instance.ScaleSpring = o.ScaleSpring;
        }
    }

    private void PullResults(ref BoingWork.Params p)
    {
      CachedPositionLs = transform.localPosition;
      CachedPositionWs = transform.position;
      RenderPositionWs = BoingWork.ComputeTranslationalResults(transform, transform.position, p.Instance.PositionSpring.Value, this);
      transform.position = RenderPositionWs;

      CachedRotationLs = transform.localRotation;
      CachedRotationWs = transform.rotation;
      RenderRotationWs = BoingWork.ComputeRotationalResults(transform, transform.rotation, p.Instance.RotationSpring.ValueQuat, this);
      transform.rotation = RenderRotationWs;

      CachedScaleLs = transform.localScale;
      RenderScaleLs = p.Instance.ScaleSpring.Value;
      transform.localScale = RenderScaleLs;

      CachedTransformValid = true;
    }

    virtual public void Restore()
    {
      if (!CachedTransformValid)
        return;

      if (Application.isEditor)
      {
        // transforms can be manually modified in editor between post-update and pre-update
        // we respect that by skipping restoration of cached transforms

        if ((transform.position - RenderPositionWs).sqrMagnitude < 1e-4f)
          transform.localPosition = CachedPositionLs;

        if (QuaternionUtil.GetAngle(transform.rotation * Quaternion.Inverse(RenderRotationWs)) < 1e-2f)
          transform.localRotation = CachedRotationLs;

        if ((transform.localScale - RenderScaleLs).sqrMagnitude < 1e-4f)
          transform.localScale = CachedScaleLs;
      }
      else
      {
        transform.localPosition = CachedPositionLs;
        transform.localRotation = CachedRotationLs;
        transform.localScale = CachedScaleLs;
      }
    }
  }
}


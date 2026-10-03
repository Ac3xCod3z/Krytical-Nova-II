
using System;
using UnityEngine;
using CTFX2.CameraShake.Utility;

namespace CTFX2.CameraShake
{
	[Serializable]
	public class BurstSettings : IShakeSettings
	{
		public float strength = 0.1f;
		public float angularVelocity = 0.5f;
		public float inTime = 0.025f;
		public EEasing inEasing = EEasing.EaseInOutSine;
		public float outTime = 0.1f;
		public EEasing outEasing = EEasing.EaseInOutSine;
	}

	[Serializable]
	public class Burst : AShake<BurstSettings>, IShake
	{
		private Vector3 targetPosition, targetRotation;

		private enum State { In, Out, Done }
		private State state;

		public Burst(BurstSettings settings, Vector3 direction, Vector3 rotation) : base(settings)
		{
			targetPosition = direction.normalized * settings.strength;
			targetRotation = rotation.normalized * settings.angularVelocity;
			state = State.In;
			counter = new Counter(settings.inTime);
		}


		public override void Init(Transform transform)
		{
			base.Init(transform);
			state = State.In;
			counter = new Counter(settings.inTime);
		}

		public override bool Step()
		{
			switch (state)
			{
				case State.In:
					float tIn = 1 - counter.GetNormalized();
					float easedIn = settings.inEasing.ToFunction()(tIn);
					position = Vector3.Lerp(Vector3.zero, targetPosition, easedIn);
					rotation = Vector3.Lerp(Vector3.zero, targetRotation, easedIn);

					if (counter.Done())
					{
						state = State.Out;
						counter = new Counter(settings.outTime);
					}
					break;
				case State.Out:
					float tOut = 1 - counter.GetNormalized();
					float easedOut = settings.outEasing.ToFunction()(tOut);
					position = Vector3.Lerp(targetPosition, Vector3.zero, easedOut);
					rotation = Vector3.Lerp(targetRotation, Vector3.zero, easedOut);

					if (counter.Done())
					{
						state = State.Done;
					}
					break;
				case State.Done:
					position = Vector3.zero;
					rotation = Vector3.zero;
					return true;
			}
			counter.Step();
			return base.Step();
		}
	}
}
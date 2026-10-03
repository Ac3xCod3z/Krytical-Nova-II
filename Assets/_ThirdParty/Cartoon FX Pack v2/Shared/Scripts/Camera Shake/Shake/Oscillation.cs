using System;
using CTFX2.CameraShake.Utility;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CTFX2.CameraShake
{
	[Serializable]
	public class OscillationSettings : IShakeSettings
	{
		public float strength = 0.1f;
		public float angularVelocity = 0.5f;
		public float speed = 3;
		public int cycles = 2;
		public EEasing easing = EEasing.EaseInOutSine;
	}

	[Serializable]
	public class Oscillation : AShake<OscillationSettings>, IShake
	{
		private Vector3 targetPosition, targetRotation;
		private int cycle;

		public Oscillation(OscillationSettings settings, Vector3 direction, Vector3 rotation) : base(settings)
		{
			targetPosition = direction.normalized;
			targetRotation = rotation.normalized;
			counter = new Counter(1f / settings.speed);
		}

		public override void Init(Transform transform)
		{
			base.Init(transform);
			cycle = 0;
		}

		public override bool Step()
		{
			float t = counter.Get();
			float dt = t - counter.Step();
			counter.Set(t - dt * settings.speed);
			t = counter.Get();

			// Oscillation phase: 0..1 for each cycle
			float phase = t * settings.speed * Mathf.PI * 2f;
			float decay = Mathf.Pow(1f - (float)cycle / settings.cycles, 2f);
			float osc = Mathf.Sin(phase) * decay;

			// Add randomness per cycle
			Vector3 randomPos = Random.insideUnitSphere * 0.5f;
			Vector3 randomEuler = Random.insideUnitSphere * 0.5f;

			position = osc * settings.strength * (targetPosition + randomPos);
			rotation = osc * settings.angularVelocity * (targetRotation + randomEuler);

			if (counter.Done())
			{
				if (++cycle > settings.cycles) return true;

				targetPosition = (targetPosition + Random.insideUnitSphere * 0.5f).normalized;
				targetRotation = (targetRotation + Random.insideUnitSphere * 0.5f).normalized;
				counter.Reset();
			}

			return base.Step();
		}
	}

}
using UnityEngine;
using CTFX2.CameraShake.Utility;

namespace CTFX2.CameraShake
{
	public interface IShakeSettings { }

	public interface IShake
	{
		/// <summary>
		/// Initializes the shake effect with the given transform.
		/// </summary>
		/// <param name="transform">The transform to apply the shake effect to.</param>
		void Init(Transform transform);

		/// <summary>
		/// Performs a step in the shake effect.
		/// </summary>
		/// <returns>True if the shake effect is still active; otherwise, false.</returns>
		bool Step();

		/// <summary>
		/// Disposes of the shake effect and releases any resources used.
		/// </summary>
		void Dispose();
	}

	public abstract class AShake<T> : IShake where T : IShakeSettings
	{
		[SerializeField] protected T settings;

		protected Vector3 position, rotation;
		protected Counter counter;

		protected Vector3 origin, originRotation;
		protected Transform transform;

		public AShake(T settings) => this.settings = settings;

		public virtual void Init(Transform transform)
		{
			this.transform = transform;
			origin = transform.transform.position;
			originRotation = transform.transform.eulerAngles;
		}
		public virtual bool Step()
		{
			transform.position = origin + position;
			transform.rotation = Quaternion.Euler(originRotation + rotation);
			return false;
		}

		public void Dispose()
		{
			transform.position = origin;
			transform.rotation = Quaternion.Euler(originRotation);

			position = Vector3.zero;
			rotation = Vector3.zero;
			origin = Vector3.zero;
			originRotation = Vector3.zero;
			counter?.Reset();
		}
	}
}
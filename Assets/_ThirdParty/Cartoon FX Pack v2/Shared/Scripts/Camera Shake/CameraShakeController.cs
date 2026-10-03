using System.Collections;
using UnityEngine;

namespace CTFX2.CameraShake
{
	[AddComponentMenu("CTFX2/Camera Shake Controller")]
	[DisallowMultipleComponent]
	public class CameraShakeController : MonoBehaviour
	{
		[Tooltip("Whether the camera can shake or not.")]
		[SerializeField] private bool canShake = true;

		private Coroutine Ishake;
		private CameraShake current;
		private bool isShaking;

		/// <summary>
		/// Triggers a camera shake using a provided <see cref="CameraShake"/> instance.
		/// </summary>
		/// <param name="shake">The <see cref="CameraShake"/> object containing shake parameters.</param>
		public void Shake(CameraShake shake)
		{
			if (!canShake) return;

			if (isShaking)
			{
				isShaking = false;
				if (Ishake != null) StopCoroutine(Ishake);
				current?.Dispose();
			}

			shake.Shake(transform);

			current = shake;
			Ishake = StartCoroutine(Shake());

			IEnumerator Shake()
			{
				isShaking = true;
				while (!shake.Step())
					yield return new WaitForEndOfFrame();
				isShaking = false;
				shake.Dispose();
			}
		}
	}
}

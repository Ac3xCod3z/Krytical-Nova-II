using System;
using UnityEngine;

namespace CTFX2.CameraShake.Utility
{

	public enum EEasing
	{
		Linear,

		EaseInSine,
		EaseOutSine,
		EaseInOutSine,

		EaseInBounce,
		EaseOutBounce,
		EaseInOutBounce,

		EaseInQuad,
		EaseOutQuad,
		EaseInOutQuad,

		EaseInCubic,
		EaseOutCubic,
		EaseInOutCubic,

		EaseInQuart,
		EaseOutQuart,
		EaseInOutQuart,

		EaseInQuint,
		EaseOutQuint,
		EaseInOutQuint,

		EaseInExpo,
		EaseOutExpo,
		EaseInOutExpo,

		EaseInCirc,
		EaseOutCirc,
		EaseInOutCirc,
	}

	public static class UEasing
	{
		/// <summary>
		/// Converts an <see cref="EEasing"/> enumeration value into a corresponding easing function.
		/// </summary>
		/// <param name="easing">The easing type to convert.</param>
		/// <returns>A function that calculates the eased value for a given input (between 0 and 1).</returns>
		public static Func<float, float> ToFunction(this EEasing easing)
		{
			return easing switch
			{
				// Linear
				EEasing.Linear => x => x,

				// Sine
				EEasing.EaseInSine => x => 1 - Mathf.Cos((x * Mathf.PI) / 2),
				EEasing.EaseOutSine => x => Mathf.Sin((x * Mathf.PI) / 2),
				EEasing.EaseInOutSine => x => -(Mathf.Cos(Mathf.PI * x) - 1) / 2,

				// Bounce
				EEasing.EaseInBounce => x => 1 - EEasing.EaseOutBounce.ToFunction()(1 - x),
				EEasing.EaseOutBounce => x =>
				{
					const float n1 = 7.5625f;
					const float d1 = 2.75f;

					if (x < 1 / d1)
						return n1 * x * x;
					else if (x < 2 / d1)
						return n1 * (x -= 1.5f / d1) * x + 0.75f;
					else if (x < 2.5 / d1)
						return n1 * (x -= 2.25f / d1) * x + 0.9375f;
					else
						return n1 * (x -= 2.625f / d1) * x + 0.984375f;
				}
				,
				EEasing.EaseInOutBounce => x => x < 0.5
					? (1 - EEasing.EaseOutBounce.ToFunction()(1 - 2 * x)) / 2
					: (1 + EEasing.EaseOutBounce.ToFunction()(2 * x - 1)) / 2,

				// Quadratic
				EEasing.EaseInQuad => x => x * x,
				EEasing.EaseOutQuad => x => 1 - (1 - x) * (1 - x),
				EEasing.EaseInOutQuad => x => x < 0.5 ? 2 * x * x : 1 - Mathf.Pow(-2 * x + 2, 2) / 2,

				// Cubic
				EEasing.EaseInCubic => x => x * x * x,
				EEasing.EaseOutCubic => x => 1 - Mathf.Pow(1 - x, 3),
				EEasing.EaseInOutCubic => x => x < 0.5 ? 4 * x * x * x : 1 - Mathf.Pow(-2 * x + 2, 3) / 2,

				// Quartic
				EEasing.EaseInQuart => x => x * x * x * x,
				EEasing.EaseOutQuart => x => 1 - Mathf.Pow(1 - x, 4),
				EEasing.EaseInOutQuart => x => x < 0.5 ? 8 * x * x * x * x : 1 - Mathf.Pow(-2 * x + 2, 4) / 2,

				// Quintic
				EEasing.EaseInQuint => x => x * x * x * x * x,
				EEasing.EaseOutQuint => x => 1 - Mathf.Pow(1 - x, 5),
				EEasing.EaseInOutQuint => x => x < 0.5 ? 16 * x * x * x * x * x : 1 - Mathf.Pow(-2 * x + 2, 5) / 2,

				// Exponential
				EEasing.EaseInExpo => x => Mathf.Approximately(x, 0) ? 0 : Mathf.Pow(2, 10 * x - 10),
				EEasing.EaseOutExpo => x => Mathf.Approximately(x, 1) ? 1 : 1 - Mathf.Pow(2, -10 * x),
				EEasing.EaseInOutExpo => x =>
					Mathf.Approximately(x, 0)
						? 0
						: Mathf.Approximately(x, 1)
							? 1
							: x < 0.5
								? Mathf.Pow(2, 20 * x - 10) / 2
								: (2 - Mathf.Pow(2, -20 * x + 10)) / 2,

				// Circular
				EEasing.EaseInCirc => x => 1 - Mathf.Sqrt(1 - Mathf.Pow(x, 2)),
				EEasing.EaseOutCirc => x => Mathf.Sqrt(1 - Mathf.Pow(x - 1, 2)),
				EEasing.EaseInOutCirc => x => x < 0.5
					? (1 - Mathf.Sqrt(1 - Mathf.Pow(2 * x, 2))) / 2
					: (Mathf.Sqrt(1 - Mathf.Pow(-2 * x + 2, 2)) + 1) / 2,
				_ => x => x,
			};
		}
	}

}
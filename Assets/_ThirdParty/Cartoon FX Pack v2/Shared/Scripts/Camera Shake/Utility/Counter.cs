using UnityEngine;

namespace CTFX2.CameraShake.Utility
{

	/// <summary>
	/// A utility class to manage timers or countdowns with various features such as stepping, resetting, and randomization.
	/// </summary>
	public class Counter
	{
		/// <summary>
		/// Gets the total time duration of the counter.
		/// </summary>
		public float time { get; private set; }
		private float counter
		{
			get => _counter;
			set => _counter = Mathf.Clamp(value, 0f, time);
		}
		private float _counter;

		private bool hasBeenCalled;


		/// <summary>
		/// Initializes a new instance of the <see cref="Counter"/> class.
		/// </summary>
		/// <param name="time">The total duration of the counter.</param>
		/// <param name="instant">If true, initializes the counter to 0; otherwise, initializes it to <paramref name="time"/>.</param>
		public Counter(float time, bool instant = false)
		{
			hasBeenCalled = false;

			if (time == 0) time = Time.fixedDeltaTime;

			this.time = time;
			Set(instant ? 0 : time);
		}

		/// <summary>
		/// Randomizes the counter's current value within the range of 0 to <see cref="time"/>.
		/// </summary>
		public void Randomize()
			=> Set(Random.Range(0, time));

		/// <summary>
		/// Sets the counter to a specific value, clamped between 0 and <see cref="time"/>.
		/// </summary>
		/// <param name="t">The value to set the counter to.</param>
		/// <returns>The clamped value that was set.</returns>
		public float Set(float t)
		{
			counter = t;
			return counter;
		}
		/// <summary>
		/// Gets the current value of the counter.
		/// </summary>
		/// <returns>The current counter value.</returns>
		public float Get() => counter;
		/// <summary>
		/// Gets the current value of the counter normalized between 0 and 1.
		/// </summary>
		/// <returns>The normalized counter value.</returns>
		public float GetNormalized() => Get() / time;

		/// <summary>
		/// Decreases the counter by the specified time step.
		/// </summary>
		/// <param name="deltaT">The time step to decrease the counter by.</param>
		/// <returns>The updated counter value.</returns>
		public float Step(float deltaT)
		{
			counter -= deltaT;
			return counter;
		}
		/// <summary>
		/// Decreases the counter by the time elapsed since the last frame (<see cref="Time.deltaTime"/>).
		/// </summary>
		/// <returns>The updated counter value.</returns>
		public float Step()
		{
			counter -= Time.deltaTime;
			return counter;
		}

		/// <summary>
		/// Checks if the counter has reached zero and returns true the first time it is called after reaching zero.
		/// </summary>
		/// <returns>True if the counter is <= 0 and this is the first call; otherwise, false.</returns>
		public bool DoneOnce()
		{
			if (counter <= 0 && !hasBeenCalled)
			{
				hasBeenCalled = true;
				return true;
			}
			return false;
		}
		/// <summary>
		/// Checks if the counter has reached zero. Optionally resets the counter if <paramref name="reset"/> is true.
		/// </summary>
		/// <param name="reset">If true, resets the counter after it is done.</param>
		/// <returns>True if the counter is <= 0; otherwise, false.</returns>
		public bool Done(bool reset = true)
		{
			if (counter <= 0)
			{
				if (reset) Reset();
				return true;
			}
			return false;
		}

		/// <summary>
		/// Resets the counter to its initial state.
		/// </summary>
		/// <param name="instant">If true, resets the counter to 0; otherwise, resets it to <see cref="time"/>.</param>
		public void Reset(bool instant = false)
			=> Set(instant ? 0 : time);
	}

}
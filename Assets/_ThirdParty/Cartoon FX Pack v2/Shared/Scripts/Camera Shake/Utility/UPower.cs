namespace CTFX2.CameraShake.Utility
{

	public static class Power
	{
		public static float Pow(this float value, Degree degree)
		{
			return degree switch
			{
				Degree.Linear => value,
				Degree.Quadratic => value * value,
				Degree.Cubic => value * value * value,
				Degree.Quadric => value * value * value * value,
				_ => value,
			};
		}
	}

	public enum Degree { Linear, Quadratic, Cubic, Quadric }

}
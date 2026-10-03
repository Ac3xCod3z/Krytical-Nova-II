using UnityEngine;

namespace CTFX2.CameraShake
{
	public abstract class CameraShakeObject : ScriptableObject
	{
		public abstract IShake GetShake(Vector3 direction, Vector3 rotation);
	}
}
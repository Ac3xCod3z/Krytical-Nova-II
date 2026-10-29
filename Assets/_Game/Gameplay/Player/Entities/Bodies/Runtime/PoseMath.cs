using Unity.Mathematics;
using UnityEngine;

namespace KryticalNova.Player.Entities.Bodies
{
    /// <summary>
    /// Metres and degrees of a GameObject body to and from the integer pose of the player ghost (millimetres, hundredths of a
    /// degree about Y). The same rounding as the walk code, so a body placed from a pose and read back gives the same pose.
    /// </summary>
    public static class PoseMath
    {
        public static int3 Mm(Vector3 metres) => new int3(
            Mathf.RoundToInt(metres.x * 1000f), Mathf.RoundToInt(metres.y * 1000f), Mathf.RoundToInt(metres.z * 1000f));

        public static Vector3 Metres(int3 mm) => new Vector3(mm.x, mm.y, mm.z) * 0.001f;

        public static Vector3 Metres(in PlayerPose pose) => Metres(pose.Mm);

        /// <summary>A yaw in degrees as 0..35999 hundredths of a degree.</summary>
        public static int Cdeg(float yawDegrees)
        {
            int c = Mathf.RoundToInt(yawDegrees * 100f) % 36000;
            return c < 0 ? c + 36000 : c;
        }

        public static int Cdeg(Quaternion rotation) => Cdeg(rotation.eulerAngles.y);

        public static Quaternion Rotation(int yawCdeg) => Quaternion.Euler(0f, yawCdeg * 0.01f, 0f);
    }
}

using UnityEngine;

namespace GI
{
    class Spectator : MonoBehaviour
    {
        [HideInInspector]
        public float Horizontal;

        [HideInInspector]
        public float Vertical;

        public float Speed = 1;

        private void Awake()
        {
            var vector = gameObject.transform.forward;

            Horizontal = Util.HorizontalAngle(vector);
            Vertical = Util.VerticalAngle(vector);
        }

        private void Update()
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                float horizontal_speed = 1;
                float vertical_speed = 1;

                Horizontal += Input.GetAxis("Mouse X") * horizontal_speed;
                Vertical -= Input.GetAxis("Mouse Y") * vertical_speed;

                Horizontal = Horizontal % 360;

                var padding = 0.01f;
                Vertical = Mathf.Clamp(Vertical, -90 + padding, 90 - padding);
            }

            gameObject.transform.forward = Util.Vector(Horizontal, Vertical);

            var forward = Input.GetAxis("Vertical") * gameObject.transform.forward;
            var right = Input.GetAxis("Horizontal") * gameObject.transform.right;

            var speed = Speed * 10 * Time.deltaTime;

            if (Input.GetKey(KeyCode.LeftShift))
                speed *= 2;

            if (Input.GetKey(KeyCode.LeftControl))
                speed *= 10;

            gameObject.transform.position += (forward + right) * speed;
        }
    }
}

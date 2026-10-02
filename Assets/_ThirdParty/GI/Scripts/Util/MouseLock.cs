using UnityEngine;

namespace GI
{
    public class MouseLock : MonoBehaviour
    {
        private bool _is_locked = true;

        private void LateUpdate()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                _is_locked = false;

            if (Input.GetMouseButtonDown(0))
                _is_locked = true;

            if (_is_locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
    }
} 
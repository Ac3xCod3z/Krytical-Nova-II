using CTFX2.Creator.Data;
using CTFX2.Creator.Tool;
using PSD.Creator.Data;
using PSD.Creator.Objects;
using PSD.Creator.Tool;
using UnityEngine;

namespace PSD.Creator.Components
{
    public abstract class PSDComponent<T, S> : Component<T, CartoonFX, CartoonFXBasic>
        where T : ComponentSettings<S>
        where S : PSDObject
    {
        private Camera mainCamera;

        private void FaceCamera()
        {
            mainCamera ??= Camera.main;
            if (mainCamera == null) return;

            Vector3 direction = mainCamera.transform.position - transform.position;
            direction.y = 0;

            if (direction.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(direction);
        }

        protected virtual void Start()
        {
            if (!IsGamePlaying() || BasicSettings.alignment != CartoonFXBasic.Alignment.SpawnFacingCamera)
                return;

            FaceCamera();
        }

        protected virtual void Update()
        {
            if (!IsGamePlaying() || BasicSettings.alignment != CartoonFXBasic.Alignment.AlwaysFaceCamera)
                return;

            FaceCamera();
        }
    }
}
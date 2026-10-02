using UnityEngine;

namespace GI
{
    public class ShowFPS : MonoBehaviour
    {
        void Update()
        {
            _delta_time += (Time.deltaTime - _delta_time) * 0.1f;

            _ms = _delta_time * 1000.0f;
            _fps = 1.0f / _delta_time;
        }

        void OnGUI()
        {
            string text = string.Format("{0:0.0} ms ({1:0.} fps)", _ms, _fps);
            GUI.color = Color.red;
            GUI.Label(new Rect(0, 0, 100, 30), text);
        }

        public float _delta_time = 0;
        private float _ms;
        private float _fps;
    }
}
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace WuHaipeng.smear
{
    public class Ellen_0_control : MonoBehaviour
    {


        public float speed;

        private bool is_left=true;


        // Update is called once per frame
        void Update()
        {
            if (this.is_left)
            {
                this.transform.position -= new Vector3(this.speed * Time.deltaTime, 0, 0);
                float x = this.transform.position.x;
                if (x < -8.5f)
                {
                    this.is_left = false;
                    transform.rotation = Quaternion.Euler(0, 90, 0);
                }
            }
            else
            {
                this.transform.position += new Vector3(this.speed * Time.deltaTime, 0, 0);
                float x = this.transform.position.x;
                if (x > -2.5f)
                {
                    this.is_left = true;
                    transform.rotation = Quaternion.Euler(0, -90, 0);
                }
            }
        }
    }
}
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace WuHaipeng.smear
{
    [RequireComponent(typeof(MeshRenderer))]
    public class Smear_control_mesh_renderer : MonoBehaviour
    {
        [Header("is render smear")]
        public bool is_render_smear = true;

        [Header("frame count")]
        public int frame_count = 10;

        private Material[] material_array_smear;

        private Queue<Vector3> positions_quene = new Queue<Vector3>();

        private MeshRenderer mesh_renderer;

        void Start()
        {
            this.mesh_renderer = GetComponent<MeshRenderer>();
            this.material_array_smear = this.mesh_renderer.materials;

            for (int i = 0; i < this.material_array_smear.Length; i++)
            {
                string shader_name = this.material_array_smear[i].shader.name;
                if (shader_name == "WuHaipeng/smear_urp" || shader_name == "WuHaipeng/smear_hdrp" || shader_name == "WuHaipeng/smear_built-in")
                {
                    this.material_array_smear[i].SetVector("position_current", this.mesh_renderer.bounds.center);
                    this.material_array_smear[i].SetVector("position_previous", this.mesh_renderer.bounds.center); 
                }
            }
        }


        void FixedUpdate()
        {
            if (this.is_render_smear == true)
            {
                Vector3 v3_position = this.mesh_renderer.bounds.center;

                for (int i = 0; i < this.material_array_smear.Length; i++)
                {
                    string shader_name = this.material_array_smear[i].shader.name;
                    if (shader_name == "WuHaipeng/smear_urp" || shader_name == "WuHaipeng/smear_hdrp" || shader_name == "WuHaipeng/smear_built-in")
                    {

                        if (positions_quene.Count > frame_count)
                            this.material_array_smear[i].SetVector("position_previous", positions_quene.Dequeue());

                        this.material_array_smear[i].SetVector("position_current", v3_position);

                        this.positions_quene.Enqueue(v3_position);
                    }
                }
            }
            else
            {
                for (int i = 0; i < this.material_array_smear.Length; i++)
                {
                    string shader_name = this.material_array_smear[i].shader.name;
                    if (shader_name == "WuHaipeng/smear_urp" || shader_name == "WuHaipeng/smear_hdrp" || shader_name == "WuHaipeng/smear_built-in")
                    {
                        if (this.material_array_smear[i].GetVector("position_previous") != this.material_array_smear[i].GetVector("position_current"))
                        {
                            this.material_array_smear[i].SetVector("position_previous", this.material_array_smear[i].GetVector("position_current"));
                        }
                    }
                }
            }
        }
    }
}
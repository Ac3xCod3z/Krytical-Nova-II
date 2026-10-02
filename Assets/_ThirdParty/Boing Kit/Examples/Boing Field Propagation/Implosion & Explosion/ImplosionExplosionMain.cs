using BoingKit;
using UnityEngine;

public class ImplosionExplosionMain : MonoBehaviour
{
  public BoingReactorField ReactorField;
  
  public GameObject Diamond;
  public int NumDiamonds;

  private static readonly int kNumInstancedBushesPerDrawCall = 1000; // Unity 5 doesn't like 1024 and I don't like 1023 *sigh*
  private Matrix4x4[][] m_aaInstancedDiamondMatrix;
  private MaterialPropertyBlock m_diamondMaterialProps;

  public void Start()
  {
    m_aaInstancedDiamondMatrix = new Matrix4x4[(NumDiamonds + kNumInstancedBushesPerDrawCall - 1) / kNumInstancedBushesPerDrawCall][];
    for (int i = 0; i < m_aaInstancedDiamondMatrix.Length; ++i)
    {
      m_aaInstancedDiamondMatrix[i] = new Matrix4x4[kNumInstancedBushesPerDrawCall];
    }
    for (int i = 0; i < NumDiamonds; ++i)
    {
      float scale = Random.Range(0.1f, 0.4f);

      Vector3 position =
        new Vector3
        (
          Random.Range(-3.5f, 3.5f), 
          Random.Range( 0.5f, 7.0f), 
          Random.Range(-3.5f, 3.5f)
        );

      Quaternion rotation = Quaternion.Euler(Random.Range(0.0f, 360.0f), Random.Range(0.0f, 360.0f), Random.Range(0.0f, 360.0f));

      m_aaInstancedDiamondMatrix[i / kNumInstancedBushesPerDrawCall][i % kNumInstancedBushesPerDrawCall].SetTRS(position, rotation, scale * Vector3.one);
    }
  }

  public void Update()
  {
    var mesh = Diamond.GetComponent<MeshFilter>().sharedMesh;
    var material = Diamond.GetComponent<MeshRenderer>().sharedMaterial;

    if (m_diamondMaterialProps == null)
      m_diamondMaterialProps = new MaterialPropertyBlock();

    if (ReactorField.UpdateShaderConstants(m_diamondMaterialProps))
    {
      foreach (var aInstancedBushMatrix in m_aaInstancedDiamondMatrix)
      {
        Graphics.DrawMeshInstanced(mesh, 0, material, aInstancedBushMatrix, aInstancedBushMatrix.Length, m_diamondMaterialProps);
      }
    }
  }
}

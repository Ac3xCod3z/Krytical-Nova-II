float3 ToonLight(
    float2 uv,
    float3 lightDirWS,
    float3 lightColor,
    float attenuation,
    float bands
)
{
    float2 p = uv * 2.0 - 1.0;

    float3 right = normalize(UNITY_MATRIX_V[0].xyz);
    float3 up    = normalize(UNITY_MATRIX_V[1].xyz);

    float3 N = normalize(right * p.x + up * p.y + float3(0, 0, 1));

    float3 L = normalize(lightDirWS);

    float ndotl = saturate(dot(N, L));
    ndotl = floor(ndotl * bands) / bands;

    float atten = step(0.2, attenuation);

    return lightColor * ndotl * atten;
}
float2 RotateUV(float2 uv, float degrees)
{
    float rotation = degrees * PI / 180;

    float2 tempUV = uv - 0.5;

    float s = sin(rotation);
    float c = cos(rotation);

    float2x2 rotMatrix = float2x2(c, -s, s, c);
    tempUV = mul(rotMatrix, tempUV);

    return tempUV + 0.5;
}
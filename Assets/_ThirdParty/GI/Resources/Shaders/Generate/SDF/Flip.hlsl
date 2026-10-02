uint rotateSign(float fl)
{
    uint f = asuint(fl);
    return (f << 1) | (f >> 31);
}

uint unrotateSign(uint f2)
{
    return (f2 >> 1) | (f2 << 31);
}
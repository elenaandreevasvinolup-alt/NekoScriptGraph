// Только тело функции становится блоками; шапка и объявления переносятся
// как есть. Внутри — письменное подмножество NSG.
float attenuate(float3 lightDir, float3 normal, float distance, float range)
{
    float lambert = max(dot(normal, lightDir), 0.0);
    float falloff = 1.0 - distance / range;
    falloff = falloff < 0.0 ? 0.0 : falloff;
    float squared = falloff * falloff;
    float result = lambert * squared;
    int i = 0;
    while (i < 4)
    {
        result *= 0.5;
        i++;
    }
    return result;
}

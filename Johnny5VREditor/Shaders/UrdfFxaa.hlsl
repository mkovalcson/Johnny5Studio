// Compile with Windows SDK fxc: /T ps_3_0 /E main /Fo UrdfFxaa.ps UrdfFxaa.hlsl
// Directional FXAA on the premultiplied 3D surface. Filter alpha with RGB so
// empty areas stay transparent and silhouette edges blend with any backdrop.
sampler2D source : register(s0);
float brightness : register(c0);
float gamma : register(c1);
float contrast : register(c2);
float highlights : register(c3);
float2 pixelStep : register(c4);
float quality : register(c5);

float edgeLuma(float4 p)
{
    // Alpha participates so dark geometry against transparency is detected too.
    return dot(p.rgb, float3(0.299, 0.587, 0.114)) + p.a * 0.25;
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 pixel = tex2D(source, uv);
    float4 nw = tex2D(source, uv + float2(-1, -1) * pixelStep);
    float4 ne = tex2D(source, uv + float2(1, -1) * pixelStep);
    float4 sw = tex2D(source, uv + float2(-1, 1) * pixelStep);
    float4 se = tex2D(source, uv + float2(1, 1) * pixelStep);
    float lm = edgeLuma(pixel), lnw = edgeLuma(nw), lne = edgeLuma(ne), lsw = edgeLuma(sw), lse = edgeLuma(se);
    float lmin = min(lm, min(min(lnw, lne), min(lsw, lse)));
    float lmax = max(lm, max(max(lnw, lne), max(lsw, lse)));
    float threshold = lerp(0.125, 0.0312, saturate((quality - 1) / 3));
    if (lmax - lmin > max(0.0156, lmax * threshold))
    {
        float2 dir = float2(-((lnw + lne) - (lsw + lse)), (lnw + lsw) - (lne + lse));
        float reduction = max((lnw + lne + lsw + lse) * (0.25 * 0.125), 1.0 / 128);
        float inverseMin = 1 / (min(abs(dir.x), abs(dir.y)) + reduction);
        float span = 2 + quality * 2;
        dir = clamp(dir * inverseMin, -span, span) * pixelStep;
        float4 a = 0.5 * (tex2D(source, uv + dir * (-1.0 / 6)) + tex2D(source, uv + dir * (1.0 / 6)));
        float4 b = a * 0.5 + 0.25 * (tex2D(source, uv - dir * 0.5) + tex2D(source, uv + dir * 0.5));
        float lb = edgeLuma(b);
        pixel = lb < lmin || lb > lmax ? a : b;
    }
    float3 color = saturate(pixel.rgb / max(pixel.a, 0.00001));
    float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
    color *= brightness * lerp(1.0, highlights, smoothstep(0.55, 1.0, luminance));
    color = pow(max(color, 0.0), 1.0 / gamma);
    color = (color - 0.5) * contrast + 0.5;
    return float4(saturate(color) * pixel.a, pixel.a);
}

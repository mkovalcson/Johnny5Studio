// Compile with Windows SDK fxc: /T ps_2_0 /E main /Fo UrdfTone.ps UrdfTone.hlsl
sampler2D source : register(s0);
float brightness : register(c0);
float gamma : register(c1);
float contrast : register(c2);
float highlights : register(c3);
float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 pixel = tex2D(source, uv);
    float3 color = saturate(pixel.rgb / max(pixel.a, 0.00001));
    float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
    color *= brightness * lerp(1.0, highlights, smoothstep(0.55, 1.0, luminance));
    color = pow(max(color, 0.0), 1.0 / gamma);
    color = (color - 0.5) * contrast + 0.5;
    return float4(saturate(color) * pixel.a, pixel.a);
}

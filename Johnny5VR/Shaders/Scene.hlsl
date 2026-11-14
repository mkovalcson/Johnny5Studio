cbuffer Frame : register(b0)
{
    row_major float4x4 ViewProjection;
    float4 EyePosition;
    float4 Apertures;
    float4 Flags;
    float4 AmbientColor;
    float4 KeyColor;
    float4 KeyDirection;
    float4 Mouth;
};
struct Bone {row_major float4x4 transform;};
StructuredBuffer<Bone> Bones:register(t0);
struct Surface {float4 diffuse;float4 emissive;float4 specular;float4 parameters;};
StructuredBuffer<Surface> Surfaces:register(t1);
Texture2DArray Images:register(t2);
SamplerState LinearSampler:register(s0);
StructuredBuffer<float4> RingColors:register(t3);
struct Input { float3 position:POSITION; float3 normal:NORMAL; float4 color:COLOR; uint bone:BLENDINDICES; float3 morph:TEXCOORD0; uint iris:TEXCOORD1; float2 uv:TEXCOORD2; uint mouth:TEXCOORD3; };
struct Pixel { float4 position:SV_POSITION; float3 normal:NORMAL; float4 color:COLOR; float3 world:TEXCOORD0;float2 uv:TEXCOORD1;nointerpolation uint material:TEXCOORD2;nointerpolation uint mouth:TEXCOORD3; };
Pixel VS(Input v)
{
    float delta=v.iris==1?Apertures.x:v.iris==2?Apertures.y:0;
    float3 world=mul(float4(v.position+v.morph*delta,1),Bones[v.bone].transform).xyz;
    if(Flags.z>1.5)world=v.position;
    Pixel p;p.position=mul(float4(world,1),ViewProjection);p.normal=mul(float4(v.normal,0),Bones[v.bone].transform).xyz;p.color=v.color;if(Flags.z>1.5)p.normal=v.normal;p.world=world;p.uv=v.uv;p.material=Flags.y>0?v.bone:0;p.mouth=v.mouth;return p;
}
float4 PS(Pixel p, bool front:SV_IsFrontFace):SV_TARGET
{
    if(Flags.z>1.5)
    {
        float3 n=normalize(p.normal),view=normalize(EyePosition.xyz-p.world);if(dot(n,view)<0)n=-n;
        float3 light=normalize(float3(-.4,.9,.35));float spec=pow(saturate(dot(n,normalize(light+view))),38);
        return float4(saturate(p.color.rgb*(.32+.68*saturate(dot(n,light)))+spec*.48),1);
    }
    if(Flags.z>.5)return p.color;
    float3 view=normalize(EyePosition.xyz-p.world);
    float3 n=normalize(p.normal);
    // URDF sources can mix winding conventions; shade the visible side.
    if(dot(n,view)<0)n=-n;
    Surface surface=Surfaces[p.material];p.color*=surface.diffuse;
    float4 texel=surface.parameters.x>=0?Images.Sample(LinearSampler,float3(p.uv,surface.parameters.x)):float4(1,1,1,1);
    p.color*=texel;
    if(Flags.y>0){clip(p.color.a-.002);if(Flags.x<.5&&p.color.a<.99)discard;if(Flags.x>.5&&p.color.a>=.99)discard;}
    float3 light=Flags.y>0?normalize(KeyDirection.xyz):normalize(float3(-.4,.85,.4));
    float diffuse=max(0,dot(n,light));
    float fill=max(0,dot(n,normalize(float3(.7,.3,-.6))));
    float spec=pow(max(0,dot(n,normalize(light+view))),max(1,surface.parameters.y));
    float3 lighting=Flags.y>0?AmbientColor.rgb+KeyColor.rgb*diffuse+.15*fill:.38+.55*diffuse+.20*fill;
    float3 color=p.color.rgb*lighting+spec*surface.specular.rgb+surface.emissive.rgb*texel.rgb;
    if(Flags.y==0&&p.mouth>0&&p.mouth<128)
    {
        float level;float3 offColor,onColor,emission;
        if(p.mouth<=14)
        {
            uint index=p.mouth-1;uint pair=index<=6?6-index:index-7;
            level=saturate(Mouth.x*7-pair);offColor=float3(154,82,28)/255;onColor=float3(255,146,32)/255;emission=float3(255,104,8)/255;
        }
        else if(p.mouth<64)
        {
            level=saturate(Mouth.x*12-(p.mouth-32));offColor=float3(118,31,25)/255;onColor=float3(255,62,38)/255;emission=float3(255,28,12)/255;
        }
        else
        {
            level=saturate(Mouth.x*12-(p.mouth-64));offColor=float3(46,92,40)/255;onColor=float3(82,255,86)/255;emission=float3(32,255,48)/255;
        }
        color=lerp(offColor,onColor,level)*lighting+emission*pow(level,.72);
    }
    if(Flags.y==0&&p.mouth>=128)
    {
        bool halo=p.mouth>=384;uint lamp=halo?p.mouth-384:p.mouth-128;
        float4 rgb=RingColors[lamp];float level=rgb.a;float3 hue=rgb.rgb/max(.0001,max(rgb.r,max(rgb.g,rgb.b)));
        float intensity=lamp<64?((lamp/16)%2==1?Mouth.z:Mouth.y):(lamp<66?Mouth.y:Mouth.z);
        if(halo)
        {
            clip(level-.001);return float4(hue,min(.98,(190.0/255)*pow(level,.52)*sqrt(intensity)));
        }
        if(lamp<64)
        {
            float3 lens=level<=.001?float3(.043,.043,.047):lerp(rgb.rgb,hue,.22+.2*level);
            float boost=(lamp/16)%2==1?3.1:1.75;return float4(saturate(lens*lighting+min(1,rgb.rgb*boost*sqrt(intensity))),1);
        }
        if(lamp<66)
        {
            if(level<=.001)return float4(0,0,0,1);
            return float4(saturate(.06*lighting+hue*.34*level*sqrt(intensity)),(150-38*level)/255);
        }
        clip(level-.001);float strength=min(1,level*sqrt(intensity));
        return float4(saturate(hue*strength*(.28*lighting+.78)),(120+115*strength)/255);
    }
    if(p.color.a<.1)
    {
        float fresnel=pow(1-abs(dot(n,view)),4);
        return float4(.65,.8,.95,min(.42,p.color.a+fresnel*.3+spec*.35));
    }
    return float4(saturate(color),p.color.a);
}

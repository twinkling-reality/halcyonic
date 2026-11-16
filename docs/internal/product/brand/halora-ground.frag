// The Halora ground: dawn over a calm sea in the kingfisher's own colours (docs/internal/product/BRAND.md).
// A WebGL 1 fragment shader. R is the canvas size in pixels; S is the seed (3.1 for the approved board).
// Domain-warped noise mapped across the brand ramp, light above and sea below, with a slight vignette.
// The page that shows it blurs it (22 px on a 1000 px canvas) and lays film grain over it after the blur.
// GPUs differ in this noise: the approved rasters were drawn by SwiftShader, and any new frame is drawn
// the same way and compared with them first (BRAND.md, "The ground").
precision highp float; uniform vec2 R; uniform float S;
float h(vec2 p){ return fract(sin(dot(p, vec2(127.1,311.7))) * 43758.5453); }
float n(vec2 p){ vec2 i=floor(p), f=fract(p); f=f*f*(3.-2.*f);
  return mix(mix(h(i),h(i+vec2(1,0)),f.x), mix(h(i+vec2(0,1)),h(i+vec2(1,1)),f.x), f.y); }
float fbm(vec2 p){ float v=0., a=.5; for(int i=0;i<4;i++){ v+=a*n(p); p=p*2.03+vec2(1.7,9.2); a*=.5; } return v; }
vec3 ramp(float t){
  vec3 cream=vec3(.99,.94,.86), apricot=vec3(.98,.74,.50), orange=vec3(.93,.47,.20), rose=vec3(.90,.55,.48),
       teal=vec3(.25,.64,.70), deep=vec3(.11,.42,.49);
  if(t<.22) return mix(deep, teal, t/.22);
  if(t<.38) return mix(teal, cream, (t-.22)/.16);
  if(t<.55) return mix(cream, apricot, (t-.38)/.17);
  if(t<.78) return mix(apricot, orange, (t-.55)/.23);
  return mix(orange, rose, (t-.78)/.22);
}
void main(){ vec2 uv = gl_FragCoord.xy / R.y; vec2 p = uv*0.85 + S;
  vec2 q = vec2(fbm(p), fbm(p+vec2(5.2,1.3)));
  vec2 r = vec2(fbm(p+3.0*q+vec2(1.7,9.2)), fbm(p+3.0*q+vec2(8.3,2.8)));
  float t = fbm(p + 2.2*r);
  t = smoothstep(.18,.88,t);
  t = clamp(t*.75 + (uv.y - .30)*.62, 0., 1.);
  vec3 c = ramp(t);
  float v = length(uv - vec2(R.x/R.y*.5, .5)); c *= 1. - .10*v*v;
  gl_FragColor = vec4(c,1.); }

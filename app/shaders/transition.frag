#version 460 core
#include <flutter/runtime_effect.glsl>

// Dissolve mask for the outgoing page snapshot.
//
// The shell keeps one snapshot layer under the incoming page so a branch switch
// can cross-fade instead of cutting. A plain FadeTransition over a full window
// reads as a grey wash; this mask removes the old page in a soft, slightly
// grainy wipe that follows the page lift, so the texture stays inside the
// existing two-layer budget (the shader only decides alpha, it never adds a
// third layer).
//
// Uniform layout (flattened float indices):
//   0,1 uSize      logical pixels of the masked area
//   2   uProgress  0 = keep the snapshot, 1 = fully revealed
//   3   uSoftness  half-width of the dissolve edge in normalized units

uniform vec2 uSize;
uniform float uProgress;
uniform float uSoftness;

out vec4 fragColor;

float hash(vec2 p) {
  return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void main() {
  vec2 size = max(uSize, vec2(1.0));
  vec2 frag = FlutterFragCoord().xy;
  vec2 uv = frag / size;

  // Two scales of noise keep the edge from looking like a regular checker when
  // the window is large, while staying static for the whole transition.
  float noise = hash(floor(frag * 0.5)) * 0.6 + hash(floor(frag * 0.17)) * 0.4;

  // Bias the removal towards the top: the incoming page lifts from below, so
  // the old page should leave first where the new page is already covering it.
  float bias = uv.y * 0.35;
  float value = clamp(mix(noise, bias, 0.45), 0.0, 1.0);

  float softness = max(uSoftness, 0.001);
  float alpha = smoothstep(uProgress - softness, uProgress + softness, value);

  // Premultiplied white: ShaderMask(BlendMode.dstIn) only reads alpha.
  fragColor = vec4(alpha, alpha, alpha, alpha);
}

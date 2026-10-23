#version 460 core
#include <flutter/runtime_effect.glsl>

// Pointer-following accent glow for interactive panels.
//
// InkWell already draws a flat hover tint, but it has no notion of where the
// pointer is. This shader paints one soft radial highlight whose falloff is an
// analytic exponential, so there is no BlurFilter/ImageFilter and no extra
// offscreen pass: the panel paints its surface, this quad, and its content.
//
// Uniform layout (flattened float indices):
//   0,1 uSize      logical pixels of the panel
//   2,3 uPointer   pointer position in panel-local logical pixels
//   4..7 uColor    straight RGBA of the accent tint
//   8   uIntensity 0..1 hover/press envelope
//   9   uRadius    glow radius in logical pixels

uniform vec2 uSize;
uniform vec2 uPointer;
uniform vec4 uColor;
uniform float uIntensity;
uniform float uRadius;

out vec4 fragColor;

void main() {
  vec2 size = max(uSize, vec2(1.0));
  vec2 frag = FlutterFragCoord().xy;

  float radius = max(uRadius, 1.0);
  vec2 delta = frag - uPointer;
  float falloff = exp(-2.4 * dot(delta, delta) / (radius * radius));

  // Fade out towards the panel border so the glow never looks pasted on.
  vec2 edge = min(frag, size - frag);
  float border = smoothstep(0.0, radius * 0.55, min(edge.x, edge.y));

  float alpha = falloff * border * clamp(uIntensity, 0.0, 1.0) * uColor.a;

  // Premultiplied output: the painter draws this over the panel surface.
  fragColor = vec4(uColor.rgb * alpha, alpha);
}

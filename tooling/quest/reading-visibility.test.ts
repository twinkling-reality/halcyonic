import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';

const workspace = new URL('../../apps/xr/Assets/Halcyonic/Workspace/', import.meta.url);
const source = (name: string) => readFileSync(new URL(name, workspace), 'utf8');

test('reading comparison runtime code has an explicit development compile boundary', () => {
  for (const name of ['ReadingVisibilityAssets.cs', 'ReadingVisibilityColumn.cs']) {
    const code = source(name);
    assert.match(code, /^#nullable enable\s+#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD/);
    assert.match(code, /#endif\s*$/);
    assert.doesNotMatch(
      code,
      /Shader\.Find|Resources\.Load|AsyncGPUReadback|ReadPixels|\.Submit\(/,
    );
  }
});

test('reading comparison shaders have explicit transient development scene ownership', () => {
  const builder = source('Editor/ReadingVisibilityInDevelopmentBuilds.cs');
  assert.match(builder, /IProcessSceneWithReport/);
  assert.match(builder, /report\.summary\.options & BuildOptions\.Development/);
  assert.match(builder, /if \(!development\) return/);
  assert.match(builder, /AssetDatabase\.LoadAssetAtPath<Shader>/);
  assert.doesNotMatch(builder, /SaveScene|SaveAssets|Shader\.Find|Resources\.Load/);
});

test('reading comparison references are independent opaque and documented multiplicative controls', () => {
  const opaque = source('Development/ReadingOpaque.shader');
  const mask = source('Development/ReadingMask.shader');
  assert.match(opaque, /Blend Off/);
  assert.match(opaque, /return float4\(_Color\.rgb,1\)/);
  assert.match(mask, /Blend Zero SrcAlpha/);
  for (const shader of [opaque, mask]) {
    assert.match(shader, /UNITY_VERTEX_OUTPUT_STEREO/);
    assert.doesNotMatch(shader, /_Camera|tex2D|GrabPass/);
  }
});

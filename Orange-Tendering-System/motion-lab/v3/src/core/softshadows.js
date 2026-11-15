import * as THREE from 'three';

// Contact-hardening soft shadows (PCSS) for the single studio key light.
//
// three r187 ships hard (Basic), 5-tap PCF and VSM shadows. None gives the look a paper miniature
// needs: a crisp contact line where a sheet touches the desk that opens into a wide, soft penumbra
// as a flap lifts. This replaces the Basic-shadow lookup (raw depth texture) with:
//   1. a blocker search (average occluder depth around the fragment),
//   2. a penumbra estimate proportional to receiver-occluder distance,
//   3. a Vogel-disk PCF with a receiver-plane depth bias so large kernels do not self-shadow.
// Must be installed before any material compiles, with renderer.shadowMap.type = BasicShadowMap.

export function installSoftShadows({
  frustumWidth, // world width of the shadow camera
  minRadius = 0.05, // world units: softness of a sheet lying flat (the "contact halo")
  maxRadius = 1.6, // world units: cap for very high occluders
  searchRadius = 1.0, // world units: blocker search disk
  blockerSamples = 16,
  pcfSamples = 28
} = {}) {
  const chunk = THREE.ShaderChunk.shadowmap_pars_fragment;
  // The Basic branch is the `#else` that follows the VSM branch and opens the third getShadow().
  // (Matched structurally: the published build strips the source comments.)
  const vsm = chunk.indexOf('SHADOWMAP_TYPE_VSM');
  const match = vsm < 0 ? null : /#else[^\n]*\n\s*float getShadow\( sampler2D shadowMap/.exec(chunk.slice(vsm));
  const elseAt = match ? vsm + match.index : -1;
  const start = elseAt < 0 ? -1 : chunk.indexOf('\n', elseAt);
  const tail = start < 0 ? -1 : chunk.indexOf('#if NUM_SUN_LIGHT_SHADOWS > 0', start);
  const close = tail < 0 ? -1 : chunk.lastIndexOf('#endif', tail);
  if (start < 0 || tail < 0 || close < 0) {
    console.warn('[softshadows] three.js shadow chunk layout changed; falling back to hard shadows.');
    return false;
  }

  const f = v => v.toFixed(6);
  const glsl = /* glsl */ `

		float pcssNoise( vec2 p ) {
			return fract( 52.9829189 * fract( dot( p, vec2( 0.06711056, 0.00583715 ) ) ) );
		}

		vec2 pcssDisk( int i, int n, float phi ) {
			float r = sqrt( ( float( i ) + 0.5 ) / float( n ) );
			float th = float( i ) * 2.399963229728653 + phi;
			return vec2( cos( th ), sin( th ) ) * r;
		}

		float getShadow( sampler2D shadowMap, vec2 shadowMapSize, float shadowIntensity, float shadowBias, float shadowRadius, vec4 shadowCoord ) {

			shadowCoord.xyz /= shadowCoord.w;

			bool inFrustum = shadowCoord.x >= 0.0 && shadowCoord.x <= 1.0 && shadowCoord.y >= 0.0 && shadowCoord.y <= 1.0;
			if ( ! ( inFrustum && shadowCoord.z <= 1.0 ) ) return 1.0;

			float z = shadowCoord.z + shadowBias;

			// Receiver-plane depth gradient: lets wide kernels follow the receiving surface.
			vec3 dx = dFdx( shadowCoord.xyz );
			vec3 dy = dFdy( shadowCoord.xyz );
			float det = dx.x * dy.y - dx.y * dy.x;
			vec2 grad = vec2( 0.0 );
			if ( abs( det ) > 1e-14 ) {
				grad = vec2( dy.y * dx.z - dx.y * dy.z, dx.x * dy.z - dy.x * dx.z ) / det;
			}
			grad = clamp( grad, vec2( -1.5 ), vec2( 1.5 ) );

			float phi = pcssNoise( gl_FragCoord.xy ) * 6.28318530718;

			const float SEARCH = ${f(searchRadius / frustumWidth)};
			const float RMIN = ${f(minRadius / frustumWidth)};
			const float RMAX = ${f(maxRadius / frustumWidth)};

			// Taps are snapped to texel centres and the reference depth is evaluated at that exact
			// point, so the nearest-filtered depth map adds no quantisation error. That is what lets
			// the bias stay far below the thickness of a single sheet of paper.
			float gap = 0.0;
			float count = 0.0;
			for ( int i = 0; i < ${blockerSamples}; i ++ ) {
				vec2 uv = ( floor( ( shadowCoord.xy + pcssDisk( i, ${blockerSamples}, phi ) * SEARCH ) * shadowMapSize ) + 0.5 ) / shadowMapSize;
				float d = textureLod( shadowMap, uv, 0.0 ).r;
				float zr = z + dot( grad, uv - shadowCoord.xy );
				if ( d < zr ) { gap += zr - d; count += 1.0; }
			}
			if ( count < 0.5 ) return 1.0;

			// shadowRadius carries (light size x depth range / frustum width): uv radius per unit depth.
			float penumbra = clamp( ( gap / count ) * shadowRadius, RMIN, RMAX );

			float lit = 0.0;
			for ( int i = 0; i < ${pcfSamples}; i ++ ) {
				vec2 uv = ( floor( ( shadowCoord.xy + pcssDisk( i, ${pcfSamples}, phi + 1.7 ) * penumbra ) * shadowMapSize ) + 0.5 ) / shadowMapSize;
				float d = textureLod( shadowMap, uv, 0.0 ).r;
				lit += step( z + dot( grad, uv - shadowCoord.xy ), d );
			}
			lit /= ${pcfSamples}.0;

			return mix( 1.0, lit, shadowIntensity );

		}

	`;

  THREE.ShaderChunk.shadowmap_pars_fragment = chunk.slice(0, start) + glsl + chunk.slice(close);
  return true;
}

/**
 * Soft highlight shoulder. Palette colours pass through untouched (linear below the knee), while
 * paper turned toward the key light rolls off instead of clipping to flat white.
 */
export function installSoftKnee(knee = 0.92) {
  const k = knee.toFixed(4);
  THREE.ShaderChunk.tonemapping_pars_fragment = THREE.ShaderChunk.tonemapping_pars_fragment.replace(
    'vec3 CustomToneMapping( vec3 color ) { return color; }',
    /* glsl */ `vec3 CustomToneMapping( vec3 color ) {
		color *= toneMappingExposure;
		float peak = max( color.r, max( color.g, color.b ) );
		if ( peak < ${k} ) return color;
		float d = 1.0 - ${k};
		float newPeak = 1.0 - d * d / ( peak + d - ${k} );
		return color * ( newPeak / peak );
	}`
  );
  return THREE.ShaderChunk.tonemapping_pars_fragment.includes('newPeak');
}

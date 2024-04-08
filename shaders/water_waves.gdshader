// Following a tutorial from catlike coding
// https://catlikecoding.com/unity/tutorials/flow/waves/

shader_type spatial;

#include "noise.gdshaderinc"
#include "fresnel.gdshaderinc"
#include "proximity_fade.gdshaderinc"

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;
uniform sampler2D depth_texture : source_color, hint_depth_texture;

uniform vec4 base_color : source_color = vec4(0.3, 0.5, 1.0, 0.5);

uniform float shimmer_start_height = 20.0;
uniform float shimmer_end_height = 30.0;
uniform float shimmer_scale = 300.0;
uniform float shimmer_crest_influence : hint_range(0.0, 1.0) = 0.75;

uniform vec3 fog_color : source_color = vec3(0.3, 0.5, 1.0);
uniform float fog_density = 0.15;

uniform vec2 wave1_direction = vec2(1.0, 0.0);
uniform float wave1_steepness : hint_range(0.0, 1.0) = 0.5;
uniform float wave1_wavelength = 10.0;

uniform vec2 wave2_direction = vec2(0.5, 0.5);
uniform float wave2_steepness : hint_range(0.0, 1.0) = 0.15;
uniform float wave2_wavelength = 5.0;

uniform vec2 wave3_direction = vec2(-0.7, 0.3);
uniform float wave3_steepness : hint_range(0.0, 1.0) = 0.5;
uniform float wave3_wavelength = 15.0;

uniform vec2 wave4_direction = vec2(-0.7, 0.3);
uniform float wave4_steepness : hint_range(0.0, 1.0) = 0.5;
uniform float wave4_wavelength = 15.0;

vec3 gerstnerWave
(
	vec3 gridPoint,
	
	vec2 direction,	
	float wavelength,
	float steepness,
	
	inout vec3 tangent,
	inout vec3 binormal
)
{
	float waveNumber = TAU / wavelength; // Wave number.
	float c = sqrt(9.8 / waveNumber);
	vec2 d = normalize(direction);
	float f = waveNumber * (dot(d, gridPoint.xz) - c * TIME);
	float a = steepness / waveNumber;
	
	tangent += vec3
	(
		1.0 - d.x * d.x * steepness * sin(f),
		d.x * steepness * cos(f),
		-d.x * d.x * steepness * sin(f)
	);
	
	binormal += vec3
	(
		-d.x * d.y * steepness * sin(f),
		d.y * steepness * cos(f),
		1.0 - d.y * d.y * steepness * sin(f)
	);
	
	return vec3
	(
		d.x * a * cos(f),
		a * sin(f),
		d.y * a * cos(f)
	);
}

vec3 colorBelowWater(float linear_depth, float fragment_depth, vec2 screen_uv, vec3 normal, vec3 view, mat4 inv_projection_matrix)
{
	float depth_difference = linear_depth - fragment_depth;
	depth_difference *= 0.05;
	
	vec3 background_color = texture(screen_texture, screen_uv).rgb;
	float fog_factor = exp2(-fog_density * depth_difference);
	return mix(fog_color, background_color, fog_factor * (1.0 - fresnel(10.0, normal, view)));
}

void vertex() {
	vec3 gridpoint = VERTEX;
	vec3 tangent = vec3(1.0, 0.0, 0.0);
	vec3 binormal = vec3(0.0, 0.0, 1.0);
	
	vec3 p = gridpoint;
	p += gerstnerWave(gridpoint, wave1_direction, wave1_wavelength, wave1_steepness, tangent, binormal);
	p += gerstnerWave(gridpoint, wave2_direction, wave2_wavelength, wave2_steepness, tangent, binormal);
	p += gerstnerWave(gridpoint, wave3_direction, wave3_wavelength, wave3_steepness, tangent, binormal);
	p += gerstnerWave(gridpoint, wave4_direction, wave4_wavelength, wave4_steepness, tangent, binormal);
	
	vec3 normal = normalize(cross(binormal, tangent));
	
	VERTEX = p;
	NORMAL = normal;
}

void fragment() {
	// Get linear depth from depth texture
	float depth = texture(depth_texture, SCREEN_UV).x;
	vec3 ndc = vec3(SCREEN_UV * 2.0 - 1.0, depth);
	vec4 depth_view = INV_PROJECTION_MATRIX * vec4(ndc, 1.0);
	depth_view.xyz /= depth_view.w;
	float linear_depth = -depth_view.z;
	
	float proximity = 1.0 - proximity_fade(5.0, -linear_depth, VERTEX, INV_PROJECTION_MATRIX);
	float close_proximity = pow(proximity, 16.0);
	float foam = abs(sin((proximity + TIME * 0.5) * 3.0)) * proximity + close_proximity;
	
	ALBEDO = mix(base_color.rgb, vec3(1.0, 1.0, 1.0), clamp(foam * 0.2, 0.0, 1.0));
	ALPHA = proximity_fade(1.0, -linear_depth, VERTEX, INV_PROJECTION_MATRIX);
	
	EMISSION = colorBelowWater(linear_depth, FRAGCOORD.z, SCREEN_UV, NORMAL, VIEW, INV_PROJECTION_MATRIX) * (1.0 - base_color.a);
	
	
	METALLIC = 0.9;
	
	float crest = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).y;
	crest = smoothstep(shimmer_start_height, shimmer_end_height, crest);
	
	vec2 uv_stretch = vec2
	(
		UV.x * 3.0,
		UV.y
	);
	float fbm = snoise(uv_stretch * shimmer_scale + TIME * 0.3) * snoise(uv_stretch * -shimmer_scale * 2.0 + TIME * 0.4);
	float shimmer = smoothstep(0.2, 0.8, fbm) * fresnel(1.0, NORMAL, VIEW) * max(1.0 - shimmer_crest_influence, crest);
	
	ROUGHNESS = shimmer;
	SPECULAR = shimmer * 5.0;
	RIM = 20.0;
}

shader_type spatial;

uniform sampler2D height_map;
uniform sampler2D normal_map;
uniform float sinkDistance = 500.0;

uniform sampler2D grass_albedo;
uniform sampler2D grass_normal;
uniform sampler2D grass_ao;
uniform float grass_scale;

uniform sampler2D cliff_albedo;
uniform sampler2D cliff_normal;
uniform sampler2D cliff_ao;
uniform float cliff_scale;
uniform float cliff_strength = 10.0;

vec3 normalBlend(vec3 normalA, vec3 normalB)
{
	return vec3
	(
		normalA.r < 0.5 ? (2.0 * normalA.r * normalB.r) : (1.0 - 2.0 * (1.0 - normalA.r) * (1.0 - normalB.r)),
		normalA.g < 0.5 ? (2.0 * normalA.g * normalB.g) : (1.0 - 2.0 * (1.0 - normalA.g) * (1.0 - normalB.g)),
		normalA.b < 0.5 ? (2.0 * normalA.b * normalB.b) : (1.0 - 2.0 * (1.0 - normalA.b) * (1.0 - normalB.b))
	);
}

void vertex() {
	
	if(sinkDistance > 0.0)
	{
		// Called for every vertex the material is visible on.
		vec3 worldPos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
		float camDist = distance(worldPos, CAMERA_POSITION_WORLD);
		float posOffset = min(camDist - sinkDistance, 0.0);
		VERTEX += vec3(0, posOffset, 0);
	}
}

void fragment() {
	vec2 grassUVs = UV * grass_scale;
	vec2 cliffUVs = UV * cliff_scale;
	
	vec3 normalmap = texture(normal_map, UV).rgb;
	vec3 normalmap_blend = texture(normal_map, UV, 5.0).rgb;
	
	float cliffAmount = clamp((cliff_strength - clamp(dot(normalmap_blend, vec3(0.0, 0.0, 1.0)), 0.0, 1.0) * cliff_strength), 0.0, 1.0);
	
	ALBEDO = mix(texture(grass_albedo, grassUVs).rgb, texture(cliff_albedo, cliffUVs).rgb, cliffAmount);
	AO = mix(texture(grass_ao, grassUVs).r, texture(cliff_ao, cliffUVs).r, cliffAmount);
	NORMAL_MAP = mix
	(
		normalBlend(normalmap, texture(grass_normal, grassUVs).rgb),
		normalBlend(normalmap, texture(cliff_normal, cliffUVs).rgb),
		cliffAmount
	);
	
}

//void light() {
	// Called for every pixel for every light affecting the material.
	// Uncomment to replace the default light processing function with this one.
//}

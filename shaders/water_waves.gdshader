// Following a tutorial from catlike coding
// https://catlikecoding.com/unity/tutorials/flow/waves/

shader_type spatial;

uniform vec3 base_color : source_color = vec3(0.3, 0.5, 1.0);

uniform vec2 wave1_direction = vec2(1.0, 0.0);
uniform float wave1_steepness : hint_range(0.0, 1.0) = 0.5;
uniform float wave1_wavelength = 10.0;

uniform vec2 wave2_direction = vec2(0.5, 0.5);
uniform float wave2_steepness : hint_range(0.0, 1.0) = 0.15;
uniform float wave2_wavelength = 5.0;

uniform vec2 wave3_direction = vec2(-0.7, 0.3);
uniform float wave3_steepness : hint_range(0.0, 1.0) = 0.5;
uniform float wave3_wavelength = 15.0;

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

vec3 colorBelowWater()
{
	return vec3(0);
}

void vertex() {
	vec3 gridpoint = VERTEX;
	vec3 tangent = vec3(1.0, 0.0, 0.0);
	vec3 binormal = vec3(0.0, 0.0, 1.0);
	
	vec3 p = gridpoint;
	p += gerstnerWave(gridpoint, wave1_direction, wave1_wavelength, wave1_steepness, tangent, binormal);
	p += gerstnerWave(gridpoint, wave2_direction, wave2_wavelength, wave2_steepness, tangent, binormal);
	p += gerstnerWave(gridpoint, wave3_direction, wave3_wavelength, wave3_steepness, tangent, binormal);
	
	vec3 normal = normalize(cross(binormal, tangent));
	
	VERTEX = p;
	NORMAL = normal;
}

void fragment() {
	// Called for every pixel the material is visible on.
	ALBEDO = colorBelowWater();
	ALPHA = 1.0;
	METALLIC = 0.5;
	ROUGHNESS = 0.2;
}

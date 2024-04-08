float proximity_fade(float fade_distance, float linear_depth, vec3 vertex, mat4 inv_projection_matrix)
{
	return clamp(1.0 - smoothstep(linear_depth + fade_distance, linear_depth, vertex.z), 0.0, 1.0);
}
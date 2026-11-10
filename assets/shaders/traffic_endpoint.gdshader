shader_type canvas_item;
render_mode unshaded;

uniform vec2 source_opening;
uniform vec2 source_direction;
uniform vec2 consumer_opening;
uniform vec2 consumer_direction;
uniform bool clip_source = true;
uniform bool clip_consumer = false;
varying vec2 world_position;

void vertex()
{
    world_position = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
}

void fragment()
{
    if (clip_source && dot(world_position - source_opening, source_direction) < 0.0)
    {
        discard;
    }
    if (clip_consumer && dot(world_position - consumer_opening, consumer_direction) < 0.0)
    {
        discard;
    }
}

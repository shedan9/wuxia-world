// 战斗形象的选中描边（M1 战斗原型）：沿人物贴图的透明边缘外扩一圈纯色轮廓，替代固定方框。
// 在贴图透明处取周围 16 个方向的最大不透明度，超过阈值即画轮廓色；width_texels 由调用方按“屏幕像素 ÷ 贴图缩放”换算，
// 使描边在屏幕上粗细一致。没有贴图的绘制（地影、剪影占位）采样到的是白色默认纹理，alpha 恒为 1，不受影响。
shader_type canvas_item;

uniform bool enabled = false;
uniform vec4 outline_color : source_color = vec4(0.85, 0.74, 0.47, 1.0);
uniform float width_texels = 6.0;

void fragment() {
	vec4 tex = texture(TEXTURE, UV);
	vec4 col = tex * COLOR;
	if (enabled && tex.a < 0.5) {
		float a = 0.0;
		vec2 step_uv = TEXTURE_PIXEL_SIZE * width_texels;
		for (int i = 0; i < 16; i++) {
			float ang = float(i) * 0.3926991;
			vec2 dir = vec2(cos(ang), sin(ang));
			a = max(a, texture(TEXTURE, UV + dir * step_uv).a);
			a = max(a, texture(TEXTURE, UV + dir * step_uv * 0.5).a);
		}
		if (a > 0.5) {
			col = mix(col, vec4(outline_color.rgb, 1.0), outline_color.a);
		}
	}
	COLOR = col;
}

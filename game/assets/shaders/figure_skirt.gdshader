// 探索人物的裙摆摆动（M3-02）：长裙盖住腿，AI 行走帧画不出迈步，改由着色器让裙摆随步相前后摆。
// 只对贴图下半段起作用：自 top_y（约膝上）往下按 t² 递增横移，到裙摆 hem_y 处横移 sway 个贴图像素，腰身以上不动；
// 另叠一道沿高度传播的小波纹，读成布料的余荡。keep_x 右侧（长篙等竖直道具所在列）不横移，免得篙杆被拗弯。
// 调用方把贴图按 pad 外扩画出（UV 会超出 0–1），超出部分取透明，横移出原外框的裙摆不被裁掉。
// 同一画布项里的地影等无贴图绘制采样的是白色默认纹理、UV 恒在 0 处，不受影响。
shader_type canvas_item;

uniform vec2 tex_size = vec2(1.0);
uniform float top_y = 0.0;
uniform float hem_y = 1.0;
uniform float sway = 0.0;
uniform float ripple = 0.0;
uniform float ripple_phase = 0.0;
uniform float keep_x = 1e9;

void fragment() {
	vec2 px = UV * tex_size;
	float t = clamp((px.y - top_y) / max(hem_y - top_y, 1.0), 0.0, 1.0);
	float off = sway * t * t + ripple * t * sin(ripple_phase - t * 4.0);
	vec2 src = px;
	if (px.x < keep_x) {
		src.x -= off;
		// 横移后取样落到篙杆列：改取篙杆左侧的镜像布面，不把篙杆复制出一道虚影。
		if (src.x >= keep_x) {
			src.x = 2.0 * keep_x - src.x - 1.0;
		}
	}
	vec2 uv = src / tex_size;
	vec4 tex = (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0) ? vec4(0.0) : texture(TEXTURE, uv);
	COLOR = tex * COLOR;
}

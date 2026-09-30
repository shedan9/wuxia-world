// 侧视战斗的近景地面（M0-04，旧渡码头石板），与远景整张 AI 布景（BattleBackdrop）拼成一幅：
// 远景只画到远岸水线，水线以下的码头地面由本着色器按透视铺 AI 石板纹理（town.ground.flagstone，与城镇探索同一张）。
// 透视：镜头高 cam_h、焦距 focal（像素），地面上屏幕行 y 对应深度 focal·cam_h / (y − horizon)，
// 横向世界坐标 (x − center_x)·cam_h / (y − horizon)；horizon 取得比画上的远岸高，镜头显得更高、地面压缩更缓，
// 两军站位（不随远近缩放）不显得浮在地上。远端一道条石压边（curb），边沿一线映着天光，压边下一道阴影；
// 由远到近从琥珀天光的暖灰过渡到偏黛的冷灰，落日方向一道湿石板暖色反光，底部两角压暗。坐标单位为设计像素（1920×1080 画布）。
shader_type canvas_item;

uniform sampler2D ground_tex : repeat_enable, filter_linear_mipmap;
uniform float tex_world = 480.0;
uniform float tex_desat = 0.25;
uniform float tex_contrast = 0.8;
uniform vec2 rect_origin = vec2(0.0, 505.0);
uniform vec2 rect_size = vec2(1920.0, 575.0);
uniform float center_x = 960.0;
uniform float horizon = 200.0;
uniform float cam_h = 600.0;
uniform float focal = 2065.0;
uniform float curb = 22.0;
uniform float curb_stone = 150.0;
uniform vec3 far_tint = vec3(0.98, 0.86, 0.80);
uniform vec3 near_tint = vec3(0.56, 0.62, 0.74);
uniform vec3 sky_glow = vec3(0.96, 0.72, 0.50);
uniform float sun_x = 760.0;

float hash(vec2 p) {
	p = fract(p * vec2(0.1031, 0.1030));
	p += dot(p, p.yx + 33.33);
	return fract((p.x + p.y) * p.x);
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x),
		mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

vec3 stone(vec2 w) {
	vec3 c = texture(ground_tex, w / tex_world).rgb;
	c = mix(textureLod(ground_tex, vec2(0.5), 12.0).rgb, c, tex_contrast);
	return mix(c, vec3(dot(c, vec3(0.299, 0.587, 0.114))), tex_desat);
}

void fragment() {
	vec2 p = rect_origin + UV * rect_size;
	float d = max(p.y - horizon, 1.0);
	vec2 w = vec2((p.x - center_x) * cam_h / d, focal * cam_h / d);
	float local = p.y - rect_origin.y;
	float t = clamp(local / rect_size.y, 0.0, 1.0);

	vec3 c = stone(w);
	// 大尺度明暗：打破纹理循环。
	c *= 0.92 + 0.16 * vnoise(w * 0.0021 + 7.0);

	// 远端压边条石：更大的块、顶面受天光略亮，块间竖缝。
	if (local < curb) {
		float u = w.x / curb_stone + hash(vec2(floor(w.x / curb_stone), 3.0)) * 0.3;
		// 压边取纹理平均色加细碎明暗（直接采石板纹理会把灰缝拉成亮条）。
		vec3 mean = textureLod(ground_tex, vec2(0.5), 12.0).rgb;
		mean = mix(mean, vec3(dot(mean, vec3(0.299, 0.587, 0.114))), tex_desat);
		vec3 top = mean * (0.9 + 0.2 * vnoise(vec2(w.x * 0.03, local * 0.4))) * (1.05 - 0.2 * local / curb);
		float joint = 1.0 - smoothstep(0.0, 0.035, 0.5 - abs(fract(u) - 0.5));
		c = mix(top, top * 0.55, joint);
		// 边沿一线映天光。
		c = mix(c, sky_glow * 1.05, (1.0 - smoothstep(0.0, 3.0, local)) * 0.55);
	}

	// 压边下的阴影与近边的暗湿带。
	float below = local - curb;
	float shade = below < 0.0 ? 1.0 : 1.0 - 0.45 * exp(-below / 5.0) - 0.12 * exp(-below / 40.0);
	c *= shade;

	// 由远到近：琥珀暖灰 → 偏黛冷灰；远处叠一层天光反照。
	c *= mix(far_tint, near_tint, pow(t, 0.6));
	c += sky_glow * 0.10 * pow(1.0 - t, 3.0);
	// 落日在两军之间偏左：湿石板上一道顺光的暖色反光，自远端向镜头拉长、渐淡。
	float streak = exp(-pow((p.x - sun_x) / (140.0 + local * 0.9), 2.0)) * exp(-local / 260.0);
	c += sky_glow * 0.22 * streak * (0.6 + 0.4 * vnoise(w * 0.01));

	// 底部两角压暗，视线收向两军之间。
	vec2 q = (p - vec2(center_x, rect_origin.y + rect_size.y * 0.35)) / vec2(1250.0, 700.0);
	c *= 1.0 - 0.35 * smoothstep(0.35, 1.1, length(q));

	COLOR = vec4(c, 1.0);
}

// 江湖大地图的地形占位（M0），画风与立绘一致：半写实赛璐璐。
// 读取 CPU 生成的低分辨率高度图（三次 B 样条插值），保留山体起伏与褶皱，但明暗分成硬边的两到三阶，
// 颜色按海拔分成清楚的青绿色带，海岸、湖岸、色带交界与明暗交界处勾线，林地铺赛璐璐小树冠，
// 水面为两阶湖蓝加白色浪线。正式地图完成后替换。
shader_type canvas_item;

// 32 位浮点纹理在兼容渲染器上不一定支持线性过滤，因此取整点值、在着色器里插值，坡度连续不出网格。
uniform sampler2D height_map : filter_nearest, repeat_disable;
uniform vec2 map_size = vec2(2880.0, 1600.0);
// 高度到坡度的换算：越大山体明暗越强。
uniform float relief = 160.0;

const vec3 LINE = vec3(0.16, 0.24, 0.26);

float hash(vec2 p) {
	p = fract(p * vec2(0.1031, 0.1030));
	p += dot(p, p.yx + 33.33);
	return fract((p.x + p.y) * p.x);
}

vec2 hash2(vec2 p) {
	return vec2(hash(p), hash(p + vec2(19.19, 7.31)));
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x),
		mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

float fbm(vec2 p) {
	float s = 0.0;
	float a = 0.5;
	for (int i = 0; i < 4; i++) {
		s += a * vnoise(p);
		p = p * 2.03 + vec2(17.1, 9.2);
		a *= 0.5;
	}
	return s / 0.9375;
}

float ridge(vec2 p) {
	float s = 0.0;
	float a = 0.5;
	for (int i = 0; i < 3; i++) {
		float n = 1.0 - abs(vnoise(p) * 2.0 - 1.0);
		s += a * n * n;
		p = p * 2.1 + vec2(3.3, 7.7);
		a *= 0.5;
	}
	return s / 0.875;
}

vec4 bspline(float t) {
	float t2 = t * t;
	float t3 = t2 * t;
	return vec4(
		(1.0 - 3.0 * t + 3.0 * t2 - t3) / 6.0,
		(4.0 - 6.0 * t2 + 3.0 * t3) / 6.0,
		(1.0 + 3.0 * t + 3.0 * t2 - 3.0 * t3) / 6.0,
		t3 / 6.0);
}

float base_height(vec2 p) {
	ivec2 size = textureSize(height_map, 0);
	vec2 texel = p / map_size * vec2(size) - 0.5;
	vec2 i = floor(texel);
	vec2 f = texel - i;
	vec4 wx = bspline(f.x);
	vec4 wy = bspline(f.y);
	float sum = 0.0;
	for (int y = 0; y < 4; y++) {
		float row = 0.0;
		for (int x = 0; x < 4; x++) {
			ivec2 c = clamp(ivec2(i) + ivec2(x - 1, y - 1), ivec2(0), size - 1);
			row += texelFetch(height_map, c, 0).r * wx[x];
		}
		sum += row * wy[y];
	}
	return sum;
}

// 细节只加中等尺度的山褶，不加碎石纹理，免得像卫星图。
float height(vec2 p) {
	float b = base_height(p);
	float land = smoothstep(0.0, 0.03, b);
	float rocky = smoothstep(0.14, 0.5, b);
	return b + (ridge(p / 90.0) - 0.45) * 0.2 * rocky * land;
}

// 等值线：f 穿过 level 处画一条约 width 像素宽的线，靠屏幕导数抗锯齿。
float contour(float f, float level, float width) {
	float d = abs(f - level) / max(fwidth(f), 1e-5);
	return 1.0 - smoothstep(width * 0.5, width * 0.5 + 1.0, d);
}

void fragment() {
	vec2 p = UV * map_size;
	float e = 3.0;
	float h = height(p);
	float hx = height(p + vec2(e, 0.0));
	float hy = height(p + vec2(0.0, e));
	vec3 n = normalize(vec3(-(hx - h) / e * relief, -(hy - h) / e * relief, 1.0));
	vec3 light = normalize(vec3(-0.62, -0.7, 0.6));
	float shade = dot(n, light);

	// ── 陆地：按海拔分色带，交界处略带过渡 ──
	float wob = (fbm(p / 110.0) - 0.5) * 0.08;
	float band = h + wob;
	vec3 plain = mix(vec3(0.66, 0.80, 0.56), vec3(0.76, 0.81, 0.58), fbm(p / 300.0));
	// 江南平原的田块：大块、颜色差小，只作画面细节。
	vec2 cell = floor((p + vec2(fbm(p / 90.0) * 40.0)) / vec2(46.0, 30.0));
	float flat_low = 1.0 - smoothstep(0.05, 0.1, band);
	plain = mix(plain, mix(vec3(0.60, 0.77, 0.52), vec3(0.80, 0.82, 0.60), hash(cell)), 0.35 * flat_low);
	vec3 forest = vec3(0.52, 0.73, 0.52);
	vec3 hill = vec3(0.50, 0.71, 0.56);
	vec3 peak = vec3(0.42, 0.62, 0.72);
	vec3 cap = vec3(0.90, 0.94, 0.95);

	vec3 col = plain;
	col = mix(col, forest, smoothstep(0.2, 0.22, band));
	col = mix(col, hill, smoothstep(0.3, 0.32, band));
	// 山脉由平视山峦画在上面，地面不再分高山与积雪色带。
	// 海边沙色一窄条。
	col = mix(col, vec3(0.90, 0.86, 0.68), 1.0 - smoothstep(0.012, 0.018, h));

	// 树冠：林地色带里按格子撒圆冠，受光面亮、背光面暗，底边一笔暗线。
	// 成片的树林与林间空地由低频噪声决定，平原上零星几株。
	vec2 tc = floor(p / 22.0);
	vec2 center = (tc + 0.5 + (hash2(tc) - 0.5) * 0.3) * 22.0;
	float r = 6.5 + 2.5 * hash(tc + 3.1);
	float grove = smoothstep(0.42, 0.62, fbm(center / 170.0 + vec2(5.0, 2.0)));
	float dens = smoothstep(0.1, 0.2, band) * (1.0 - smoothstep(0.42, 0.5, band)) * grove * 0.85
		+ 0.05 * (1.0 - smoothstep(0.05, 0.1, band));
	if (hash(tc + 9.7) < dens) {
		vec2 d = p - center;
		float dist = length(d);
		float inside = 1.0 - smoothstep(r - 0.8, r + 0.2, dist);
		float lit_side = step(0.0, -(d.x + d.y) - r * 0.2);
		vec3 crown = mix(forest * 0.78, forest * 1.12, lit_side);
		col = mix(col, crown, inside);
		col = mix(col, LINE, (1.0 - smoothstep(0.6, 1.4, abs(dist - r))) * step(0.0, d.y) * 0.5);
	}

	// 赛璐璐明暗：受光、背光两阶硬边，深背光再压一阶；明暗交界带一道淡线。
	float lit = smoothstep(0.34, 0.38, shade);
	float deep = smoothstep(0.1, 0.06, shade);
	// 地面只做轻微的起伏明暗，山的体积交给平视山峦。
	vec3 shadow = col * vec3(0.84, 0.89, 0.96);
	col = mix(shadow, col, lit);
	col = mix(col, col * vec3(0.9, 0.93, 0.97), deep * 0.5);
	// 陡坡迎光的山脊亮边。
	col += vec3(0.1, 0.1, 0.08) * smoothstep(0.82, 0.88, shade) * smoothstep(0.22, 0.4, h);
	col = mix(col, LINE, contour(shade, 0.36, 1.2) * smoothstep(0.16, 0.3, h) * 0.45);
	// 色带交界勾细线。
	col = mix(col, LINE, contour(band, 0.31, 1.0) * 0.25);
	col = mix(col, LINE, contour(band, 0.51, 1.0) * 0.3);

	// ── 水：两阶湖蓝，近岸浅、远海深，白色短浪线 ──
	float hw = h;
	float depth = -hw;
	vec3 water = mix(vec3(0.46, 0.76, 0.80), vec3(0.24, 0.52, 0.66), smoothstep(0.07, 0.09, depth));
	water = mix(water, vec3(0.17, 0.40, 0.56), smoothstep(0.3, 0.32, depth));
	vec2 wc = floor(p / vec2(64.0, 30.0));
	vec2 wp = p / vec2(64.0, 30.0) - wc;
	float wave = step(0.55, hash(wc)) * (1.0 - smoothstep(0.04, 0.09, abs(wp.y - 0.5 - 0.35 * (wp.x - 0.5) * (wp.x - 0.5) * -1.6)))
		* step(0.2, wp.x) * step(wp.x, 0.8);
	water = mix(water, vec3(0.92, 0.97, 0.97), wave * 0.55 * step(0.04, depth));
	// 近岸白浪一圈。
	water = mix(water, vec3(0.92, 0.97, 0.96), contour(hw, -0.012, 3.0) * 0.8);
	col = mix(water, col, smoothstep(-0.0015, 0.0015, hw));
	// 海岸与湖岸线。
	col = mix(col, LINE, contour(hw, 0.0, 1.8) * 0.85);

	// 图边压暗，像一幅铺开的地图。
	vec2 edge = min(UV, 1.0 - UV) * map_size;
	col *= 0.85 + 0.15 * smoothstep(0.0, 90.0, min(edge.x, edge.y));

	COLOR = vec4(col, 1.0);
}

// 江湖大地图的地面（2026-10-25 按中国地理重做），画风与立绘一致：半写实赛璐璐。
// 读取离线烘焙的高度图（WorldTerrainBake，三次 B 样条插值）与三张地貌权重图：
// 明暗分成硬边的两到三阶，平原按地貌着色并铺各自的纹样——大漠沙丘、戈壁碎石、草原风痕、高原与雪域、
// 林海针叶、水田田畦、麦田条垄、黄土梯坎、南方密林、沼泽水洼；未划地貌处为青绿平原与零星林地。
// 水面为两阶湖蓝加白色浪线。缩小看全图时细纹按屏幕像素淡出，免得闪烁。山峦、江河、城池等由画布画在上面。
shader_type canvas_item;

// 32 位浮点纹理在兼容渲染器上不一定支持线性过滤，因此取整点值、在着色器里插值，坡度连续不出网格。
uniform sampler2D height_map : filter_nearest, repeat_disable;
uniform sampler2D biome0 : filter_linear, repeat_disable;
uniform sampler2D biome1 : filter_linear, repeat_disable;
uniform sampler2D biome2 : filter_linear, repeat_disable;
uniform vec2 map_size = vec2(8400.0, 5600.0);
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

// 格子里撒一棵树冠：返回 (覆盖, 受光侧, 底边暗线)。cell 为格子大小，dens 为出现几率，conifer 为针叶（尖冠）。
vec3 crown(vec2 p, float cell, float dens, float conifer, float seed) {
	vec2 tc = floor(p / cell);
	vec2 center = (tc + 0.5 + (hash2(tc + seed) - 0.5) * 0.4) * cell;
	float r = cell * (0.28 + 0.12 * hash(tc + 3.1 + seed));
	if (hash(tc + 9.7 + seed) >= dens) {
		return vec3(0.0);
	}
	vec2 d = p - center;
	// 针叶：上尖下宽的冠形。
	float squash = mix(1.0, 0.75 + 0.6 * clamp((d.y + r) / (2.0 * r), 0.0, 1.0), conifer);
	float dist = length(vec2(d.x / squash, d.y));
	float inside = 1.0 - smoothstep(r - 0.8, r + 0.2, dist);
	float lit_side = step(0.0, -(d.x + d.y) - r * 0.2);
	float rim = (1.0 - smoothstep(0.6, 1.4, abs(dist - r))) * step(0.0, d.y);
	return vec3(inside, lit_side, rim);
}

// 田块：把平面按带噪声的格子切开，返回 (格子随机值, 格内 x, 格内 y, 田埂)。
vec4 fields(vec2 p, vec2 size, float warp, float detail) {
	vec2 q = p + vec2(fbm(p / 160.0), fbm(p / 160.0 + 7.0)) * warp;
	vec2 cell = floor(q / size);
	vec2 f = q / size - cell;
	vec2 edge = min(f, 1.0 - f) * size;
	float bund = (1.0 - smoothstep(0.6, 1.6, min(edge.x, edge.y))) * detail;
	return vec4(hash(cell), f, bund);
}

void fragment() {
	vec2 p = UV * map_size;
	// 每屏幕像素对应的地图像素：缩小看全图时大于 1，细纹据此淡出。
	float px = max(fwidth(p.x), 1e-4);
	float detail = 1.0 - smoothstep(1.4, 3.2, px);
	float fine = 1.0 - smoothstep(0.8, 1.8, px);

	float e = 3.0;
	float h = height(p);
	float hx = height(p + vec2(e, 0.0));
	float hy = height(p + vec2(0.0, e));
	vec3 n = normalize(vec3(-(hx - h) / e * relief, -(hy - h) / e * relief, 1.0));
	vec3 light = normalize(vec3(-0.62, -0.7, 0.6));
	float shade = dot(n, light);

	vec4 b0 = texture(biome0, UV);
	vec4 b1 = texture(biome1, UV);
	vec4 b2 = texture(biome2, UV);
	// 权重边缘再加一层细噪声，交界不呈烘焙格子的软糊。
	float jitter = (fbm(p / 40.0) - 0.5) * 0.5;
	b0 = clamp((b0 - 0.5 + jitter * 0.4) * 1.6 + 0.5, 0.0, 1.0);
	b1 = clamp((b1 - 0.5 + jitter * 0.4) * 1.6 + 0.5, 0.0, 1.0);
	b2 = clamp((b2 - 0.5 + jitter * 0.4) * 1.6 + 0.5, 0.0, 1.0);
	float desert = b0.r, gobi = b0.g, grass = b0.b, plateau = b0.a;
	float forest = b1.r, paddy = b1.g, wheat = b1.b, loess = b1.a;
	float jungle = b2.r, marsh = b2.g, snow = b2.b, steppe = b2.a;

	// ── 青绿平原（未划地貌处）：海拔色带与零星林地 ──
	float wob = (fbm(p / 110.0) - 0.5) * 0.08;
	float band = h + wob;
	vec3 plain = mix(vec3(0.66, 0.80, 0.56), vec3(0.76, 0.81, 0.58), fbm(p / 300.0));
	vec3 grove_tone = vec3(0.52, 0.73, 0.52);
	vec3 hill = vec3(0.50, 0.71, 0.56);
	vec3 col = plain;
	col = mix(col, grove_tone, smoothstep(0.2, 0.22, band));
	col = mix(col, hill, smoothstep(0.3, 0.32, band));

	float grove = smoothstep(0.42, 0.62, fbm(p / 170.0 + vec2(5.0, 2.0)));
	float open_land = 1.0 - clamp(desert + gobi + grass + plateau + snow + paddy + wheat + loess + marsh + steppe, 0.0, 1.0);
	float plain_trees = (smoothstep(0.1, 0.2, band) * (1.0 - smoothstep(0.42, 0.5, band)) * grove * 0.85
		+ 0.06 * (1.0 - smoothstep(0.05, 0.1, band))) * open_land;

	// ── 荒原：西北的干草地，土黄带灰绿，零星矮灌 ──
	if (steppe > 0.0) {
		vec3 g = mix(vec3(0.80, 0.78, 0.58), vec3(0.72, 0.76, 0.56), smoothstep(0.35, 0.65, fbm(p / 260.0)));
		vec3 c = crown(p, 14.0, 0.12, 0.0, 41.0);
		g = mix(g, vec3(0.56, 0.62, 0.44), c.x * fine * 0.8);
		col = mix(col, g, steppe);
	}

	// ── 大漠：沙色底上细密的沙纹，再错落撒一弯弯新月形沙丘（迎风坡亮、落沙坡暗） ──
	if (desert > 0.0) {
		vec3 sand = mix(vec3(0.91, 0.84, 0.64), vec3(0.88, 0.79, 0.58), fbm(p / 400.0));
		vec3 g = sand;
		float ripple = fract((p.x * 0.8 + p.y * 0.3 + fbm(p / 120.0) * 60.0) / 9.0);
		g = mix(g, sand * 0.95, smoothstep(0.75, 0.9, ripple) * fine * 0.6);
		vec2 cell = vec2(70.0, 42.0);
		vec2 cid = floor(p / cell);
		vec2 f = p / cell - cid;
		vec2 ctr = vec2(0.5) + (hash2(cid + 4.0) - 0.5) * 0.4;
		float r = 0.26 + 0.12 * hash(cid + 8.0);
		vec2 d = (f - ctr) * vec2(1.0, cell.y / cell.x) ;
		float ring = length(d * vec2(1.0, 1.7));
		float body = (1.0 - smoothstep(r - 0.02, r, ring)) * smoothstep(-0.05, 0.12, d.y + r * 0.35);
		float hole = 1.0 - smoothstep(r * 0.55 - 0.02, r * 0.55, length((d - vec2(0.0, r * 0.45)) * vec2(1.0, 1.7)));
		float dune = body * (1.0 - hole) * step(0.25, hash(cid + 1.0));
		float lit_side = step(0.0, -d.x);
		g = mix(g, mix(sand * vec3(0.84, 0.79, 0.74), sand * 1.06, lit_side), dune * detail);
		col = mix(col, g, desert);
	}

	// ── 戈壁：灰褐砾石地，散落的圆砾点 ──
	if (gobi > 0.0) {
		vec3 g = mix(vec3(0.78, 0.74, 0.60), vec3(0.72, 0.70, 0.60), fbm(p / 180.0));
		vec3 c = crown(p, 9.0, 0.35, 0.0, 33.0);
		g = mix(g, g * 0.84, c.x * fine);
		col = mix(col, g, gobi);
	}

	// ── 草原：明黄绿，顺风的长条风痕 ──
	if (grass > 0.0) {
		vec3 g = mix(vec3(0.70, 0.83, 0.47), vec3(0.78, 0.84, 0.50), fbm(p / 260.0));
		float streak = fbm(vec2(p.x / 140.0, p.y / 18.0));
		g = mix(g, g * vec3(0.9, 0.95, 0.88), smoothstep(0.55, 0.7, streak) * detail);
		g = mix(g, g * 1.06, smoothstep(0.35, 0.2, streak) * detail);
		col = mix(col, g, grass);
	}

	// ── 高原：灰绿与赭石相间的高寒草甸 ──
	if (plateau > 0.0) {
		vec3 g = mix(vec3(0.70, 0.74, 0.58), vec3(0.78, 0.74, 0.60), smoothstep(0.4, 0.6, fbm(p / 220.0)));
		g = mix(g, vec3(0.66, 0.72, 0.62), smoothstep(0.6, 0.75, fbm(p / 90.0 + 4.0)) * 0.5);
		col = mix(col, g, plateau);
	}

	// ── 黄土：浅赭黄台地，沿等高的梯坎细线与冲沟 ──
	if (loess > 0.0) {
		vec3 g = mix(vec3(0.87, 0.79, 0.6), vec3(0.83, 0.75, 0.57), fbm(p / 200.0));
		g = mix(g, vec3(0.74, 0.78, 0.56), smoothstep(0.55, 0.7, fbm(p / 150.0 + 6.0)) * 0.45);
		float terr = fbm(p / 70.0) * 9.0;
		g = mix(g, g * 0.92, (1.0 - smoothstep(0.0, 0.12 * max(px, 1.0), abs(fract(terr) - 0.5) - 0.42)) * detail * 0.6);
		g = mix(g, g * 0.88, smoothstep(0.74, 0.8, ridge(p / 120.0)) * 0.5);
		col = mix(col, g, loess);
	}

	// ── 麦田：麦黄、青苗与休耕地相间的长方田块，田内条垄 ──
	if (wheat > 0.0) {
		vec4 f = fields(p, vec2(52.0, 30.0), 30.0, detail);
		vec3 g = mix(vec3(0.84, 0.81, 0.58), vec3(0.73, 0.8, 0.52), smoothstep(0.35, 0.65, f.x));
		g = mix(g, vec3(0.8, 0.76, 0.6), step(0.88, f.x));
		g = mix(g, g * 0.94, step(0.5, fract(f.z * 5.0)) * fine * 0.5);
		g = mix(g, vec3(0.9, 0.88, 0.72), f.w * 0.5);
		col = mix(col, mix(vec3(0.78, 0.8, 0.56), g, detail * 0.8 + 0.2), wheat);
	}

	// ── 水田：小块田畦，部分灌水泛青光，田埂浅色 ──
	if (paddy > 0.0) {
		vec4 f = fields(p, vec2(32.0, 22.0), 26.0, detail);
		vec3 g = mix(vec3(0.63, 0.8, 0.53), vec3(0.71, 0.84, 0.57), f.x);
		float flooded = step(0.78, f.x);
		g = mix(g, mix(vec3(0.62, 0.8, 0.74), vec3(0.74, 0.87, 0.82), f.z), flooded);
		g = mix(g, vec3(0.84, 0.88, 0.68), f.w * 0.4);
		col = mix(col, mix(vec3(0.66, 0.82, 0.56), g, detail * 0.8 + 0.2), paddy);
	}

	// ── 沼泽：湿绿底上的小水洼与一簇簇芦苇 ──
	if (marsh > 0.0) {
		vec3 g = vec3(0.58, 0.74, 0.58);
		float pool = smoothstep(0.6, 0.63, fbm(p / 38.0 + 2.0));
		g = mix(g, vec3(0.52, 0.74, 0.78), pool);
		vec3 reed = crown(p, 9.0, 0.35, 1.0, 21.0);
		g = mix(g, vec3(0.48, 0.62, 0.42), reed.x * (1.0 - pool) * fine);
		col = mix(col, g, marsh);
	}

	// ── 雪域：白中带蓝的雪原 ──
	if (snow > 0.0) {
		vec3 g = mix(vec3(0.93, 0.95, 0.97), vec3(0.84, 0.89, 0.94), smoothstep(0.45, 0.7, fbm(p / 120.0)));
		col = mix(col, g, snow);
	}

	// 海边沙色一窄条。
	col = mix(col, vec3(0.90, 0.86, 0.68), 1.0 - smoothstep(0.012, 0.018, h));

	// ── 树冠：平原零星林地（圆冠）、林海（针叶，密）、南方密林（大圆冠，密） ──
	if (plain_trees > 0.01) {
		vec3 c = crown(p, 22.0, plain_trees, 0.0, 0.0);
		vec3 crown_col = mix(grove_tone * 0.78, grove_tone * 1.12, c.y);
		col = mix(col, crown_col, c.x * detail);
		col = mix(col, LINE, c.z * 0.5 * c.x * detail);
	}
	if (forest > 0.01) {
		vec3 base = mix(vec3(0.38, 0.58, 0.44), vec3(0.34, 0.54, 0.46), fbm(p / 200.0));
		col = mix(col, base * 0.92, forest * (1.0 - detail * 0.4));
		vec3 c = crown(p, 16.0, 0.92 * forest, 1.0, 5.0);
		col = mix(col, mix(base * 0.8, base * 1.15, c.y), c.x * detail * forest);
		col = mix(col, LINE, c.z * 0.45 * c.x * detail * forest);
	}
	if (jungle > 0.01) {
		vec3 base = mix(vec3(0.44, 0.68, 0.46), vec3(0.50, 0.72, 0.44), fbm(p / 180.0));
		col = mix(col, base * 0.9, jungle * (1.0 - detail * 0.4));
		vec3 c = crown(p, 24.0, 0.9 * jungle, 0.0, 11.0);
		col = mix(col, mix(base * 0.82, base * 1.18, c.y), c.x * detail * jungle);
		col = mix(col, LINE, c.z * 0.4 * c.x * detail * jungle);
	}

	// 赛璐璐明暗：受光、背光两阶硬边，深背光再压一阶；明暗交界带一道淡线。
	float lit = smoothstep(0.34, 0.38, shade);
	float deep = smoothstep(0.1, 0.06, shade);
	// 地面只做轻微的起伏明暗，山的体积交给平视山峦。
	vec3 shadow = col * vec3(0.89, 0.92, 0.97);
	col = mix(shadow, col, lit);
	col = mix(col, col * vec3(0.92, 0.94, 0.98), deep * 0.35);
	col += vec3(0.1, 0.1, 0.08) * smoothstep(0.82, 0.88, shade) * smoothstep(0.22, 0.4, h);
	col = mix(col, LINE, contour(shade, 0.36, 1.2) * smoothstep(0.16, 0.3, h) * 0.45 * detail);
	col = mix(col, LINE, contour(band, 0.31, 1.0) * 0.25 * open_land * detail);
	col = mix(col, LINE, contour(band, 0.51, 1.0) * 0.3 * open_land * detail);

	// ── 水：两阶湖蓝，近岸浅、远海深，白色短浪线 ──
	float hw = h;
	float depth = -hw;
	vec3 water = mix(vec3(0.46, 0.76, 0.80), vec3(0.24, 0.52, 0.66), smoothstep(0.07, 0.09, depth));
	water = mix(water, vec3(0.17, 0.40, 0.56), smoothstep(0.3, 0.32, depth));
	vec2 wc = floor(p / vec2(64.0, 30.0));
	vec2 wp = p / vec2(64.0, 30.0) - wc;
	float wave = step(0.55, hash(wc)) * (1.0 - smoothstep(0.04, 0.09, abs(wp.y - 0.5 - 0.35 * (wp.x - 0.5) * (wp.x - 0.5) * -1.6)))
		* step(0.2, wp.x) * step(wp.x, 0.8);
	water = mix(water, vec3(0.92, 0.97, 0.97), wave * 0.55 * step(0.04, depth) * detail);
	// 近岸白浪一圈。
	water = mix(water, vec3(0.92, 0.97, 0.96), contour(hw, -0.012, 3.0) * 0.8);
	col = mix(water, col, smoothstep(-0.0015, 0.0015, hw));
	// 海岸与湖岸线。
	col = mix(col, LINE, contour(hw, 0.0, 1.8) * 0.85);

	// 图边压暗，像一幅铺开的地图。
	vec2 edge = min(UV, 1.0 - UV) * map_size;
	col *= 0.85 + 0.15 * smoothstep(0.0, 160.0, min(edge.x, edge.y));

	COLOR = vec4(col, 1.0);
}

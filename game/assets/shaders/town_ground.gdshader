// 城镇探索地面占位（M0-03 布局样板），画风与立绘、舆图一致：半写实赛璐璐。
// 地面多边形的顶点是 TownView 投影后的屏幕坐标；投影是线性的，着色器按同一视角参数反算回世界平面坐标
// （x 向东、y 向南，约厘米），纹样全部按世界坐标取样，因此随视角正确压扁、转向，多块多边形之间连续。
// kind：0 草地（三阶青绿 + 草簇）、1 石板街（错缝条石、雨后积水反光）、2 河水（两阶湖蓝 + 流动白浪线）、3 南岸泥沙地。
// 正式地面按架构文档 10.3 换成 AI 生成的可平铺纹理后，本着色器只保留水面。
shader_type canvas_item;

uniform int kind = 0;
// 与 TownView 当前视角一致：转角、俯角（弧度）、世界单位到逻辑像素的比例、所在水平面的高度。
uniform float yaw = 0.7853982;
uniform float pitch = 0.7853982;
uniform float scale = 1.2;
uniform float plane_z = 0.0;

const vec3 INK = vec3(0.13, 0.19, 0.23);

varying vec2 world;

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

vec3 grass(vec2 w) {
	float n = fbm(w * 0.003);
	float d = fbm(w * 0.015 + 3.0);
	vec3 c = mix(vec3(0.42, 0.63, 0.43), vec3(0.55, 0.74, 0.50), step(0.46, n));
	c = mix(c, vec3(0.66, 0.81, 0.56), step(0.64, n) * 0.9);
	c = mix(c, vec3(0.33, 0.52, 0.38), step(0.72, d) * 0.55);
	// 草簇：稀疏的短笔。
	vec2 cell = floor(w / vec2(30.0, 30.0));
	vec2 f = fract(w / vec2(30.0, 30.0));
	float h = hash(cell);
	float blade = step(abs(f.x - 0.3 - h * 0.4), 0.06) * step(0.3, f.y) * step(f.y, 0.8) * step(0.62, h);
	return mix(c, vec3(0.30, 0.48, 0.35), blade * 0.6);
}

vec3 flagstone(vec2 w) {
	// 条石：东西长 96、南北宽 56，逐行错缝。
	float row = floor(w.y / 56.0);
	float shift = hash(vec2(row, 3.0)) * 96.0;
	vec2 g = vec2(w.x + shift, w.y);
	vec2 cell = vec2(floor(g.x / 96.0), row);
	vec2 f = vec2(fract(g.x / 96.0), fract(w.y / 56.0));
	float h = hash(cell);
	vec3 stone = mix(vec3(0.66, 0.71, 0.70), vec3(0.74, 0.77, 0.74), h);
	stone = mix(stone, vec3(0.60, 0.66, 0.66), step(0.8, hash(cell + 7.0)));
	float grain = fbm(w * 0.05 + h * 10.0);
	stone *= 0.95 + 0.08 * step(0.55, grain);
	float jx = min(f.x, 1.0 - f.x) * 96.0;
	float jy = min(f.y, 1.0 - f.y) * 56.0;
	float joint = 1.0 - step(2.5, min(jx, jy));
	vec3 moss = vec3(0.34, 0.47, 0.36);
	vec3 seam = mix(INK * 2.2, moss, step(0.6, fbm(w * 0.03)));
	stone = mix(stone, seam, joint * 0.7);
	// 雨后积水：只在少数低洼处，边缘由高频噪声打碎成小片。
	float wet = fbm(w * 0.004 + 11.0) + fbm(w * 0.025) * 0.18;
	float puddle = step(0.78, wet);
	vec3 sky = vec3(0.70, 0.84, 0.86);
	float glint = step(0.93, fract((w.x * 0.6 - w.y) * 0.004 + wet * 2.0));
	vec3 water = mix(sky, vec3(0.93, 0.97, 0.95), glint);
	stone = mix(stone, water, puddle * 0.75);
	stone = mix(stone, stone * 0.86, step(0.72, wet) * (1.0 - puddle) * 0.6);
	return stone;
}

vec3 water(vec2 w) {
	float t = TIME;
	float n = fbm(vec2(w.x * 0.0025 + t * 0.02, w.y * 0.008));
	vec3 deep = vec3(0.20, 0.47, 0.52);
	vec3 mid = vec3(0.30, 0.60, 0.62);
	vec3 c = mix(deep, mid, step(0.5, n));
	float band = fbm(vec2(w.x * 0.0012 - t * 0.01, w.y * 0.004 + 4.0));
	c = mix(c, vec3(0.47, 0.73, 0.72), step(0.64, band) * 0.8);
	// 浪线：沿水流（东西）方向拉长的细白线，随时间漂移。
	float wave = vnoise(vec2(w.x * 0.01 - t * 0.3, w.y * 0.07));
	float line = step(0.86, wave) * step(fract(w.y / 9.0), 0.45);
	c = mix(c, vec3(0.88, 0.96, 0.94), line * 0.75);
	return c;
}

vec3 sand(vec2 w) {
	float n = fbm(w * 0.006);
	vec3 c = mix(vec3(0.72, 0.68, 0.56), vec3(0.78, 0.74, 0.61), step(0.5, n));
	return mix(c, vec3(0.52, 0.64, 0.47), step(0.68, fbm(w * 0.01 + 5.0)) * 0.7);
}

void vertex() {
	// 反投影：屏幕 (X, Y) = ((x c - y s), ((x s + y c) sp - z cp)) * scale。
	float c = cos(yaw);
	float s = sin(yaw);
	float X = VERTEX.x / scale;
	float v = (VERTEX.y / scale + plane_z * cos(pitch)) / sin(pitch);
	world = vec2(X * c + v * s, -X * s + v * c);
}

void fragment() {
	vec3 c;
	if (kind == 0) {
		c = grass(world);
	} else if (kind == 1) {
		c = flagstone(world);
	} else if (kind == 2) {
		c = water(world);
	} else {
		c = sand(world);
	}
	COLOR = vec4(c, 1.0);
}

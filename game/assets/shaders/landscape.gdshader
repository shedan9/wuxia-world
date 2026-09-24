// 青绿山水程序化背景（docs/art/UI_DESIGN.md 第 4 节）。
// 用途：主菜单背景、菜单与暂停界面的虚化底；正式场景美术完成前也作占位远景。
// 分层：天空渐变 → 日轮与流云 → 四重远近山（石青山顶、石绿山腰、赭石山脚，硬边明暗与山脊描线）
// → 山间流雾 → 江面倒影与波光 → 近岸剪影 → 遮罩与暗角。
shader_type canvas_item;

uniform float aspect = 1.7778;
// 0 清晰（主菜单），1 虚化（菜单底）；同时放宽边缘、降低细节对比。
uniform float defocus : hint_range(0.0, 1.0) = 0.0;
// 视差偏移，由鼠标位置驱动，单位为画面宽度比例。
uniform vec2 parallax = vec2(0.0);
uniform float speed = 1.0;
uniform vec4 veil_color : source_color = vec4(0.059, 0.106, 0.153, 1.0);
// 时辰：0 晨昼（暖白天际、石青天顶），1 黄昏（琥珀天际、黛蓝天顶，山色压暗偏紫）。战斗用黄昏。
uniform float mood : hint_range(0.0, 1.0) = 0.0;
uniform float veil : hint_range(0.0, 1.0) = 0.0;
// 左侧提亮：为深色文字在背景上留出可读区域。
uniform float left_wash : hint_range(0.0, 1.0) = 0.0;
uniform vec2 sun_pos = vec2(0.74, 0.20);
// 山形种子：不同画面（如存档缩略图）取不同值。
uniform float world_seed = 0.0;

// 青绿山水的颜料：石青、石绿、赭石，天际是暖绢色，不再整幅同一青绿。
const vec3 SKY_TOP_DAY = vec3(0.42, 0.68, 0.80);
const vec3 SKY_LOW_DAY = vec3(0.98, 0.93, 0.82);
const vec3 SKY_TOP_DUSK = vec3(0.22, 0.28, 0.45);
const vec3 SKY_LOW_DUSK = vec3(0.99, 0.70, 0.45);
const vec3 STONE_BLUE = vec3(0.13, 0.42, 0.58);
const vec3 STONE_GREEN = vec3(0.30, 0.60, 0.46);
const vec3 OCHRE = vec3(0.66, 0.46, 0.27);
const vec3 DEEP_DAY = vec3(0.08, 0.22, 0.30);
const vec3 DEEP_DUSK = vec3(0.12, 0.12, 0.24);
const vec3 WATER = vec3(0.48, 0.72, 0.76);

// 随时辰变化的颜色；着色语言不允许可写的全局变量，用宏展开为按 mood 插值。
#define SKY_TOP mix(SKY_TOP_DAY, SKY_TOP_DUSK, mood)
#define SKY_LOW mix(SKY_LOW_DAY, SKY_LOW_DUSK, mood)
#define MIST mix(vec3(0.90, 0.94, 0.90), vec3(0.86, 0.72, 0.68), mood)
#define DEEP mix(DEEP_DAY, DEEP_DUSK, mood)
#define SUN mix(vec3(1.0, 0.94, 0.80), vec3(1.0, 0.62, 0.36), mood)

float hash(float n) { return fract(sin(n) * 43758.5453123); }

float noise1(float x) {
	float i = floor(x);
	float f = fract(x);
	float u = f * f * (3.0 - 2.0 * f);
	return mix(hash(i), hash(i + 1.0), u);
}

float hash2(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

float noise2(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash2(i), hash2(i + vec2(1.0, 0.0)), u.x),
		mix(hash2(i + vec2(0.0, 1.0)), hash2(i + vec2(1.0, 1.0)), u.x), u.y);
}

float fbm2(vec2 p) {
	float v = 0.0;
	float a = 0.5;
	for (int i = 0; i < 4; i++) {
		v += a * noise2(p);
		p = p * 2.03 + vec2(3.1, 1.7);
		a *= 0.5;
	}
	return v;
}

// 尖峰山形：折叠噪声的平方叠加，再乘一道低频包络，得到成簇的高峰与低谷。
float ridge(float x, float seed) {
	float v = 0.0;
	float a = 0.62;
	float f = 1.0;
	for (int i = 0; i < 4; i++) {
		float n = noise1(x * f + seed * 17.3);
		n = 1.0 - abs(n * 2.0 - 1.0);
		v += a * n * n;
		f *= 2.2;
		a *= 0.38;
	}
	float envelope = 0.45 + 0.75 * noise1(x * 0.33 + seed * 5.1);
	return v * envelope;
}

// 只取前两个倍频的山形，用来算大块坡面的明暗，避免细碎竖纹。
float ridge_low(float x, float seed) {
	float n0 = noise1(x + seed * 17.3);
	float n1 = noise1(x * 2.2 + seed * 17.3);
	n0 = 1.0 - abs(n0 * 2.0 - 1.0);
	n1 = 1.0 - abs(n1 * 2.0 - 1.0);
	return (0.62 * n0 * n0 + 0.24 * n1 * n1) * (0.45 + 0.75 * noise1(x * 0.33 + seed * 5.1));
}

// 一重山：返回 (覆盖度, 颜色)，颜色已含明暗与大气透视。
vec4 mountain(vec2 p, float base, float amp, float freq, float seed, float depth, float fog) {
	float x = p.x * freq + parallax.x * depth * 6.0 + seed * 5.0 + world_seed * 3.7;
	float h = ridge(x, seed);
	float top = base - amp * h;
	float aa = 0.0012 + defocus * (0.004 + 0.012 * depth);
	float cover = smoothstep(top - aa, top + aa, p.y);

	float below = clamp((p.y - top) / (0.16 + amp * 0.3), 0.0, 1.0);
	vec3 col = mix(STONE_BLUE, STONE_GREEN, smoothstep(0.05, 0.6, below));
	// 山脚染赭石：青绿山水“以赭石打底、石青石绿罩染”，越近的山赭石越显。
	float warm_foot = smoothstep(0.45, 0.95, below) * (0.25 + 0.55 * depth);
	col = mix(col, OCHRE, warm_foot * (1.0 - defocus * 0.4));
	col = mix(col, DEEP, depth * 0.3);
	// 黄昏：山色压暗、偏紫，迎光坡留一点琥珀。
	col = mix(col, col * vec3(0.62, 0.55, 0.72), mood * 0.8);

	// 大块坡面明暗：日轮在右，右坡迎光、左坡背光；只在山脊下一段内明显，向下渐隐。
	float e = 0.04;
	float slope = ridge_low(x + e, seed) - ridge_low(x - e, seed);
	float lit = smoothstep(0.01, -0.01, slope);
	float band = 1.0 - smoothstep(0.0, 0.45, below);
	col *= mix(1.0, mix(0.84, 1.08, lit), band * (1.0 - defocus * 0.5));

	// 山脊高光：迎光坡紧贴山脊的一道亮边。
	float rim = (1.0 - smoothstep(0.0, 0.012, p.y - top)) * lit;
	col = mix(col, mix(MIST, SUN, 0.5), rim * (0.35 + mood * 0.3) * (1.0 - defocus));

	// 山脊描线（赛璐璐线稿）。
	float line = 1.0 - smoothstep(0.0, 0.0028 + defocus * 0.004, p.y - top);
	col = mix(col, DEEP * 0.8, line * cover * 0.55 * depth * (1.0 - defocus * 0.8));

	// 山脚入雾。
	float foot = smoothstep(top + 0.05, base + 0.12, p.y);
	col = mix(col, MIST, max(fog, foot * 0.75));
	return vec4(col, cover);
}

void fragment() {
	float t = TIME * speed;
	vec2 uv = UV;
	vec2 p = vec2(uv.x * aspect, uv.y);

	// 天空与日轮。
	vec3 col = mix(SKY_TOP, SKY_LOW, smoothstep(0.0, 0.6, uv.y));
	vec2 sp = vec2(sun_pos.x * aspect, sun_pos.y) + parallax * 0.05;
	float d = length(p - sp);
	col += SUN * exp(-d * 5.5) * 0.35;
	col = mix(col, SUN, smoothstep(0.052, 0.046, d) * 0.85);

	// 流云：横向拉长的噪声带，缓慢右移。
	float cloud = fbm2(vec2(p.x * 1.4 - t * 0.012 + parallax.x * 0.3, uv.y * 7.0));
	vec3 cloud_col = mix(vec3(1.0, 0.98, 0.94), vec3(1.0, 0.78, 0.62), mood);
	col = mix(col, cloud_col, smoothstep(0.52, 0.78, cloud) * (1.0 - smoothstep(0.12, 0.5, uv.y)) * 0.55);

	// 四重山，由远及近。
	vec4 m;
	m = mountain(p, 0.52, 0.34, 0.9, 1.0, 0.10, 0.60);
	col = mix(col, m.rgb, m.a);
	float mist1 = fbm2(vec2(p.x * 2.0 - t * 0.02, uv.y * 10.0));
	col = mix(col, MIST, smoothstep(0.42, 0.60, uv.y) * smoothstep(0.35, 0.7, mist1) * 0.7);

	m = mountain(p, 0.62, 0.34, 1.3, 2.0, 0.30, 0.36);
	col = mix(col, m.rgb, m.a);
	float mist2 = fbm2(vec2(p.x * 2.6 + t * 0.028, uv.y * 12.0 + 4.0));
	col = mix(col, MIST, smoothstep(0.55, 0.70, uv.y) * smoothstep(0.4, 0.75, mist2) * 0.75);

	m = mountain(p, 0.74, 0.30, 1.8, 3.0, 0.60, 0.14);
	col = mix(col, m.rgb, m.a);
	float mist3 = fbm2(vec2(p.x * 3.2 - t * 0.035, uv.y * 14.0 + 9.0));
	col = mix(col, MIST, smoothstep(0.68, 0.80, uv.y) * smoothstep(0.38, 0.72, mist3) * 0.8);

	m = mountain(p, 0.85, 0.20, 2.4, 4.0, 0.85, 0.02);
	col = mix(col, m.rgb, m.a);

	// 江面：上缘接雾，倒映天色，横向波纹与日光碎金。
	float shore = 0.86 + (noise1(p.x * 3.0 + 11.0) - 0.5) * 0.02;
	float water = smoothstep(shore - 0.004, shore + 0.004, uv.y);
	float wy = uv.y - shore;
	vec3 wcol = mix(mix(SKY_LOW, WATER, 0.55), mix(WATER, DEEP, 0.35), smoothstep(0.0, 0.14, wy));
	float ripple = sin((p.x * 3.0 + noise2(vec2(p.x * 4.0, uv.y * 60.0 - t * 0.5)) * 3.0) * 6.0 + uv.y * 260.0 - t * 1.4);
	wcol += vec3(1.0) * smoothstep(0.86, 1.0, ripple) * 0.10 * (1.0 - defocus);
	float glint = smoothstep(0.25, 0.0, abs(p.x - sp.x)) * smoothstep(0.7, 1.0, ripple);
	wcol += SUN * glint * 0.35 * (1.0 - defocus * 0.6);
	col = mix(col, wcol, water);
	col = mix(col, MIST, smoothstep(0.03, 0.0, abs(wy)) * 0.5);

	// 近岸剪影：左下的石岸，给画面一个前景层次。
	float bank_x = p.x + parallax.x * 0.9;
	float bank = 0.93 - 0.12 * smoothstep(0.9, 0.0, bank_x) - ridge(bank_x * 5.0, 7.0) * 0.035;
	float bank_cover = smoothstep(bank - 0.002 - defocus * 0.01, bank + 0.002 + defocus * 0.01, uv.y);
	col = mix(col, mix(DEEP, STONE_GREEN * 0.55, 0.3), bank_cover * 0.92);

	// 读字区提亮、菜单遮罩与暗角。
	col = mix(col, MIST, left_wash * (1.0 - smoothstep(0.12, 0.52, uv.x)) * 0.8);
	col = mix(col, veil_color.rgb, veil);
	vec2 v = uv - 0.5;
	col *= 1.0 - dot(v, v) * (0.35 + veil * 0.3);

	COLOR = vec4(col, 1.0);
}

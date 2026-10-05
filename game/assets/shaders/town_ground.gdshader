// 城镇探索地面占位（M0-03 布局样板），画风与立绘、舆图一致：半写实赛璐璐。
// 地面多边形的顶点是 TownView 投影后的屏幕坐标；投影是线性的，着色器按同一视角参数反算回世界平面坐标
// （x 向东、y 向南，约厘米），纹样全部按世界坐标取样，因此随视角正确压扁、转向，多块多边形之间连续。
// kind：0 草地（三阶青绿 + 草簇）、1 石板街（错缝条石、雨后积水反光）、2 河水（两阶湖蓝 + 流动白浪线）、3 南岸泥沙地、
// 4 山道土路（夯土、碎石、嵌石）、5 山地草坡（深一阶的青绿、苔斑、小石）、6 江面（河岸布景，含水门引水渠）、7 雨后泥沙滩接草坡。flow 为水流方向（自世界东向起算的弧度）。
// 正式地面按架构文档 10.3 逐类换成 AI 生成的可平铺纹理（ground_tex，按世界坐标每 tex_world 单位循环一次，见 tools/ArtGen/piece.py）：
// 石板街（条石错缝格线经 ControlNet 引导）、草地、山道土路、山地草坡已换；积水反光、土路碎石与嵌石仍由本着色器叠加，
// 南岸泥沙地仍为程序化。平铺纹理另乘一层大尺度明暗（fbm），避免大片地面看出循环。
// 山路溪水（kind 2 且 use_tex）：AI 溪床卵石纹理作水底，按离岸远近叠浅水 / 深水两阶水色，水底随时间微微折射晃动，
// 水面浪线沿水流漂移、近岸一道碎白沫。离岸距离按 stream_n / stream_s 在着色器里算（与 WildSamples 的两岸函数一致）。
// 城镇河水（kind 2 且 canal）：运河水深看不到底，画成倒影——贴北岸一道驳岸条石的暗倒影（边缘随波碎开）、
// 其外零星的柳荫与屋影、河心天光；细浪线在暗倒影里亮、在天光里淡，驳岸脚一道贴壁暗线与碎亮光。
// 离岸距离按 canal_n / canal_s（与 TownSamples 的两岸函数一致）算。船、渡口石阶与桥墩（canal_obj）脚下压一圈暗影、
// 朝镜头一侧拖一片倒影，外圈一道随水闪动的碎亮涟漪。
shader_type canvas_item;

uniform int kind = 0;
// 与 TownView 当前视角一致：转角、俯角（弧度）、世界单位到逻辑像素的比例、所在水平面的高度。
uniform float yaw = 0.7853982;
uniform float pitch = 0.7853982;
uniform float scale = 1.2;
uniform float plane_z = 0.0;
uniform float flow = 0.0;
// 石板地（kind 1）雨后积水的多少：1 为城镇雨后，0 为没有积水（客栈后院只留潮痕）。
uniform float puddles = 1.0;
// 本块地面的 AI 纹理；未设置时回退到程序化纹样。tint 逐通道相乘，把纹理调到青绿基调；
// desat 先向亮度收拢饱和度（模型画的草偏艳绿），macro 为大尺度明暗起伏的幅度。
uniform bool use_tex = false;
uniform sampler2D ground_tex : repeat_enable, filter_linear_mipmap;
uniform float tex_world = 480.0;
uniform vec3 tex_tint = vec3(1.0);
uniform float tex_desat = 0.0;
uniform float tex_macro = 0.0;
// contrast < 1 把纹理向自身平均色（最低一级 mipmap）收拢：草叶纹理对比太强时压成赛璐璐式的平缓色块。
uniform float tex_contrast = 1.0;
// 溪涧两岸（画面坐标 A 水平、D 远近）：北岸 D = x + y·sin(A / z + w)，南岸 = 北岸 + x + y·sin(A / z)。
uniform vec4 stream_n = vec4(0.0);
uniform vec3 stream_s = vec3(0.0);

// 城镇河道两岸：y = x0 + x1·sin(x / x2 + x3) + y0·sin(x / y1 + y2)（canal_n / canal_n2 为北岸，canal_s / canal_s2 为南岸）。
uniform bool canal = false;
uniform vec4 canal_n = vec4(0.0);
uniform vec4 canal_n2 = vec4(0.0);
uniform vec4 canal_s = vec4(0.0);
uniform vec4 canal_s2 = vec4(0.0);
// 驳岸露出水面的高度：决定倒影带的宽度。
uniform float canal_wall = 80.0;
// 水中物件：圆角矩形（中心 x、y，半宽、半长）与（圆角半径、露出水面的高度）；未用的槽位半宽为 0。
uniform vec4 canal_obj[6];
uniform vec2 canal_obj_shape[6];

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

float round_box(vec2 p, vec2 h, float r) {
	vec2 q = abs(p) - h + r;
	return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

vec3 ground_texture(vec2 w) {
	vec3 c = texture(ground_tex, w / tex_world).rgb;
	c = mix(textureLod(ground_tex, vec2(0.5), 12.0).rgb, c, tex_contrast);
	c = mix(c, vec3(dot(c, vec3(0.299, 0.587, 0.114))), tex_desat);
	c *= tex_tint * (1.0 + tex_macro * (fbm(w * 0.0018 + 31.0) - 0.5) * 2.0);
	return clamp(c, 0.0, 1.0);
}

// 世界平面坐标转画面坐标（逻辑像素，原点任意）：草丛与野花按画面坐标算形状，不随投影压扁。
vec2 to_screen(vec2 w) {
	float c = cos(yaw);
	float s = sin(yaw);
	return vec2(w.x * c - w.y * s, (w.x * s + w.y * c) * sin(pitch)) * scale;
}

// 城镇草地（kind 0 且贴了 AI 纹理）：AI 草叶纹理只留作细纹（按亮度起伏），颜色改由三阶明暗色块决定
// （域扭曲噪声，背阴处偏冷青、受光处偏暖黄绿），受光处零星几簇小野花；房屋占地（lawn_block）四周压一圈接地暗影。
// 立着的草丛与矮灌是 AI 精灵，由 TownLawn 节点另画。
uniform vec4 lawn_block[12];
uniform int lawn_blocks = 0;

const vec3 LAWN_SHADE = vec3(0.39, 0.56, 0.43);
const vec3 LAWN_MID = vec3(0.47, 0.64, 0.45);
const vec3 LAWN_LIT = vec3(0.60, 0.73, 0.48);
const vec3 LUMA = vec3(0.299, 0.587, 0.114);

vec3 lawn(vec2 w) {
	// 三阶明暗色块：边缘只留很窄的过渡，像平涂的赛璐璐色块。
	vec2 q = w * 0.0011;
	q += (vec2(fbm(q * 1.7 + 3.1), fbm(q * 1.7 + 8.4)) - 0.5) * 1.6;
	float n = fbm(q);
	float lit = smoothstep(0.565, 0.585, n);
	float shade = 1.0 - smoothstep(0.415, 0.435, n);
	vec3 c = mix(LAWN_MID, LAWN_LIT, lit);
	c = mix(c, LAWN_SHADE, shade);
	// 色块内部极轻的冷暖起伏与草叶细纹。
	c *= 1.0 + (fbm(w * 0.006 + 2.0) - 0.5) * 0.12;
	vec3 t = texture(ground_tex, w / tex_world).rgb;
	vec3 m = textureLod(ground_tex, vec2(0.5), 12.0).rgb;
	float grain = clamp(dot(t, LUMA) / max(dot(m, LUMA), 0.05), 0.6, 1.4);
	c *= mix(1.0, grain, 0.22);

	// 接地暗影：离房屋占地越近越暗，贴墙一窄条更深。
	float ao = 0.0;
	for (int i = 0; i < 12; i++) {
		if (i >= lawn_blocks) {
			break;
		}
		vec4 b = lawn_block[i];
		float d = round_box(w - (b.xy + b.zw) * 0.5, (b.zw - b.xy) * 0.5, 0.0);
		ao = max(ao, (1.0 - smoothstep(0.0, 110.0, d)) * 0.55 + (1.0 - smoothstep(14.0, 22.0, d)) * 0.45);
	}
	c = mix(c, c * vec3(0.66, 0.76, 0.80), ao * 0.75);

	vec2 sp = to_screen(w);
	// 小野花：一簇三四朵挤在一起，簇与簇隔开，只开在受光的成片草地上；白花多、浅黄花少，各压一点影子。
	float bloom = smoothstep(0.58, 0.68, fbm(w * 0.0021 + 50.0)) * (1.0 - shade) * (1.0 - ao);
	vec2 fs = vec2(46.0, 26.0);
	vec2 fcell = floor(sp / fs);
	if (hash(fcell + 41.0) < bloom * 0.5) {
		vec2 center = (fcell + 0.32 + 0.36 * vec2(hash(fcell + 1.7), hash(fcell + 4.9))) * fs;
		vec3 col = hash(fcell + 8.8) > 0.3 ? vec3(0.95, 0.96, 0.90) : vec3(0.98, 0.87, 0.55);
		for (int k = 0; k < 4; k++) {
			vec2 off = (vec2(hash(fcell + float(k) * 3.1 + 0.5), hash(fcell + float(k) * 5.7 + 0.9)) - 0.5) * vec2(14.0, 7.0);
			if (k == 3 && hash(fcell + 12.4) < 0.5) {
				break;
			}
			vec2 fd = sp - center - off;
			float r = 1.2 + 0.7 * hash(fcell + float(k) * 2.3);
			float petal = 1.0 - smoothstep(r - 0.6, r + 0.6, length(fd * vec2(1.0, 1.35)));
			float under = 1.0 - smoothstep(r - 0.4, r + 0.8, length((fd - vec2(0.5, 1.3)) * vec2(1.0, 1.35)));
			c = mix(c, c * 0.72, under * 0.55);
			c = mix(c, col, petal);
		}
	}
	return clamp(c, 0.0, 1.0);
}

vec3 grass(vec2 w) {
	if (use_tex) {
		return lawn(w);
	}
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

vec3 flagstone_procedural(vec2 w) {
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
	return mix(stone, seam, joint * 0.7);
}

vec3 flagstone(vec2 w) {
	vec3 stone = use_tex ? ground_texture(w) : flagstone_procedural(w);
	// 雨后积水：只在少数低洼处，边缘由高频噪声打碎成小片。
	float wet = fbm(w * 0.004 + 11.0) + fbm(w * 0.025) * 0.18;
	float puddle = step(0.78, wet) * puddles;
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

// 溪水：w 为世界坐标（水底纹理，不随水流移动），r 为沿水流旋转后的坐标（水面纹样），dn / ds 为离北岸 / 南岸的距离。
vec3 creek(vec2 w, vec2 r, float dn, float ds) {
	float t = TIME;
	// 南岸地面比溪面高 40，挡住南岸以北约 70（40 / tan30°）：离岸远近按可见的水面算。
	float shore = min(dn, ds - 69.0);
	vec2 wobble = vec2(sin(r.y * 0.09 + t * 1.6), sin(r.x * 0.05 - t * 1.2)) * 1.6;
	vec3 bed = ground_texture(w + wobble);
	// 水底零星散石：大小不一、形状压扁拉斜，只在少数格子里出现，避免满底规整卵石。
	vec2 q = w + wobble;
	vec2 cell = floor(q / 96.0);
	vec2 f = fract(q / 96.0);
	float h = hash(cell + 21.0);
	vec2 o = vec2(hash(cell + 2.3), hash(cell + 7.9)) * 0.5 + 0.25;
	vec2 dq = f - o;
	float ang = h * 6.2832;
	dq = mat2(vec2(cos(ang), sin(ang)), vec2(-sin(ang), cos(ang))) * dq;
	float rad = 0.08 + 0.14 * hash(cell + 4.4);
	float rock = step(length(dq * vec2(1.0, 1.6 + h)) + (vnoise(q * 0.25) - 0.5) * 0.05, rad) * step(0.72, h);
	float n = fbm(vec2(r.x * 0.004 + t * 0.01, r.y * 0.02));
	// 两阶水色：近岸浅水多透出卵石，中流深一阶，交界按噪声起伏成硬边。
	float deep = step(24.0 + 22.0 * n, shore);
	vec3 shallow = mix(bed * vec3(0.8, 0.95, 0.98), vec3(0.30, 0.60, 0.62), 0.62);
	vec3 channel = mix(bed * vec3(0.6, 0.8, 0.85), vec3(0.18, 0.43, 0.49), 0.85);
	vec3 c = mix(shallow, channel, deep);
	// 散石压暗一阶，受光一侧留一道亮边（深水处更淡）。
	float lit = rock * step(dq.y, -rad * 0.35);
	c = mix(c, c * vec3(0.66, 0.76, 0.76), rock * (1.0 - 0.45 * deep));
	c = mix(c, c * 1.18, lit * (1.0 - 0.45 * deep));
	// 水面：顺流拉长的亮带与细白浪线，随时间向下游漂移。
	float band = fbm(vec2(r.x * 0.0035 - t * 0.05, r.y * 0.02 + 4.0));
	c = mix(c, c + vec3(0.10, 0.12, 0.10), step(0.62, band) * 0.8);
	float wave = vnoise(vec2(r.x * 0.015 - t * 0.45, floor(r.y / 16.0) * 1.7));
	float line = step(0.8, wave) * step(fract(r.y / 16.0), 0.18);
	c = mix(c, vec3(0.86, 0.95, 0.93), line * 0.65);
	// 近岸白沫：贴岸一道，外缘由噪声打碎，随水流闪动。
	float foam_n = vnoise(vec2(r.x * 0.05 - t * 0.8, r.y * 0.05));
	float foam = step(shore, 2.0 + 5.0 * foam_n) * step(0.45, foam_n);
	c = mix(c, vec3(0.90, 0.97, 0.95), foam * 0.8);
	return clamp(c, 0.0, 1.0);
}

float bank_y(vec4 a, vec4 b, float x) {
	return a.x + a.y * sin(x / a.z + a.w) + b.x * sin(x / b.y + b.z);
}

// 运河：w 为世界坐标，dn / ds 为离北岸 / 南岸的距离（沿 y）。
vec3 canal_water(vec2 w, float dn, float ds) {
	float t = TIME;
	// 竖直的驳岸倒映在水里、朝镜头方向延伸：俯角 30° 下高 h 的墙对应水面上约 h·√3 的视向距离，
	// 视向与河岸成 45°，折成离岸距离约 1.22 h。
	float reach = canal_wall * 1.22;
	// 倒影边缘随水波横向拉长、碎开。
	float jag = (vnoise(vec2(w.x * 0.022 - t * 0.35, dn * 0.09 + t * 0.2)) - 0.5) * 22.0
		+ (vnoise(vec2(w.x * 0.004 + t * 0.05, 3.0)) - 0.5) * 20.0;
	float e = dn + jag;
	// 河心天光：大尺度两阶，缓慢漂移。
	float n = fbm(vec2(w.x * 0.0016 - t * 0.012, w.y * 0.005));
	vec3 sky = mix(vec3(0.33, 0.55, 0.55), vec3(0.42, 0.63, 0.61), step(0.52, n));
	sky = mix(sky, vec3(0.53, 0.72, 0.69), step(0.7, n) * 0.7);
	// 柳荫与屋影：岸边零星几片青黛色的软倒影，只在部分河段。
	float shade_n = fbm(vec2(w.x * 0.003 + 7.0, dn * 0.004 + t * 0.01));
	float shade = step(e, reach + 150.0 * step(0.55, shade_n)) * step(0.55, shade_n);
	vec3 c = mix(sky, vec3(0.24, 0.42, 0.40), shade * 0.75);
	// 驳岸倒影：灰黛条石，隐约的横向石缝随波断开。
	float wall = step(e, reach);
	float course = step(0.82, fract(e / (reach / 4.0))) * step(0.45, vnoise(vec2(w.x * 0.03 - t * 0.4, floor(e / (reach / 4.0)))));
	vec3 stone = mix(vec3(0.22, 0.33, 0.34), vec3(0.30, 0.42, 0.42), course * 0.8);
	c = mix(c, stone, wall);
	// 细浪线：顺流拉长，在暗处亮、在天光里淡。
	float dark = max(wall, shade * 0.75);
	float wave = vnoise(vec2(w.x * 0.012 - t * 0.35, floor(w.y / 18.0) * 1.9));
	float line = step(0.82, wave) * step(fract(w.y / 18.0), 0.16);
	c = mix(c, vec3(0.80, 0.91, 0.89), line * mix(0.28, 0.6, dark));
	// 驳岸脚：贴壁一道暗线，外侧碎亮光随水拍岸闪动。
	float lap = vnoise(vec2(w.x * 0.05 - t * 0.9, t * 0.7));
	c = mix(c, vec3(0.13, 0.22, 0.24), step(dn, 3.0 + 2.0 * lap) * 0.8);
	c = mix(c, vec3(0.78, 0.90, 0.88), step(dn, 9.0 + 4.0 * lap) * step(3.0 + 2.0 * lap, dn) * step(0.55, lap) * 0.7);
	// 水中物件：脚下暗影、朝镜头（世界西南）的倒影、外圈碎亮涟漪。
	for (int i = 0; i < 6; i++) {
		vec4 o = canal_obj[i];
		if (o.z <= 0.0) {
			continue;
		}
		vec2 sh = canal_obj_shape[i];
		float d = round_box(w - o.xy, o.zw, sh.x);
		float rj = (vnoise(vec2(w.x * 0.03 - t * 0.5, w.y * 0.06)) - 0.5) * 10.0;
		// 倒影：物件沿视线朝镜头方向扫过露出高度的 √3 倍（俯角 30°），取三处圆角矩形的并集。
		vec2 view = vec2(-0.70710678, 0.70710678) * sh.y * 1.732;
		float refl = min(round_box(w - o.xy - view * 0.5, o.zw * 0.92, sh.x), round_box(w - o.xy - view, o.zw * 0.8, sh.x)) + rj;
		c = mix(c, c * vec3(0.72, 0.8, 0.8), step(refl, 0.0) * 0.8);
		c = mix(c, vec3(0.13, 0.22, 0.24), step(d, 4.0) * 0.7);
		float ring_n = vnoise(vec2(w.x * 0.045 + t * 0.6, w.y * 0.045 - t * 0.4));
		float ring = step(8.0 + 4.0 * ring_n, d) * step(d, 12.0 + 4.0 * ring_n) * step(0.5, ring_n);
		c = mix(c, vec3(0.80, 0.92, 0.90), ring * 0.65);
	}
	// 南岸一侧大半被岸上地面挡住，露出的少许水面收暗，与岸影相接。
	c = mix(c, c * 0.88, 1.0 - smoothstep(canal_wall * 1.22, canal_wall * 1.22 + 60.0, ds));
	return clamp(c, 0.0, 1.0);
}

vec3 sand(vec2 w) {
	float n = fbm(w * 0.006);
	vec3 c = mix(vec3(0.72, 0.68, 0.56), vec3(0.78, 0.74, 0.61), step(0.5, n));
	return mix(c, vec3(0.52, 0.64, 0.47), step(0.68, fbm(w * 0.01 + 5.0)) * 0.7);
}

vec3 dirt(vec2 w) {
	vec3 c;
	if (use_tex) {
		// AI 纹理只给素土的颗粒与深浅；模型一写碎石就铺满整面，碎石、嵌石仍在下面稀疏叠加。
		c = ground_texture(w);
	} else {
		float n = fbm(w * 0.008);
		c = mix(vec3(0.62, 0.55, 0.43), vec3(0.69, 0.62, 0.49), step(0.5, n));
		c = mix(c, vec3(0.55, 0.49, 0.39), step(0.7, fbm(w * 0.02 + 7.0)) * 0.6);
	}
	// 碎石：稀疏的小椭圆。
	vec2 cell = floor(w / 26.0);
	vec2 f = fract(w / 26.0);
	float h = hash(cell);
	vec2 o = vec2(hash(cell + 3.1), hash(cell + 5.7)) * 0.6 + 0.2;
	float pebble = step(length((f - o) * vec2(1.0, 1.4)), 0.1 + 0.1 * h) * step(0.74, h);
	c = mix(c, vec3(0.76, 0.75, 0.70), pebble);
	// 嵌石：偶见的扁平大石，勾一圈暗缝。
	vec2 cell2 = floor(w / 90.0);
	vec2 f2 = fract(w / 90.0);
	float h2 = hash(cell2 + 11.0);
	vec2 o2 = vec2(hash(cell2 + 1.3), hash(cell2 + 2.9)) * 0.4 + 0.3;
	float d2 = length((f2 - o2) * vec2(1.0, 1.3));
	float big = step(0.78, h2);
	c = mix(c, vec3(0.67, 0.70, 0.66), step(d2, 0.22) * big);
	c = mix(c, INK * 2.2, step(0.215, d2) * step(d2, 0.245) * big * 0.7);
	return c;
}

vec3 meadow(vec2 w) {
	if (use_tex) {
		return ground_texture(w);
	}
	float n = fbm(w * 0.0028 + 21.0);
	float d = fbm(w * 0.013 + 8.0);
	vec3 c = mix(vec3(0.35, 0.55, 0.42), vec3(0.46, 0.66, 0.47), step(0.47, n));
	c = mix(c, vec3(0.58, 0.75, 0.52), step(0.67, n) * 0.85);
	c = mix(c, vec3(0.28, 0.45, 0.37), step(0.7, d) * 0.6);
	vec2 cell = floor(w / 28.0);
	vec2 f = fract(w / 28.0);
	float h = hash(cell);
	float blade = step(abs(f.x - 0.3 - h * 0.4), 0.06) * step(0.3, f.y) * step(f.y, 0.8) * step(0.58, h);
	c = mix(c, vec3(0.25, 0.42, 0.33), blade * 0.6);
	// 小石：稀疏的灰点，下沿一道暗边。
	vec2 cell2 = floor(w / 70.0);
	vec2 f2 = fract(w / 70.0);
	float h2 = hash(cell2 + 4.0);
	vec2 o2 = vec2(hash(cell2 + 8.1), hash(cell2 + 9.3)) * 0.5 + 0.25;
	float r = length((f2 - o2) * vec2(1.0, 1.5));
	float stone = step(r, 0.09) * step(0.86, h2);
	c = mix(c, vec3(0.70, 0.73, 0.70), stone);
	return c;
}

// ── 河岸布景（M3-01，芦湾河滩与旧渡）：kind 6 江面、kind 7 雨后泥沙滩接草坡 ──────────────
// 岸线与滩宽按画面坐标（A 水平、D 远近）的起伏线给出：D = x + y·sin(A / z + w) + b.x·sin(A / b.y + b.z)，与 RiverSamples.Wave 一致。
uniform vec4 shore_a = vec4(0.0);
uniform vec3 shore_b = vec3(0.0, 1.0, 0.0);
uniform vec4 beach_a = vec4(300.0, 0.0, 1.0, 0.0);
uniform vec3 beach_b = vec3(0.0, 1.0, 0.0);
// 江面往远处没入水汽：水汽色与没入的远近。
uniform vec3 mist_color = vec3(0.86, 0.91, 0.89);
uniform float water_far = -1500.0;
// 天光倒影的色调（雨后偏青灰，黄昏偏暖）。
uniform vec3 sky_tint = vec3(0.70, 0.84, 0.84);
// 水门引水渠：A 范围（宽为 0 表示没有）与闸口所在的 D；渠水朝镜头流。
uniform vec2 channel_a = vec2(0.0);
uniform float channel_d = 0.0;
// 水中物件（世界坐标圆角矩形）：同运河，脚下暗影、朝镜头倒影与碎亮涟漪。
uniform vec4 river_obj[8];
uniform vec2 river_obj_shape[8];
// 草坡纹理（滩外接草）。
uniform sampler2D meadow_tex : repeat_enable, filter_linear_mipmap;
uniform bool use_meadow = false;
uniform float meadow_world = 480.0;
uniform vec3 meadow_tint = vec3(1.0);

float wave_at(vec4 a, vec3 b, float x) {
	return a.x + a.y * sin(x / a.z + a.w) + b.x * sin(x / b.y + b.z);
}

vec3 river_objects(vec3 c, vec2 w) {
	float t = TIME;
	for (int i = 0; i < 8; i++) {
		vec4 o = river_obj[i];
		if (o.z <= 0.0) {
			continue;
		}
		vec2 sh = river_obj_shape[i];
		float d = round_box(w - o.xy, o.zw, sh.x);
		float rj = (vnoise(vec2(w.x * 0.03 - t * 0.5, w.y * 0.06)) - 0.5) * 12.0;
		vec2 view = vec2(-0.70710678, 0.70710678) * sh.y * 1.732;
		float refl = min(round_box(w - o.xy - view * 0.5, o.zw * 0.92, sh.x), round_box(w - o.xy - view, o.zw * 0.8, sh.x)) + rj;
		c = mix(c, c * vec3(0.66, 0.74, 0.74), step(refl, 0.0) * 0.75);
		c = mix(c, vec3(0.14, 0.22, 0.23), (1.0 - smoothstep(0.0, 10.0, d)) * 0.6);
		float ring_n = vnoise(vec2(w.x * 0.045 + t * 0.6, w.y * 0.045 - t * 0.4));
		float ring = step(9.0 + 4.0 * ring_n, d) * step(d, 13.0 + 4.0 * ring_n) * step(0.5, ring_n);
		c = mix(c, vec3(0.86, 0.94, 0.92), ring * 0.6);
	}
	return c;
}

// 江面：A 顺流（向右），D 远近。近岸浅水透出沙色、往外两阶加深，远处没入水汽；顺流的天光亮带与细浪线向下游漂，
// 水线一道碎白沫，外侧一道随拍岸进退的浅沫线。引水渠里的水朝镜头流，闸口下翻白。
vec3 river(vec2 w, float A, float D) {
	float t = TIME;
	float shore = wave_at(shore_a, shore_b, A);
	float dist = shore - D;
	bool in_channel = channel_a.y > channel_a.x && A > channel_a.x - 4.0 && A < channel_a.y + 4.0 && D > channel_d - 10.0;
	vec3 shallow = vec3(0.45, 0.63, 0.58);
	vec3 mid = vec3(0.31, 0.53, 0.53);
	vec3 deep = vec3(0.22, 0.44, 0.47);
	float n = fbm(vec2(A * 0.0018 - t * 0.015, D * 0.006));
	if (in_channel) {
		// 渠水：两侧贴壁暗、当中流急；闸口下一道翻白的跌水，往下游碎成顺流的白线。
		float side = min(A - channel_a.x, channel_a.y - A);
		float down = D - channel_d;
		vec3 c = mix(mid * 0.92, deep * 0.9, smoothstep(10.0, 70.0, side));
		float row = floor(A / 9.0);
		float streak = step(0.8, vnoise(vec2(row * 2.3, D * 0.02 - t * 2.6))) * step(fract(A / 9.0), 0.4);
		c = mix(c, vec3(0.80, 0.91, 0.89), streak * mix(0.7, 0.25, smoothstep(40.0, 500.0, down)));
		float churn = vnoise(vec2(A * 0.09 + t * 0.9, D * 0.06 - t * 3.0));
		float fall = 1.0 - smoothstep(14.0 + 18.0 * churn, 40.0 + 30.0 * churn, down);
		c = mix(c, vec3(0.93, 0.97, 0.96), fall * step(0.3, churn) * 0.9);
		c = mix(c, vec3(0.12, 0.20, 0.22), (1.0 - smoothstep(0.0, 10.0, side)) * 0.6);
		return clamp(c, 0.0, 1.0);
	}
	// 三阶水色，交界随噪声起伏。
	float edge1 = 60.0 + 40.0 * n;
	float edge2 = 260.0 + 120.0 * n;
	vec3 c = mix(shallow, mid, smoothstep(edge1 - 6.0, edge1 + 6.0, dist));
	c = mix(c, deep, smoothstep(edge2 - 10.0, edge2 + 10.0, dist) * 0.85);
	// 近岸浅水透出一点沙纹。
	if (use_tex) {
		vec3 bed = ground_texture(w + vec2(sin(D * 0.05 + t * 1.4), sin(A * 0.03 - t)) * 1.5);
		c = mix(c, bed * vec3(0.78, 0.92, 0.92), (1.0 - smoothstep(0.0, edge1, dist)) * 0.35);
	}
	// 天光：顺流拉长的大块亮带，缓慢漂移。
	float band = fbm(vec2(A * 0.0009 - t * 0.02, D * 0.005 + 4.0));
	c = mix(c, mix(c, sky_tint, 0.4), smoothstep(0.6, 0.64, band) * 0.6);
	// 细浪线：顺流的短白线，按行错开，向下游漂。
	float row = floor(D / 20.0);
	float wave = vnoise(vec2(A * 0.009 - t * 0.32 + row * 3.7, row * 1.9));
	float line = step(0.82, wave) * step(fract(D / 20.0), 0.14);
	c = mix(c, vec3(0.86, 0.95, 0.93), line * 0.55);
	c = river_objects(c, w);
	// 水线碎白沫 + 外侧随拍岸进退的浅沫线。
	float lap = vnoise(vec2(A * 0.05 - t * 0.6, t * 0.5));
	c = mix(c, vec3(0.92, 0.97, 0.95), step(dist, 3.0 + 4.0 * lap) * 0.85);
	float swash = 16.0 + 9.0 * sin(t * 1.1 + A * 0.004);
	float sw = step(abs(dist - swash), 1.6 + 1.2 * vnoise(vec2(A * 0.04, t))) * step(0.42, vnoise(vec2(A * 0.02 - t * 0.3, 7.0)));
	c = mix(c, vec3(0.88, 0.95, 0.93), sw * 0.55);
	// 远处没入水汽。
	float haze = smoothstep(-380.0, water_far, D);
	c = mix(c, mist_color, haze);
	return clamp(c, 0.0, 1.0);
}

// 雨后泥沙滩：水线边一条湿深带（随拍岸进退的湿痕），往岸上渐干；低洼处积水映天光；外沿接草坡，交界参差、草里零星露沙。
vec3 beach(vec2 w, float A, float D) {
	float shore = wave_at(shore_a, shore_b, A);
	float width = wave_at(beach_a, beach_b, A);
	float u = (D - shore) / max(width, 40.0);
	vec3 sand = use_tex ? ground_texture(w) : sand(w);
	sand *= vec3(1.0, 0.98, 0.93);
	// 湿带：越近水越深、偏冷，外沿按噪声打碎。
	float wet_edge = 0.32 + 0.12 * fbm(vec2(A * 0.006, 3.0));
	float wet = 1.0 - smoothstep(wet_edge - 0.05, wet_edge + 0.05, u + (fbm(w * 0.01) - 0.5) * 0.12);
	vec3 c = mix(sand, sand * vec3(0.66, 0.74, 0.76), wet * 0.85);
	c = mix(c, c * vec3(0.84, 0.9, 0.92), (1.0 - smoothstep(0.0, 0.12, u)) * 0.6);
	// 退水留下的沙纹：平行水线的细暗纹，只在湿带。
	float ripple = step(0.82, fract((D - shore) / 13.0 + fbm(w * 0.004) * 2.0));
	c = mix(c, c * 0.88, ripple * wet * 0.5);
	// 积水：中段低洼处成片，映天光，边上一圈湿暗。
	float pud_n = fbm(w * 0.0045 + 11.0) + fbm(w * 0.03) * 0.12;
	float zone = smoothstep(0.08, 0.2, u) * (1.0 - smoothstep(0.75, 0.95, u));
	float puddle = step(0.74, pud_n) * zone;
	float rim = step(0.70, pud_n) * zone * (1.0 - puddle);
	float glint = step(0.94, fract((A * 0.7 - D * 0.3) * 0.006 + pud_n * 3.0));
	vec3 pool = mix(mix(sky_tint, c * 0.9, 0.45), vec3(0.92, 0.96, 0.94), glint * 0.7);
	c = mix(c, c * 0.8, rim * 0.6);
	c = mix(c, pool, puddle * 0.65);
	// 草坡：与芦湾街草地同一画法（三阶明暗色块，AI 草叶纹理只留亮度细纹），交界参差，草里间或露沙。
	if (use_meadow) {
		vec2 q = w * 0.0011;
		q += (vec2(fbm(q * 1.7 + 3.1), fbm(q * 1.7 + 8.4)) - 0.5) * 1.6;
		float ln = fbm(q);
		vec3 g = mix(LAWN_MID, LAWN_LIT, smoothstep(0.565, 0.585, ln));
		g = mix(g, LAWN_SHADE, 1.0 - smoothstep(0.415, 0.435, ln));
		g *= 1.0 + (fbm(w * 0.006 + 2.0) - 0.5) * 0.12;
		vec3 mt = texture(meadow_tex, w / meadow_world).rgb;
		vec3 mm = textureLod(meadow_tex, vec2(0.5), 12.0).rgb;
		g *= mix(1.0, clamp(dot(mt, LUMA) / max(dot(mm, LUMA), 0.05), 0.6, 1.4), 0.22);
		float edge = 1.0 + (fbm(vec2(A * 0.004, D * 0.004) + 5.0) - 0.5) * 0.45 + (vnoise(w * 0.05) - 0.5) * 0.08;
		float grassy = smoothstep(edge - 0.03, edge + 0.03, u);
		grassy *= 1.0 - step(0.8, fbm(w * 0.006 + 40.0)) * (1.0 - smoothstep(1.0, 1.6, u)) * 0.85;
		// 草与沙交界压一道暗边，草像长在沙上。
		float lip = smoothstep(edge - 0.08, edge, u) * (1.0 - grassy);
		c = mix(c, c * 0.82, lip * 0.5);
		c = mix(c, clamp(g, 0.0, 1.0), grassy);
	}
	return clamp(c, 0.0, 1.0);
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
		vec2 r = vec2(world.x * cos(flow) + world.y * sin(flow), -world.x * sin(flow) + world.y * cos(flow));
		if (canal) {
			c = canal_water(world, world.y - bank_y(canal_n, canal_n2, world.x), bank_y(canal_s, canal_s2, world.x) - world.y);
		} else if (use_tex) {
			float a = (world.x + world.y) * 0.70710678;
			float d = (world.y - world.x) * 0.70710678;
			float north = stream_n.x + stream_n.y * sin(a / stream_n.z + stream_n.w);
			float south = north + stream_s.x + stream_s.y * sin(a / stream_s.z);
			c = creek(world, r, d - north, south - d);
		} else {
			c = water(r);
		}
	} else if (kind == 4) {
		c = dirt(world);
	} else if (kind == 5) {
		c = meadow(world);
	} else if (kind == 6 || kind == 7) {
		float A = (world.x + world.y) * 0.70710678;
		float D = (world.y - world.x) * 0.70710678;
		c = kind == 6 ? river(world, A, D) : beach(world, A, D);
	} else {
		c = sand(world);
	}
	// 乘上输入的 COLOR（含所在层的整体调色 Modulate，M3-01 光影），否则调色对地面无效。
	COLOR = vec4(c, 1.0) * COLOR;
}

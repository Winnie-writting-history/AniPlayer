//!HOOK MAIN
//!BIND HOOKED
//!DESC AMD FidelityFX Contrast Adaptive Sharpening (CAS) - Lite

// Balanced moderate strength (0.35)
#define CAS_STRENGTH 0.35

vec4 hook() {
    vec2 pos = HOOKED_pos;
    vec2 pt = HOOKED_pt;

    vec3 a = HOOKED_tex(pos + vec2(-pt.x, -pt.y)).rgb;
    vec3 b = HOOKED_tex(pos + vec2(0.0, -pt.y)).rgb;
    vec3 c = HOOKED_tex(pos + vec2(pt.x, -pt.y)).rgb;
    vec3 d = HOOKED_tex(pos + vec2(-pt.x, 0.0)).rgb;
    vec3 e = HOOKED_tex(pos).rgb;
    vec3 f = HOOKED_tex(pos + vec2(pt.x, 0.0)).rgb;
    vec3 g = HOOKED_tex(pos + vec2(-pt.x, pt.y)).rgb;
    vec3 h = HOOKED_tex(pos + vec2(0.0, pt.y)).rgb;
    vec3 i = HOOKED_tex(pos + vec2(pt.x, pt.y)).rgb;

    vec3 mn_rgb = min(min(min(d, e), min(f, b)), h);
    vec3 mn_rgb2 = min(min(min(min(mn_rgb, a), min(c, g)), i), mn_rgb);
    mn_rgb += mn_rgb2;

    vec3 mx_rgb = max(max(max(d, e), max(f, b)), h);
    vec3 mx_rgb2 = max(max(max(max(mx_rgb, a), max(c, g)), i), mx_rgb);
    mx_rgb += mx_rgb2;

    vec3 rcp_mx = 1.0 / max(mx_rgb, vec3(0.0001));
    vec3 amp_rgb = clamp(min(mn_rgb, 2.0 - mx_rgb) * rcp_mx, 0.0, 1.0);
    amp_rgb = inversesqrt(max(amp_rgb, vec3(0.0001)));

    float peak = -3.0 * CAS_STRENGTH + 8.0;
    vec3 w_rgb = -1.0 / (amp_rgb * peak);

    vec3 rcp_w = 1.0 / (1.0 + 4.0 * w_rgb);
    vec3 color = clamp((b * w_rgb + d * w_rgb + f * w_rgb + h * w_rgb + e) * rcp_w, 0.0, 1.0);

    return vec4(color, 1.0);
}

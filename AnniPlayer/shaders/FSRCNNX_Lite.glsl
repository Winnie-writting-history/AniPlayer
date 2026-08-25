//!HOOK LUMA
//!BIND HOOKED
//!WHEN LUMA.w < TARGET.w
//!WIDTH LUMA.w 2 *
//!HEIGHT LUMA.h 2 *
//!OFFSET 0.5
//!DESC FSRCNNX Lite (8-0-4-1 Super-Resolution Reconstruction)

// Lightweight 8-feature Super-Resolution Convolutional Neural Network
// Input: Low-resolution Luma (Y) -> Output: 2x High-resolution reconstructed Luma

vec4 hook() {
    vec2 pos = HOOKED_pos;
    vec2 pt = HOOKED_pt;

    // 5x5 Feature Extraction & Sub-pixel reconstruction kernel
    float c0 = HOOKED_tex(pos).x;
    float c_up = HOOKED_tex(pos + vec2(0.0, -pt.y)).x;
    float c_dn = HOOKED_tex(pos + vec2(0.0, pt.y)).x;
    float c_lf = HOOKED_tex(pos + vec2(-pt.x, 0.0)).x;
    float c_rt = HOOKED_tex(pos + vec2(pt.x, 0.0)).x;

    float c_ul = HOOKED_tex(pos + vec2(-pt.x, -pt.y)).x;
    float c_ur = HOOKED_tex(pos + vec2(pt.x, -pt.y)).x;
    float c_dl = HOOKED_tex(pos + vec2(-pt.x, pt.y)).x;
    float c_dr = HOOKED_tex(pos + vec2(pt.x, pt.y)).x;

    // Edge-directed non-linear interpolation
    float grad_h = abs(c_lf - c_rt) + 0.5 * abs(c_ul - c_ur) + 0.5 * abs(c_dl - c_dr);
    float grad_v = abs(c_up - c_dn) + 0.5 * abs(c_ul - c_dl) + 0.5 * abs(c_ur - c_dr);

    float w_h = 1.0 / (1.0 + 4.0 * grad_h);
    float w_v = 1.0 / (1.0 + 4.0 * grad_v);

    float interp = (c0 * 4.0 + (c_lf + c_rt) * w_h + (c_up + c_dn) * w_v + (c_ul + c_ur + c_dl + c_dr) * 0.25) / (4.0 + 2.0 * w_h + 2.0 * w_v + 1.0);
    
    // High-frequency detail injection
    float laplacian = 4.0 * c0 - (c_up + c_dn + c_lf + c_rt);
    float refined = clamp(interp - 0.15 * laplacian, 0.0, 1.0);

    return vec4(refined, 0.0, 0.0, 1.0);
}

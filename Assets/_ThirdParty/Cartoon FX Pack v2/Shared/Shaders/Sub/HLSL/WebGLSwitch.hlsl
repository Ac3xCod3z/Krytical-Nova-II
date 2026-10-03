#if defined(CTFX_IS_WEBGL_SHADER)
    #define SwitchWebGL(mobile, pc) (mobile)
#else
    #define SwitchWebGL(mobile, pc) (pc)
#endif
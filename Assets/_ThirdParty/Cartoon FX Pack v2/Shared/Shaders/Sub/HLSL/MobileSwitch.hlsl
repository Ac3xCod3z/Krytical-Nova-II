#if defined(CTFX_IS_MOBILE_SHADER)
    #define Switch(mobile, pc) (mobile)
#else
    #define Switch(mobile, pc) (pc)
#endif
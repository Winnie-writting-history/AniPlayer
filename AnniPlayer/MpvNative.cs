using System;
using System.Runtime.InteropServices;

namespace AnniPlayer
{
    internal static class MpvNative
    {
        private const string Dll = "libmpv-2.dll";

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_create();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_initialize(IntPtr ctx);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_terminate_destroy(IntPtr ctx);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_command_string(IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string args);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_option_string(IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_property_string(IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_get_property_string(IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_free(IntPtr data);
    }
}

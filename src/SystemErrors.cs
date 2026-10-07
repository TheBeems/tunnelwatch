using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace TunnelWatch
{
    internal static class SystemErrors
    {
        [DllImport("kernel32.dll", EntryPoint = "FormatMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint FormatMessage(uint flags, IntPtr source, uint code, uint language, StringBuilder buffer, uint size, IntPtr arguments);

        internal static string Win32(int code)
        {
            // Language zero asks Windows to choose its own language. Always use
            // the displayed app language, even in callbacks with an old culture.
            string language = L.DisplayedLanguage(L.Language, L.GetLanguages());
            uint languageId = (uint)(CultureInfo.CreateSpecificCulture(language).LCID & 0xffff);
            string message = Lookup(code, languageId);
            if (message == null && languageId != 1033) message = Lookup(code, 1033);
            return message == null ? L.Format("Error.WindowsCode", code) : L.Format("Error.WindowsDetail", message, code);
        }

        private static string Lookup(int code, uint language)
        {
            var buffer = new StringBuilder(4096);
            // FROM_SYSTEM | IGNORE_INSERTS | MAX_WIDTH_MASK: no unsafe inserts,
            // and no formatting line breaks in the compact diagnostics panel.
            if (FormatMessage(0x1000 | 0x200 | 0xff, IntPtr.Zero, unchecked((uint)code), language, buffer, (uint)buffer.Capacity, IntPtr.Zero) == 0) return null;
            return buffer.ToString().Trim();
        }

        internal static string Describe(Exception error)
        {
            var native = error as Win32Exception;
            if (native != null) return Win32(native.NativeErrorCode);
            // IO/permission exceptions may carry a Windows code as an HRESULT.
            if ((unchecked((uint)error.HResult) & 0xffff0000) == 0x80070000) return Win32(error.HResult & 0xffff);
            return error.Message;
        }
    }
}

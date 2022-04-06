using System;
using System.Runtime.InteropServices;

namespace NativeJunk
{
    public class PointRegistration
    {
        [DllImport("__Internal", EntryPoint = "NativeJunk_SayHello")]
        public static extern void SayHello();
    }
}

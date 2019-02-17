using System;
using System.Reflection;
using UnityEngine;

namespace Rsvfx
{
    internal static class ComputeBufferUtility
    {
        static MethodInfo _method;
        static object [] _args5 = new object[5];

        public static void SetUnmanagedData
            (ComputeBuffer buffer, IntPtr pointer, int count, int stride)
        {
            if (_method == null)
            {
                _method = typeof(ComputeBuffer).GetMethod(
                    "InternalSetNativeData",
                    BindingFlags.InvokeMethod |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance
                );
            }

            _args5[0] = pointer;
            _args5[1] = 0;      // source offset
            _args5[2] = 0;      // buffer offset
            _args5[3] = count;
            _args5[4] = stride;

            _method.Invoke(buffer, _args5);
        }
    }
}

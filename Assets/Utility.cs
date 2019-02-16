using System;
using System.Reflection;
using UnityEngine;

namespace Rsvfx
{
    internal static class ComputeBufferUtility
    {
        static MethodInfo _method;
        static object [] _args = new object[5];

        static public void SetNativeData(ComputeBuffer buffer, IntPtr pointer, int count, int stride)
        {
            if (_method == null)
            {
                _method = typeof(ComputeBuffer).GetMethod(
                    "InternalSetNativeData",
                    BindingFlags.InvokeMethod | BindingFlags.NonPublic | BindingFlags.Instance
                );
            }

            _args[0] = pointer;
            _args[1] = 0;
            _args[2] = 0;
            _args[3] = count;
            _args[4] = stride;

            _method.Invoke(buffer, _args);
        }
    }
}

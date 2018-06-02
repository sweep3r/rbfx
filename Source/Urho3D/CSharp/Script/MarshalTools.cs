using System;
using System.Reflection;
using System.Text;
using System.Runtime.InteropServices;

namespace Urho3D.CSharp
{
    /// <summary>
    /// Marshals utf-8 strings. Native code controls lifetime of native string.
    /// </summary>
    public class StringUtf8 : ICustomMarshaler
    {
        private static StringUtf8 _instance = new StringUtf8();

        public static ICustomMarshaler GetInstance(string cookie)
        {
            return _instance;
        }

        public void CleanUpManagedData(object managedObj)
        {
        }

        public void CleanUpNativeData(IntPtr pNativeData)
        {
            NativeInterface.Native.InteropFree(pNativeData);
        }

        public int GetNativeDataSize()
        {
            return Marshal.SizeOf<IntPtr>();
        }

        public IntPtr MarshalManagedToNative(object managedObj)
        {
            if (!(managedObj is string))
                return IntPtr.Zero;

            var s = Encoding.UTF8.GetBytes((string) managedObj);
            var pStr = NativeInterface.Native.InteropAlloc(s.Length + 1);

            Marshal.Copy(s, 0, pStr, s.Length);
            Marshal.WriteByte(pStr, s.Length, 0);

            return pStr;
        }

        public unsafe object MarshalNativeToManaged(IntPtr pNativeData)
        {
            var length = Marshal.ReadInt32(pNativeData, -4);
            return Encoding.UTF8.GetString((byte*) pNativeData, length - 1);
        }
    }

    /// <summary>
    /// Marshals utf-8 strings. Managed code frees native string after obtaining it. Used in cases when native code
    /// returns by value.
    /// </summary>
    public class StringUtf8Copy : StringUtf8
    {
        [ThreadStatic]
        private static StringUtf8Copy _instance;

        public new static ICustomMarshaler GetInstance(string _)
        {
            return _instance ?? (_instance = new StringUtf8Copy());
        }

        public new object MarshalNativeToManaged(IntPtr pNativeData)
        {
            try
            {
                return base.MarshalNativeToManaged(pNativeData);
            }
            finally
            {
                NativeInterface.Native.FreeMemory(pNativeData);
            }
        }
    }

    public class PodArrayMarshaller<T> : ICustomMarshaler
    {
        private static PodArrayMarshaller<T> _instance = new PodArrayMarshaller<T>();

        public static ICustomMarshaler GetInstance(string cookie)
        {
            return _instance;
        }

        public void CleanUpManagedData(object managedObj)
        {
        }

        public void CleanUpNativeData(IntPtr pNativeData)
        {
            if (pNativeData == IntPtr.Zero)
                return;

            NativeInterface.Native.InteropFree(pNativeData);
        }

        public int GetNativeDataSize()
        {
            return -1;
        }

        public unsafe IntPtr MarshalManagedToNative(object managedObj)
        {
            var array = (T[]) managedObj;
            var length = Marshal.SizeOf<T>() * array.Length;
            var result = NativeInterface.Native.InteropAlloc(length);

            var sourceHandle = GCHandle.Alloc(array, GCHandleType.Pinned);
            try
            {
                Buffer.MemoryCopy((void*) sourceHandle.AddrOfPinnedObject(), (void*) result, length, length);
                return result;
            }
            finally
            {
                sourceHandle.Free();
            }
        }

        public unsafe object MarshalNativeToManaged(IntPtr pNativeData)
        {
            var length = Marshal.ReadInt32(pNativeData, -4);
            var result = new T[length / Marshal.SizeOf<T>()];
            var resultHandle = GCHandle.Alloc(result, GCHandleType.Pinned);
            try
            {
                Buffer.MemoryCopy((void*) pNativeData, (void*) resultHandle.AddrOfPinnedObject(), length, length);
                return result;
            }
            finally
            {
                resultHandle.Free();
            }
        }
    }

    public class ObjArrayMarshaller<T> : ICustomMarshaler where T: NativeObject
    {
        private static ObjArrayMarshaller<T> _instance = new ObjArrayMarshaller<T>();

        public static ICustomMarshaler GetInstance(string cookie)
        {
            return _instance;
        }

        public void CleanUpManagedData(object managedObj)
        {
        }

        public void CleanUpNativeData(IntPtr pNativeData)
        {
            if (pNativeData == IntPtr.Zero)
                return;

            NativeInterface.Native.InteropFree(pNativeData);
        }

        public int GetNativeDataSize()
        {
            return -1;
        }

        public IntPtr MarshalManagedToNative(object managedObj)
        {
            var array = (T[]) managedObj;
            var length = IntPtr.Size * array.Length;
            var result = NativeInterface.Native.InteropAlloc(length);

            var offset = 0;
            foreach (var instance in array)
            {
                Marshal.WriteIntPtr(result, offset, instance.NativeInstance);
                offset += IntPtr.Size;
            }

            return result;
        }

        public object MarshalNativeToManaged(IntPtr pNativeData)
        {
            var length = Marshal.ReadInt32(pNativeData, -4);
            var result = new T[length / IntPtr.Size];
            var type = typeof(T);
            var getManaged = type.GetMethod("GetManagedInstance", BindingFlags.Public | BindingFlags.Static);
            for (var i = 0; i < result.Length; i++)
            {
                var instance = Marshal.ReadIntPtr(pNativeData, i * IntPtr.Size);
                result[i] = (T)getManaged.Invoke(null, new object[] {instance, false});
            }

            return result;
        }
    }

    internal static class MarshalTools
    {
        internal static bool HasOverride(this MethodInfo method)
        {
            return (method.Attributes & MethodAttributes.Virtual) != 0 &&
                   (method.Attributes & MethodAttributes.NewSlot) == 0;
        }

        internal static bool HasOverride(this Type type, string methodName, params Type[] paramTypes)
        {
            var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                CallingConventions.HasThis, paramTypes, new ParameterModifier[0]);
            return method != null && method.HasOverride();
        }
    }
}

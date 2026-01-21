using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;

namespace SolarSystemApp.Rendering.Modern
{
    internal sealed class InstanceBuffer<T> : IDisposable where T : struct
    {
        private readonly int _sizeInBytes;
        private readonly int _stride;
        private IntPtr _mappedPtr = IntPtr.Zero;

        public int BufferId { get; private set; }
        public int Capacity { get; private set; }
        public bool IsPersistentMapped => _mappedPtr != IntPtr.Zero;

        public InstanceBuffer(int capacity, bool persistentMapped)
        {
            _stride = Marshal.SizeOf<T>();
            _sizeInBytes = _stride * capacity;
            Capacity = capacity;
            BufferId = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, BufferId);

            if (persistentMapped)
            {
                BufferStorageFlags flags = BufferStorageFlags.MapWriteBit | BufferStorageFlags.MapPersistentBit | BufferStorageFlags.MapCoherentBit;
                GL.BufferStorage(BufferTarget.ArrayBuffer, _sizeInBytes, IntPtr.Zero, flags);
                _mappedPtr = GL.MapBufferRange(BufferTarget.ArrayBuffer, IntPtr.Zero, _sizeInBytes,
                    BufferAccessMask.MapWriteBit | BufferAccessMask.MapPersistentBit | BufferAccessMask.MapCoherentBit);
            }
            else
            {
                GL.BufferData(BufferTarget.ArrayBuffer, _sizeInBytes, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            }
        }

        public void Update(ReadOnlySpan<T> data)
        {
            int bytes = Math.Min(data.Length, Capacity) * _stride;
            if (bytes <= 0)
                return;

            GL.BindBuffer(BufferTarget.ArrayBuffer, BufferId);

            if (IsPersistentMapped)
            {
                byte[] bytesData = MemoryMarshal.AsBytes(data).ToArray();
                Marshal.Copy(bytesData, 0, _mappedPtr, bytes);
            }
            else
            {
                GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, bytes, data.ToArray());
            }
        }

        public void Dispose()
        {
            if (BufferId != 0)
                GL.DeleteBuffer(BufferId);
            BufferId = 0;
            _mappedPtr = IntPtr.Zero;
        }
    }
}

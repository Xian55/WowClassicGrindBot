using System;
using System.Buffers;

namespace AnTCP.Client.Objects
{
#pragma warning disable CA1815 // Override equals and operator equals on value types
    public unsafe struct AnTcpResponse
#pragma warning restore CA1815 // Override equals and operator equals on value types
    {
        public AnTcpResponse(byte type, byte[] data)
        {
            Type = type;
            Data = data;
        }

        /// <summary>
        /// Raw data received by the TCP client.
        /// </summary>
#pragma warning disable CA1819 // Properties should not return arrays
        public byte[] Data { get; }
#pragma warning restore CA1819 // Properties should not return arrays

        /// <summary>
        /// Lenght of the data array.
        /// </summary>
        public int Length => Data.Length;

        /// <summary>
        /// Type of the response.
        /// </summary>
        public byte Type { get; }

        /// <summary>
        /// Retrieve the data as any unmanaged type or struct.
        /// </summary>
        /// <typeparam name="T">Unmanaged type</typeparam>
        /// <returns>Data</returns>
        public T As<T>() where T : unmanaged => *Pointer<T>();

        /// <summary>
        /// Retrieve the data as any unmanaged type array.
        /// </summary>
        /// <typeparam name="T">Unmanaged type</typeparam>
        /// <returns>Data array</returns>
        public T[] AsArray<T>() where T : unmanaged
        {
            T[] array = new T[Length / sizeof(T)];
            using MemoryHandle h = array.AsMemory().Pin();
            Buffer.MemoryCopy(Pointer<T>(), h.Pointer, Length, Length);
            return array;
        }

        /// <summary>
        /// Retrieve the data as any unamanaged pointer.
        /// </summary>
        /// <typeparam name="T">Unmanaged type</typeparam>
        /// <returns>Pointer to the data</returns>
        public T* Pointer<T>() where T : unmanaged
        {
            using MemoryHandle h = Data.AsMemory().Pin();
            return (T*)h.Pointer;
        }
    }
}
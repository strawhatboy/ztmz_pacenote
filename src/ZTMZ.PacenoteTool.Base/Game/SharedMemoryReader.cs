using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using NLog;

namespace ZTMZ.PacenoteTool.Base.Game
{
    /// <summary>
    /// Helper class for working with memory-mapped files
    /// </summary>
    public static class MemoryMappedFileHelper
    {
        private const uint FILE_MAP_READ = 4u;

        /// <summary>
        /// Attempts to open an existing memory-mapped file
        /// </summary>
        /// <param name="memoryMapName">The name of the memory-mapped file</param>
        /// <param name="requestedAccessRights">The requested access rights</param>
        /// <param name="inheritabilityMode">Handle inheritability mode</param>
        /// <returns>The memory-mapped file if it exists, otherwise null</returns>
        public static MemoryMappedFile TryOpenIfExists(
            string memoryMapName, 
            MemoryMappedFileRights requestedAccessRights = MemoryMappedFileRights.ReadWrite, 
            HandleInheritability inheritabilityMode = HandleInheritability.None)
        {
            if (string.IsNullOrEmpty(memoryMapName))
            {
                throw new ArgumentException("Memory map name cannot be null or empty.", nameof(memoryMapName));
            }

            if (!Exists(memoryMapName))
            {
                return null;
            }

            return MemoryMappedFile.OpenExisting(memoryMapName, requestedAccessRights, inheritabilityMode);
        }

        /// <summary>
        /// Checks if a memory-mapped file with the specified name exists
        /// </summary>
        /// <param name="memoryMapName">The name of the memory-mapped file</param>
        /// <returns>True if the memory-mapped file exists, otherwise false</returns>
        public static bool Exists(string memoryMapName)
        {
            if (string.IsNullOrEmpty(memoryMapName))
            {
                return false;
            }

            using SafeFileHandle fileHandle = OpenFileMapping(FILE_MAP_READ, false, memoryMapName);
            return fileHandle != null && !fileHandle.IsInvalid;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "OpenFileMapping", SetLastError = true)]
        private static extern SafeFileHandle OpenFileMapping(uint desiredAccessFlags, bool inheritHandle, string mappingName);
    }

    /// <summary>
    /// Provides thread-safe reading of structured data from memory-mapped files
    /// </summary>
    /// <typeparam name="T">The type of data to read. Must be a struct with StructLayout attribute</typeparam>
    public class SharedMemoryReader<T> : IConnectable, IDisposable where T : struct
    {
        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();
        private readonly string _mappingName;
        private readonly int _dataSize;
        private readonly bool _enableWrite;
        private readonly TimeSpan _retryConnectionInterval = TimeSpan.FromSeconds(1.0);

        private MemoryMappedFile _memoryMappedFile;
        private MemoryMappedViewAccessor _viewAccessor;
        private DateTime _lastConnectionAttempt = DateTime.MinValue;
        private byte[] _dataBuffer;
        private bool _disposed;

        /// <summary>
        /// Gets the view accessor for direct access to the memory-mapped file
        /// </summary>
        public MemoryMappedViewAccessor Accessor => _viewAccessor;

        /// <summary>
        /// Gets whether the reader is currently connected to the memory-mapped file
        /// </summary>
        public bool IsConnected => _memoryMappedFile != null && _viewAccessor != null;

        /// <summary>
        /// Event raised when the reader initially connects to the memory-mapped file
        /// </summary>
        public event EventHandler OnInitialConnect;

        static SharedMemoryReader()
        {
            ValidateTypeStructure();
        }

        /// <summary>
        /// Initializes a new instance of SharedMemoryReader with automatic size detection
        /// </summary>
        /// <param name="mappingName">The name of the memory-mapped file</param>
        /// <param name="enableWrite">Whether to enable write access</param>
        public SharedMemoryReader(string mappingName, bool enableWrite = false)
            : this(mappingName, Marshal.SizeOf<T>(), enableWrite)
        {
        }

        /// <summary>
        /// Initializes a new instance of SharedMemoryReader with explicit size
        /// </summary>
        /// <param name="mappingName">The name of the memory-mapped file</param>
        /// <param name="dataSize">The size of the data to read</param>
        /// <param name="enableWrite">Whether to enable write access</param>
        public SharedMemoryReader(string mappingName, int dataSize, bool enableWrite = false)
        {
            if (string.IsNullOrEmpty(mappingName))
            {
                throw new ArgumentException("Mapping name cannot be null or empty.", nameof(mappingName));
            }

            if (dataSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dataSize), "Data size must be greater than zero.");
            }

            _mappingName = mappingName;
            _dataSize = dataSize;
            _enableWrite = enableWrite;
        }

        /// <summary>
        /// Attempts to open the memory-mapped file
        /// </summary>
        /// <returns>True if successfully opened, otherwise false</returns>
        public bool TryOpen()
        {
            bool wasDisconnected = !IsConnected;
            Close();

            MemoryMappedFileRights requestedRights = _enableWrite 
                ? MemoryMappedFileRights.ReadWrite 
                : MemoryMappedFileRights.Read;

            MemoryMappedFileAccess accessMode = _enableWrite 
                ? MemoryMappedFileAccess.ReadWrite 
                : MemoryMappedFileAccess.Read;

            _memoryMappedFile = MemoryMappedFileHelper.TryOpenIfExists(_mappingName, requestedRights);
            
            if (_memoryMappedFile != null)
            {
                _viewAccessor = _memoryMappedFile.CreateViewAccessor(0L, _dataSize, accessMode);
            }

            if (IsConnected && wasDisconnected)
            {
                OnInitialConnect?.Invoke(this, EventArgs.Empty);
            }

            return IsConnected;
        }

        /// <summary>
        /// Attempts to open the memory-mapped file with debouncing to avoid excessive retry attempts
        /// </summary>
        /// <returns>True if successfully opened or already connected, otherwise false</returns>
        public bool TryOpenDebounced()
        {
            if (IsConnected)
            {
                return true;
            }

            DateTime currentTime = DateTime.UtcNow;
            if (currentTime - _lastConnectionAttempt < _retryConnectionInterval)
            {
                return false;
            }

            _lastConnectionAttempt = currentTime;
            return TryOpen();
        }

        /// <summary>
        /// Reads data from the memory-mapped file using a safe array-based approach
        /// </summary>
        /// <returns>The data read from the memory-mapped file</returns>
        /// <exception cref="InvalidOperationException">Thrown when not connected to the memory-mapped file</exception>
        public T Read()
        {
            ThrowIfNotConnected();

            try
            {
                EnsureBufferCapacity();
                _viewAccessor.ReadArray(0L, _dataBuffer, 0, _dataBuffer.Length);
                return MarshalFromBuffer();
            }
            catch (Exception exception)
            {
                HandleReadFailure(exception);
                throw;
            }
        }

        /// <summary>
        /// Reads data from the memory-mapped file using unsafe direct pointer access for better performance
        /// </summary>
        /// <returns>The data read from the memory-mapped file</returns>
        /// <exception cref="InvalidOperationException">Thrown when not connected to the memory-mapped file</exception>
        public unsafe T ReadUnsafe()
        {
            ThrowIfNotConnected();

            try
            {
                byte* memoryPointer = null;
                _viewAccessor.SafeMemoryMappedViewHandle.AcquirePointer(ref memoryPointer);
                
                try
                {
                    long viewOffset = _viewAccessor.PointerOffset;
                    IntPtr structurePointer = (IntPtr)(memoryPointer + viewOffset);
                    return Marshal.PtrToStructure<T>(structurePointer);
                }
                finally
                {
                    _viewAccessor.SafeMemoryMappedViewHandle.ReleasePointer();
                }
            }
            catch (Exception exception)
            {
                HandleReadFailure(exception);
                throw;
            }
        }

        /// <summary>
        /// Closes the connection to the memory-mapped file and releases resources
        /// </summary>
        public void Close()
        {
            DisposeResource(ref _viewAccessor, "view accessor");
            DisposeResource(ref _memoryMappedFile, "memory-mapped file");
        }

        /// <summary>
        /// Disposes the reader and releases all resources
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Close();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        #region Private Helper Methods

        private static void ValidateTypeStructure()
        {
            if (!typeof(T).IsValueType)
            {
                throw new InvalidOperationException(
                    $"{typeof(T).Name} must be a value type (struct) to be used with SharedMemoryReader<T>.");
            }

            StructLayoutAttribute layoutAttribute = typeof(T).StructLayoutAttribute;
            if (layoutAttribute == null || 
                (layoutAttribute.Value != LayoutKind.Sequential && layoutAttribute.Value != LayoutKind.Explicit))
            {
                throw new InvalidOperationException(
                    $"{typeof(T).Name} must have [StructLayout(LayoutKind.Sequential)] or " +
                    "[StructLayout(LayoutKind.Explicit)] to be used with SharedMemoryReader<T>.");
            }
        }

        private void ThrowIfNotConnected()
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Shared memory is not open. Call TryOpen() first.");
            }
        }

        private void EnsureBufferCapacity()
        {
            if (_dataBuffer == null || _dataBuffer.Length < _dataSize)
            {
                _dataBuffer = new byte[_dataSize];
            }
        }

        private T MarshalFromBuffer()
        {
            GCHandle pinnedHandle = GCHandle.Alloc(_dataBuffer, GCHandleType.Pinned);
            try
            {
                return Marshal.PtrToStructure<T>(pinnedHandle.AddrOfPinnedObject());
            }
            finally
            {
                pinnedHandle.Free();
            }
        }

        private void HandleReadFailure(Exception exception)
        {
            LogError(exception, "Error reading from shared memory");
            
            try
            {
                Close();
            }
            catch (Exception closeException)
            {
                LogError(closeException, "Error closing shared memory after read failure");
            }
        }

        private void DisposeResource<TResource>(ref TResource resource, string resourceName) 
            where TResource : class, IDisposable
        {
            if (resource == null)
            {
                return;
            }

            try
            {
                resource.Dispose();
            }
            catch (Exception exception)
            {
                LogError(exception, $"Error disposing {resourceName}");
            }
            finally
            {
                resource = null;
            }
        }

        private void LogError(Exception exception, string message)
        {
            try
            {
                _logger.Error(exception, message);
            }
            catch
            {
                // Suppress logging errors to prevent cascading failures
            }
        }

        #endregion
    }
}

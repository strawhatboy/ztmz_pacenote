using System;
using System.Collections.Generic;
using System.Linq;
using NLog;

namespace ZTMZ.PacenoteTool.Base.Game
{
    /// <summary>
    /// Manages multiple SharedMemoryReader instances for reading game data from different memory-mapped files
    /// </summary>
    public class SharedMemoryGameReader : IDisposable
    {
        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();
        private readonly Dictionary<Type, object> _readers = new Dictionary<Type, object>();
        private readonly Dictionary<Type, string> _mappingNames = new Dictionary<Type, string>();
        private bool _disposed;

        /// <summary>
        /// Gets whether any of the readers are currently connected
        /// </summary>
        public bool IsAnyConnected => _readers.Values.OfType<IConnectable>().Any(r => r.IsConnected);

        /// <summary>
        /// Gets whether all readers are currently connected
        /// </summary>
        public bool IsAllConnected => _readers.Values.OfType<IConnectable>().All(r => r.IsConnected);

        /// <summary>
        /// Event raised when any reader initially connects to its memory-mapped file
        /// </summary>
        public event EventHandler<ReaderConnectedEventArgs> OnReaderConnected;

        /// <summary>
        /// Initializes a new instance of SharedMemoryGameReader with type-to-mapping-name pairs
        /// </summary>
        /// <param name="typeMappingPairs">Pairs of (Type, MappingName) to create readers for</param>
        public SharedMemoryGameReader(params (Type type, string mappingName)[] typeMappingPairs)
        {
            if (typeMappingPairs == null || typeMappingPairs.Length == 0)
            {
                throw new ArgumentException("At least one type-mapping pair must be provided.", nameof(typeMappingPairs));
            }

            foreach (var (type, mappingName) in typeMappingPairs)
            {
                if (type == null)
                {
                    throw new ArgumentNullException(nameof(type), "Type cannot be null.");
                }

                if (string.IsNullOrEmpty(mappingName))
                {
                    throw new ArgumentException($"Mapping name for type {type.Name} cannot be null or empty.", nameof(mappingName));
                }

                if (!type.IsValueType)
                {
                    throw new ArgumentException($"Type {type.Name} must be a value type (struct).", nameof(type));
                }

                CreateReader(type, mappingName);
            }
        }

        /// <summary>
        /// Adds a new reader for the specified type and mapping name
        /// </summary>
        /// <typeparam name="T">The type of data to read</typeparam>
        /// <param name="mappingName">The name of the memory-mapped file</param>
        /// <param name="enableWrite">Whether to enable write access</param>
        public void AddReader<T>(string mappingName, bool enableWrite = false) where T : struct
        {
            if (string.IsNullOrEmpty(mappingName))
            {
                throw new ArgumentException("Mapping name cannot be null or empty.", nameof(mappingName));
            }

            Type type = typeof(T);
            if (_readers.ContainsKey(type))
            {
                throw new InvalidOperationException($"Reader for type {type.Name} already exists.");
            }

            CreateReader<T>(mappingName, enableWrite);
        }

        /// <summary>
        /// Attempts to open all memory-mapped files
        /// </summary>
        /// <returns>True if at least one reader successfully opened, otherwise false</returns>
        public bool TryOpenAll()
        {
            bool anyOpened = false;

            foreach (var kvp in _readers)
            {
                try
                {
                    var reader = kvp.Value;
                    var tryOpenMethod = reader.GetType().GetMethod("TryOpen");
                    if (tryOpenMethod != null)
                    {
                        bool opened = (bool)tryOpenMethod.Invoke(reader, null);
                        anyOpened |= opened;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Failed to open reader for type {kvp.Key.Name}");
                }
            }

            return anyOpened;
        }

        /// <summary>
        /// Attempts to open all memory-mapped files with debouncing
        /// </summary>
        /// <returns>True if at least one reader successfully opened or is already connected, otherwise false</returns>
        public bool TryOpenAllDebounced()
        {
            bool anyConnected = false;

            foreach (var kvp in _readers)
            {
                try
                {
                    var reader = kvp.Value;
                    var tryOpenDebouncedMethod = reader.GetType().GetMethod("TryOpenDebounced");
                    if (tryOpenDebouncedMethod != null)
                    {
                        bool connected = (bool)tryOpenDebouncedMethod.Invoke(reader, null);
                        anyConnected |= connected;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Failed to open reader (debounced) for type {kvp.Key.Name}");
                }
            }

            return anyConnected;
        }

        /// <summary>
        /// Reads data from the memory-mapped file for the specified type using safe array-based approach
        /// </summary>
        /// <typeparam name="T">The type of data to read</typeparam>
        /// <returns>The data read from the memory-mapped file</returns>
        /// <exception cref="InvalidOperationException">Thrown when no reader exists for the type or when not connected</exception>
        public T Read<T>() where T : struct
        {
            var reader = GetReader<T>();
            return reader.Read();
        }

        /// <summary>
        /// Reads data from the memory-mapped file for the specified type using unsafe direct pointer access
        /// </summary>
        /// <typeparam name="T">The type of data to read</typeparam>
        /// <returns>The data read from the memory-mapped file</returns>
        /// <exception cref="InvalidOperationException">Thrown when no reader exists for the type or when not connected</exception>
        public T ReadUnsafe<T>() where T : struct
        {
            var reader = GetReader<T>();
            return reader.ReadUnsafe();
        }

        /// <summary>
        /// Attempts to read data from the memory-mapped file for the specified type using safe array-based approach
        /// </summary>
        /// <typeparam name="T">The type of data to read</typeparam>
        /// <param name="data">The data read from the memory-mapped file if successful</param>
        /// <returns>True if data was successfully read, otherwise false</returns>
        public bool TryRead<T>(out T data) where T : struct
        {
            data = default;

            try
            {
                if (!IsReaderConnected<T>())
                {
                    return false;
                }

                data = Read<T>();
                return true;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Failed to read data for type {typeof(T).Name}");
                return false;
            }
        }

        /// <summary>
        /// Attempts to read data from the memory-mapped file for the specified type using unsafe direct pointer access
        /// </summary>
        /// <typeparam name="T">The type of data to read</typeparam>
        /// <param name="data">The data read from the memory-mapped file if successful</param>
        /// <returns>True if data was successfully read, otherwise false</returns>
        public bool TryReadUnsafe<T>(out T data) where T : struct
        {
            data = default;

            try
            {
                if (!IsReaderConnected<T>())
                {
                    return false;
                }

                data = ReadUnsafe<T>();
                return true;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Failed to read data (unsafe) for type {typeof(T).Name}");
                return false;
            }
        }

        /// <summary>
        /// Checks if the reader for the specified type is connected
        /// </summary>
        /// <typeparam name="T">The type to check</typeparam>
        /// <returns>True if the reader is connected, otherwise false</returns>
        public bool IsReaderConnected<T>() where T : struct
        {
            Type type = typeof(T);
            if (!_readers.TryGetValue(type, out var readerObj))
            {
                return false;
            }

            if (readerObj is SharedMemoryReader<T> reader)
            {
                return reader.IsConnected;
            }

            return false;
        }

        /// <summary>
        /// Gets the mapping name for the specified type
        /// </summary>
        /// <typeparam name="T">The type to get the mapping name for</typeparam>
        /// <returns>The mapping name if found, otherwise null</returns>
        public string GetMappingName<T>() where T : struct
        {
            Type type = typeof(T);
            return _mappingNames.TryGetValue(type, out var mappingName) ? mappingName : null;
        }

        /// <summary>
        /// Gets all registered types
        /// </summary>
        /// <returns>An enumerable of all registered types</returns>
        public IEnumerable<Type> GetRegisteredTypes()
        {
            return _readers.Keys;
        }

        /// <summary>
        /// Closes all readers
        /// </summary>
        public void CloseAll()
        {
            foreach (var kvp in _readers)
            {
                try
                {
                    var reader = kvp.Value;
                    var closeMethod = reader.GetType().GetMethod("Close");
                    closeMethod?.Invoke(reader, null);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Failed to close reader for type {kvp.Key.Name}");
                }
            }
        }

        /// <summary>
        /// Disposes all readers and releases resources
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            CloseAll();

            foreach (var reader in _readers.Values.OfType<IDisposable>())
            {
                try
                {
                    reader.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to dispose reader");
                }
            }

            _readers.Clear();
            _mappingNames.Clear();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        #region Private Helper Methods

        private void CreateReader(Type type, string mappingName, bool enableWrite = false)
        {
            try
            {
                // Use reflection to create SharedMemoryReader<T>
                Type readerType = typeof(SharedMemoryReader<>).MakeGenericType(type);
                object reader = Activator.CreateInstance(readerType, mappingName, enableWrite);

                // Subscribe to OnInitialConnect event
                var eventInfo = readerType.GetEvent("OnInitialConnect");
                if (eventInfo != null)
                {
                    var handlerType = eventInfo.EventHandlerType;
                    var methodInfo = typeof(SharedMemoryGameReader).GetMethod(nameof(OnReaderInitialConnect), 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    var handler = Delegate.CreateDelegate(handlerType, this, methodInfo);
                    eventInfo.AddEventHandler(reader, handler);
                }

                _readers[type] = reader;
                _mappingNames[type] = mappingName;

                _logger.Info($"Created reader for type {type.Name} with mapping name '{mappingName}'");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to create reader for type {type.Name}");
                throw;
            }
        }

        private void CreateReader<T>(string mappingName, bool enableWrite = false) where T : struct
        {
            var reader = new SharedMemoryReader<T>(mappingName, enableWrite);
            reader.OnInitialConnect += OnReaderInitialConnect;

            Type type = typeof(T);
            _readers[type] = reader;
            _mappingNames[type] = mappingName;

            _logger.Info($"Created reader for type {type.Name} with mapping name '{mappingName}'");
        }

        private SharedMemoryReader<T> GetReader<T>() where T : struct
        {
            Type type = typeof(T);
            if (!_readers.TryGetValue(type, out var readerObj))
            {
                throw new InvalidOperationException(
                    $"No reader registered for type {type.Name}. Add a reader first using AddReader<T>() or via constructor.");
            }

            if (readerObj is SharedMemoryReader<T> reader)
            {
                return reader;
            }

            throw new InvalidOperationException($"Reader for type {type.Name} is not of the expected type.");
        }

        private void OnReaderInitialConnect(object sender, EventArgs e)
        {
            // Find which type's reader this is
            foreach (var kvp in _readers)
            {
                if (kvp.Value == sender)
                {
                    _logger.Info($"Reader for type {kvp.Key.Name} connected to '{_mappingNames[kvp.Key]}'");
                    OnReaderConnected?.Invoke(this, new ReaderConnectedEventArgs(kvp.Key, _mappingNames[kvp.Key]));
                    break;
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// Event arguments for reader connection events
    /// </summary>
    public class ReaderConnectedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the type of data the reader handles
        /// </summary>
        public Type DataType { get; }

        /// <summary>
        /// Gets the mapping name of the connected reader
        /// </summary>
        public string MappingName { get; }

        /// <summary>
        /// Initializes a new instance of ReaderConnectedEventArgs
        /// </summary>
        /// <param name="dataType">The type of data the reader handles</param>
        /// <param name="mappingName">The mapping name of the connected reader</param>
        public ReaderConnectedEventArgs(Type dataType, string mappingName)
        {
            DataType = dataType;
            MappingName = mappingName;
        }
    }

    /// <summary>
    /// Interface for checking connection status
    /// </summary>
    internal interface IConnectable
    {
        bool IsConnected { get; }
    }
}

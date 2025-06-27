/*************************************************************************
 *  Traversify – SafeCoroutine.cs                                        *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-27 04:00:04 UTC                                     *
 *  Desc   : Provides robust error handling for coroutines with detailed *
 *           logging, cancellation support, and performance tracking.    *
 *           Prevents crashes from exceptions in asynchronous operations *
 *           and ensures consistent resource cleanup and error reporting.*
 *************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Traversify.Core {
    /// <summary>
    /// Provides robust error handling for coroutines, preventing crashes from exceptions
    /// in asynchronous operations and ensuring consistent resource cleanup and error reporting.
    /// </summary>
    public static class SafeCoroutine {
        #region Static Utility Methods
        
        /// <summary>
        /// Wraps a coroutine with error handling and returns a new coroutine that can be started.
        /// </summary>
        /// <param name="enumerator">The coroutine to wrap</param>
        /// <param name="onError">Optional callback for error handling</param>
        /// <param name="category">Log category for error reporting</param>
        /// <returns>A wrapped coroutine with error handling</returns>
        public static IEnumerator Wrap(IEnumerator enumerator, Action<Exception> onError = null, LogCategory category = LogCategory.General) {
            bool completed = false;
            Exception exception = null;
            
            // Create a new coroutine that runs the original one and catches any exceptions
            IEnumerator safeEnumerator = RunWithErrorHandling(enumerator, ex => {
                exception = ex;
                completed = true;
            });
            
            // Run the safe coroutine
            while (!completed) {
                try {
                    if (!safeEnumerator.MoveNext()) {
                        completed = true;
                    }
                } catch (Exception ex) {
                    exception = ex;
                    completed = true;
                }
                
                if (!completed) {
                    yield return safeEnumerator.Current;
                }
            }
            
            // Handle any exceptions that occurred
            if (exception != null) {
                LogException(exception, category);
                onError?.Invoke(exception);
            }
        }
        
        /// <summary>
        /// Starts a coroutine with error handling.
        /// </summary>
        /// <param name="monoBehaviour">MonoBehaviour to start the coroutine on</param>
        /// <param name="enumerator">The coroutine to run</param>
        /// <param name="onError">Optional callback for error handling</param>
        /// <param name="category">Log category for error reporting</param>
        /// <returns>Coroutine reference that can be used to stop the coroutine</returns>
        public static Coroutine Start(MonoBehaviour monoBehaviour, IEnumerator enumerator, Action<Exception> onError = null, LogCategory category = LogCategory.General) {
            if (monoBehaviour == null) {
                LogError("Cannot start SafeCoroutine: MonoBehaviour is null", category);
                onError?.Invoke(new ArgumentNullException("monoBehaviour"));
                return null;
            }
            
            // Wrap the coroutine with error handling
            IEnumerator safeEnumerator = Wrap(enumerator, onError, category);
            
            // Start the safe coroutine
            return monoBehaviour.StartCoroutine(safeEnumerator);
        }
        
        /// <summary>
        /// Starts a coroutine with error handling and performance tracking.
        /// </summary>
        /// <param name="monoBehaviour">MonoBehaviour to start the coroutine on</param>
        /// <param name="enumerator">The coroutine to run</param>
        /// <param name="operationName">Name of the operation for performance tracking</param>
        /// <param name="onComplete">Optional callback when the coroutine completes successfully</param>
        /// <param name="onError">Optional callback for error handling</param>
        /// <param name="category">Log category for reporting</param>
        /// <returns>Coroutine reference that can be used to stop the coroutine</returns>
        public static Coroutine StartWithTracking(MonoBehaviour monoBehaviour, IEnumerator enumerator, string operationName, 
                                                 Action onComplete = null, Action<Exception> onError = null, LogCategory category = LogCategory.General) {
            if (monoBehaviour == null) {
                LogError("Cannot start SafeCoroutine: MonoBehaviour is null", category);
                onError?.Invoke(new ArgumentNullException("monoBehaviour"));
                return null;
            }
            
            // Create a wrapped coroutine with performance tracking
            IEnumerator trackedEnumerator = TrackPerformance(enumerator, operationName, onComplete, onError, category);
            
            // Start the tracked coroutine
            return monoBehaviour.StartCoroutine(trackedEnumerator);
        }
        
        /// <summary>
        /// Runs multiple coroutines in parallel and waits for all to complete.
        /// </summary>
        /// <param name="monoBehaviour">MonoBehaviour to start the coroutines on</param>
        /// <param name="enumerators">Collection of coroutines to run</param>
        /// <param name="onAllComplete">Callback when all coroutines complete</param>
        /// <param name="onAnyError">Callback if any coroutine encounters an error</param>
        /// <param name="category">Log category for reporting</param>
        /// <returns>Coroutine reference that can be used to stop all coroutines</returns>
        public static Coroutine StartMultiple(MonoBehaviour monoBehaviour, IEnumerable<IEnumerator> enumerators, 
                                             Action onAllComplete = null, Action<Exception> onAnyError = null, LogCategory category = LogCategory.General) {
            return monoBehaviour.StartCoroutine(RunMultiple(monoBehaviour, enumerators, onAllComplete, onAnyError, category));
        }
        
        /// <summary>
        /// Starts a coroutine with timeout handling.
        /// </summary>
        /// <param name="monoBehaviour">MonoBehaviour to start the coroutine on</param>
        /// <param name="enumerator">The coroutine to run</param>
        /// <param name="timeoutSeconds">Timeout in seconds</param>
        /// <param name="onComplete">Callback when the coroutine completes successfully</param>
        /// <param name="onTimeout">Callback if the coroutine times out</param>
        /// <param name="onError">Callback if the coroutine encounters an error</param>
        /// <param name="category">Log category for reporting</param>
        /// <returns>Coroutine reference that can be used to stop the coroutine</returns>
        public static Coroutine StartWithTimeout(MonoBehaviour monoBehaviour, IEnumerator enumerator, float timeoutSeconds,
                                               Action onComplete = null, Action onTimeout = null, Action<Exception> onError = null, 
                                               LogCategory category = LogCategory.General) {
            return monoBehaviour.StartCoroutine(RunWithTimeout(enumerator, timeoutSeconds, onComplete, onTimeout, onError, category));
        }
        
        #endregion
        
        #region Cancelable Coroutine Support
        
        /// <summary>
        /// Starts a cancelable coroutine with error handling.
        /// </summary>
        /// <param name="monoBehaviour">MonoBehaviour to start the coroutine on</param>
        /// <param name="enumerator">The coroutine function that accepts a CancellationToken</param>
        /// <param name="onComplete">Callback when the coroutine completes successfully</param>
        /// <param name="onError">Callback if the coroutine encounters an error</param>
        /// <param name="category">Log category for reporting</param>
        /// <returns>A handle that can be used to cancel the coroutine</returns>
        public static CancelableCoroutineHandle StartCancelable(MonoBehaviour monoBehaviour, Func<CancellationToken, IEnumerator> enumerator,
                                                              Action onComplete = null, Action<Exception> onError = null, LogCategory category = LogCategory.General) {
            // Create cancellation token source
            CancellationTokenSource cts = new CancellationTokenSource();
            
            // Create coroutine with the token
            IEnumerator coroutine = enumerator(cts.Token);
            
            // Wrap with error handling
            IEnumerator safeCoroutine = WrapCancelable(coroutine, cts.Token, onComplete, onError, category);
            
            // Start the coroutine
            Coroutine handle = monoBehaviour.StartCoroutine(safeCoroutine);
            
            // Return cancelable handle
            return new CancelableCoroutineHandle(handle, cts, monoBehaviour);
        }
        
        /// <summary>
        /// Wraps a coroutine with cancellation support.
        /// </summary>
        private static IEnumerator WrapCancelable(IEnumerator enumerator, CancellationToken token, 
                                                Action onComplete = null, Action<Exception> onError = null, LogCategory category = LogCategory.General) {
            Exception error = null;
            bool completed = false;
            
            // Execute the coroutine without try-catch around yield
            while (!token.IsCancellationRequested) {
                bool hasNext = false;
                try {
                    hasNext = enumerator.MoveNext();
                } catch (OperationCanceledException) {
                    // Cancellation is not an error
                    Log($"Coroutine was canceled", category);
                    break;
                } catch (Exception ex) {
                    error = ex;
                    LogException(ex, category);
                    onError?.Invoke(ex);
                    break;
                }
                
                if (!hasNext) {
                    completed = true;
                    break;
                }
                
                yield return enumerator.Current;
            }
            
            // Handle completion
            if (!token.IsCancellationRequested && completed && error == null) {
                onComplete?.Invoke();
            }
            
            // Check if we were canceled
            if (token.IsCancellationRequested) {
                Log($"Coroutine was canceled", category);
            }
            
            // Handle any cleanup needed
            if (enumerator is IDisposable disposable) {
                try {
                    disposable.Dispose();
                } catch (Exception ex) {
                    LogError($"Error during coroutine cleanup: {ex.Message}", category);
                }
            }
        }
        
        #endregion
        
        #region Implementation Helpers
        
        /// <summary>
        /// Runs a coroutine with error handling.
        /// </summary>
        private static IEnumerator RunWithErrorHandling(IEnumerator enumerator, Action<Exception> onError) {
            bool moveNext;
            
            do {
                object current;
                try {
                    moveNext = enumerator.MoveNext();
                    current = enumerator.Current;
                } catch (Exception ex) {
                    onError?.Invoke(ex);
                    yield break;
                }
                
                if (moveNext) {
                    yield return current;
                }
            } while (moveNext);
        }
        
        /// <summary>
        /// Tracks performance of a coroutine.
        /// </summary>
        private static IEnumerator TrackPerformance(IEnumerator enumerator, string operationName, 
                                                  Action onComplete = null, Action<Exception> onError = null, LogCategory category = LogCategory.General) {
            // Create stopwatch for timing
            Stopwatch stopwatch = Stopwatch.StartNew();
            Log($"Starting operation: {operationName}", category);
            
            Exception error = null;
            
            // Run the coroutine outside of try-catch to avoid yield issues
            yield return Wrap(enumerator, ex => error = ex, category);
            
            // Stop timing
            stopwatch.Stop();
            
            if (error != null) {
                LogError($"Operation '{operationName}' failed after {stopwatch.ElapsedMilliseconds}ms: {error.Message}", category);
                onError?.Invoke(error);
            } else {
                Log($"Operation '{operationName}' completed successfully in {stopwatch.ElapsedMilliseconds}ms", category);
                onComplete?.Invoke();
            }
            
            // Get the debugger instance and log performance metrics if available
            TraversifyDebugger debugger = TraversifyDebugger.Instance;
            if (debugger != null) {
                debugger.Log($"Operation '{operationName}' took {stopwatch.Elapsed.TotalSeconds:F3}s", LogCategory.Performance);
            }
        }
        
        /// <summary>
        /// Runs multiple coroutines in parallel and waits for all to complete.
        /// </summary>
        private static IEnumerator RunMultiple(MonoBehaviour monoBehaviour, IEnumerable<IEnumerator> enumerators,
                                             Action onAllComplete = null, Action<Exception> onAnyError = null, LogCategory category = LogCategory.General) {
            List<Coroutine> coroutines = new List<Coroutine>();
            List<Exception> errors = new List<Exception>();
            bool hasError = false;
            
            // Start all coroutines
            foreach (var enumerator in enumerators) {
                Coroutine coroutine = Start(monoBehaviour, enumerator, ex => {
                    lock (errors) {
                        errors.Add(ex);
                        hasError = true;
                    }
                }, category);
                
                if (coroutine != null) {
                    coroutines.Add(coroutine);
                }
            }
            
            // Wait for all coroutines to complete
            foreach (var coroutine in coroutines) {
                yield return coroutine;
            }
            
            // Handle completion or errors
            if (hasError) {
                // Combine all errors into a single exception
                AggregateException aggregateException = new AggregateException(errors);
                LogError($"One or more coroutines failed: {aggregateException.Message}", category);
                onAnyError?.Invoke(aggregateException);
            } else {
                onAllComplete?.Invoke();
            }
        }
        
        /// <summary>
        /// Runs a coroutine with timeout handling.
        /// </summary>
        private static IEnumerator RunWithTimeout(IEnumerator enumerator, float timeoutSeconds,
                                               Action onComplete = null, Action onTimeout = null, Action<Exception> onError = null, 
                                               LogCategory category = LogCategory.General) {
            // Create done flag and error holder
            bool isDone = false;
            Exception error = null;
            
            // Start the actual coroutine
            IEnumerator coroutineWithFlag = RunWithFlag(enumerator, () => isDone = true, ex => error = ex);
            
            // Start timeout timer
            float startTime = Time.time;
            float endTime = startTime + timeoutSeconds;
            
            // Run the coroutine until it completes or times out
            while (!isDone && Time.time < endTime) {
                yield return coroutineWithFlag;
            }
            
            // Check if we timed out
            if (!isDone) {
                LogWarning($"Coroutine timed out after {timeoutSeconds} seconds", category);
                onTimeout?.Invoke();
            } else if (error != null) {
                LogException(error, category);
                onError?.Invoke(error);
            } else {
                onComplete?.Invoke();
            }
        }
        
        /// <summary>
        /// Runs a coroutine and sets a flag when complete.
        /// </summary>
        private static IEnumerator RunWithFlag(IEnumerator enumerator, Action onComplete, Action<Exception> onError) {
            Exception error = null;
            
            // Run the coroutine without try-catch around yield
            bool hasNext;
            do {
                try {
                    hasNext = enumerator.MoveNext();
                } catch (Exception ex) {
                    error = ex;
                    onError?.Invoke(ex);
                    break;
                }
                
                if (hasNext) {
                    yield return enumerator.Current;
                }
            } while (hasNext);
            
            if (error == null) {
                onComplete?.Invoke();
            }
        }
        
        #endregion
        
        #region Logging Helpers
        
        /// <summary>
        /// Logs a message using TraversifyDebugger if available, otherwise uses Debug.Log.
        /// </summary>
        private static void Log(string message, LogCategory category) {
            TraversifyDebugger debugger = TraversifyDebugger.Instance;
            if (debugger != null) {
                debugger.Log(message, category);
            } else {
                Debug.Log($"[{category}] {message}");
            }
        }
        
        /// <summary>
        /// Logs a warning using TraversifyDebugger if available, otherwise uses Debug.LogWarning.
        /// </summary>
        private static void LogWarning(string message, LogCategory category) {
            TraversifyDebugger debugger = TraversifyDebugger.Instance;
            if (debugger != null) {
                debugger.LogWarning(message, category);
            } else {
                Debug.LogWarning($"[{category}] {message}");
            }
        }
        
        /// <summary>
        /// Logs an error using TraversifyDebugger if available, otherwise uses Debug.LogError.
        /// </summary>
        private static void LogError(string message, LogCategory category) {
            TraversifyDebugger debugger = TraversifyDebugger.Instance;
            if (debugger != null) {
                debugger.LogError(message, category);
            } else {
                Debug.LogError($"[{category}] {message}");
            }
        }
        
        /// <summary>
        /// Logs an exception using TraversifyDebugger if available, otherwise uses Debug.LogException.
        /// </summary>
        private static void LogException(Exception exception, LogCategory category) {
            TraversifyDebugger debugger = TraversifyDebugger.Instance;
            if (debugger != null) {
                debugger.LogError($"Exception: {exception.Message}\n{exception.StackTrace}", category);
            } else {
                Debug.LogError($"[{category}] Exception: {exception.Message}\n{exception.StackTrace}");
            }
        }
        
        #endregion
    }
    
    /// <summary>
    /// Represents a cancelable coroutine handle that can be used to cancel a running coroutine.
    /// </summary>
    public class CancelableCoroutineHandle : IDisposable {
        private Coroutine _coroutine;
        private CancellationTokenSource _cancellationSource;
        private MonoBehaviour _monoBehaviour;
        private bool _disposed;
        
        /// <summary>
        /// Gets whether the coroutine has been canceled.
        /// </summary>
        public bool IsCanceled => _cancellationSource.IsCancellationRequested;
        
        /// <summary>
        /// Creates a new cancelable coroutine handle.
        /// </summary>
        public CancelableCoroutineHandle(Coroutine coroutine, CancellationTokenSource cancellationSource, MonoBehaviour monoBehaviour) {
            _coroutine = coroutine;
            _cancellationSource = cancellationSource;
            _monoBehaviour = monoBehaviour;
            _disposed = false;
        }
        
        /// <summary>
        /// Cancels the coroutine.
        /// </summary>
        public void Cancel() {
            if (_disposed) return;
            
            try {
                // Signal cancellation
                if (!_cancellationSource.IsCancellationRequested) {
                    _cancellationSource.Cancel();
                }
                
                // Stop the coroutine
                if (_coroutine != null && _monoBehaviour != null) {
                    _monoBehaviour.StopCoroutine(_coroutine);
                    _coroutine = null;
                }
            } catch (Exception ex) {
                Debug.LogError($"Error canceling coroutine: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Disposes the handle and cancels the coroutine if it's still running.
        /// </summary>
        public void Dispose() {
            if (_disposed) return;
            
            Cancel();
            
            if (_cancellationSource != null) {
                _cancellationSource.Dispose();
                _cancellationSource = null;
            }
            
            _monoBehaviour = null;
            _disposed = true;
        }
    }
    
    /// <summary>
    /// Extension methods for the SafeCoroutine class.
    /// </summary>
    public static class SafeCoroutineExtensions {
        /// <summary>
        /// Wraps a coroutine with error handling.
        /// </summary>
        /// <param name="monoBehaviour">The MonoBehaviour to run the coroutine on</param>
        /// <param name="enumerator">The coroutine to wrap</param>
        /// <param name="onError">Optional callback for error handling</param>
        /// <param name="category">Log category for error reporting</param>
        /// <returns>A coroutine reference that can be used to stop the coroutine</returns>
        public static Coroutine StartSafeCoroutine(this MonoBehaviour monoBehaviour, IEnumerator enumerator, 
                                                 Action<Exception> onError = null, LogCategory category = LogCategory.General) {
            return SafeCoroutine.Start(monoBehaviour, enumerator, onError, category);
        }
        
        /// <summary>
        /// Wraps a coroutine with error handling and performance tracking.
        /// </summary>
        /// <param name="monoBehaviour">The MonoBehaviour to run the coroutine on</param>
        /// <param name="enumerator">The coroutine to wrap</param>
        /// <param name="operationName">Name of the operation for performance tracking</param>
        /// <param name="onComplete">Optional callback when the coroutine completes successfully</param>
        /// <param name="onError">Optional callback for error handling</param>
        /// <param name="category">Log category for reporting</param>
        /// <returns>A coroutine reference that can be used to stop the coroutine</returns>
        public static Coroutine StartTrackedCoroutine(this MonoBehaviour monoBehaviour, IEnumerator enumerator, string operationName,
                                                    Action onComplete = null, Action<Exception> onError = null, LogCategory category = LogCategory.General) {
            return SafeCoroutine.StartWithTracking(monoBehaviour, enumerator, operationName, onComplete, onError, category);
        }
        
        /// <summary>
        /// Starts a coroutine with timeout handling.
        /// </summary>
        /// <param name="monoBehaviour">The MonoBehaviour to run the coroutine on</param>
        /// <param name="enumerator">The coroutine to run</param>
        /// <param name="timeoutSeconds">Timeout in seconds</param>
        /// <param name="onComplete">Callback when the coroutine completes successfully</param>
        /// <param name="onTimeout">Callback if the coroutine times out</param>
        /// <param name="onError">Callback if the coroutine encounters an error</param>
        /// <param name="category">Log category for reporting</param>
        /// <returns>A coroutine reference that can be used to stop the coroutine</returns>
        public static Coroutine StartCoroutineWithTimeout(this MonoBehaviour monoBehaviour, IEnumerator enumerator, float timeoutSeconds,
                                                        Action onComplete = null, Action onTimeout = null, Action<Exception> onError = null,
                                                        LogCategory category = LogCategory.General) {
            return SafeCoroutine.StartWithTimeout(monoBehaviour, enumerator, timeoutSeconds, onComplete, onTimeout, onError, category);
        }
        
        /// <summary>
        /// Starts a cancelable coroutine with error handling.
        /// </summary>
        /// <param name="monoBehaviour">The MonoBehaviour to run the coroutine on</param>
        /// <param name="enumerator">The coroutine function that accepts a CancellationToken</param>
        /// <param name="onComplete">Callback when the coroutine completes successfully</param>
        /// <param name="onError">Callback if the coroutine encounters an error</param>
        /// <param name="category">Log category for reporting</param>
        /// <returns>A handle that can be used to cancel the coroutine</returns>
        public static CancelableCoroutineHandle StartCancelableCoroutine(this MonoBehaviour monoBehaviour, Func<CancellationToken, IEnumerator> enumerator,
                                                                       Action onComplete = null, Action<Exception> onError = null, 
                                                                       LogCategory category = LogCategory.General) {
            return SafeCoroutine.StartCancelable(monoBehaviour, enumerator, onComplete, onError, category);
        }
    }
}
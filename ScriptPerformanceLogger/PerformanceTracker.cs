﻿namespace Skyline.DataMiner.Utils.ScriptPerformanceLogger
{
	using System;
	using System.Collections.Concurrent;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Linq;
	using System.Reflection;
	using System.Runtime.CompilerServices;
	using System.Threading;

	using Skyline.DataMiner.Utils.ScriptPerformanceLogger.Models;

	/// <summary>
	/// <see cref="PerformanceTracker"/> tracks method calls in single or multi threaded environments.
	/// </summary>
	public sealed class PerformanceTracker : IDisposable
	{
		private static readonly ConcurrentDictionary<int, Stack<PerformanceData>> _perThreadStack = new ConcurrentDictionary<int, Stack<PerformanceData>>();

		private readonly bool _isMultiThreaded;
		private readonly PerformanceCollector _collector;
		private readonly int _threadId = Thread.CurrentThread.ManagedThreadId;

		private PerformanceData _trackedMethod;
		private bool _disposed;
		private bool _isStarted;
		private bool _isCompleted;
		private bool _isSubMethod;

		/// <summary>
		/// Initializes a new instance of the <see cref="PerformanceTracker"/> class.
		/// </summary>
		/// <param name="collector"><see cref="PerformanceCollector"/> to use.</param>
		/// <exception cref="ArgumentNullException">Throws if <paramref name="collector"/> is null.</exception>
		public PerformanceTracker(PerformanceCollector collector) : this()
		{
			_collector = collector ?? throw new ArgumentNullException(nameof(collector));

			Start(_threadId);
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="PerformanceTracker"/> class.
		/// </summary>
		/// <param name="collector"><see cref="PerformanceCollector"/> to use.</param>
		/// <param name="className">Name of the class from which a method is tracked.</param>
		/// <param name="methodName">Name of the method that is tracked.</param>
		/// <exception cref="ArgumentNullException">Throws if <paramref name="collector"/> is null.</exception>
		public PerformanceTracker(PerformanceCollector collector, string className, string methodName) : this()
		{
			_collector = collector ?? throw new ArgumentNullException(nameof(collector));
			if (string.IsNullOrWhiteSpace(className))
			{
				throw new ArgumentNullException(nameof(className));
			}

			if (string.IsNullOrWhiteSpace(methodName))
			{
				throw new ArgumentNullException(nameof(methodName));
			}

			Start(className, methodName, _threadId);
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="PerformanceTracker"/> class and starts performance tracking for the method in which it was initialized.
		/// </summary>
		/// <param name="parentPerformanceTracker">Parent <see cref="PerformanceTracker"/> of the new instance. This controls the nesting of the <see cref="PerformanceData"/> for methods in multithreaded use cases.</param>
		/// <exception cref="ArgumentNullException">Throws if parent <paramref name="parentPerformanceTracker"/> is null.</exception>
		public PerformanceTracker(PerformanceTracker parentPerformanceTracker) : this()
		{
			_collector = parentPerformanceTracker?.Collector ?? throw new ArgumentNullException(nameof(parentPerformanceTracker));

			PerformanceData methodData = Start(parentPerformanceTracker._threadId);
			methodData.Parent = parentPerformanceTracker._trackedMethod;

			if (Thread.CurrentThread.ManagedThreadId != parentPerformanceTracker._threadId && !_isSubMethod)
			{
				parentPerformanceTracker._trackedMethod.SubMethods.Add(methodData);
				_isSubMethod = true;
			}
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="PerformanceTracker"/> class and starts performance tracking for the method in which it was initialized.
		/// </summary>
		/// <param name="parentPerformanceTracker">Parent <see cref="PerformanceTracker"/> of the new instance. This controls the nesting of the <see cref="PerformanceData"/> for methods in multithreaded use cases.</param>
		/// <param name="className">Name of the class from which a method is tracked.</param>
		/// <param name="methodName">Name of the method that is tracked.</param>
		/// <exception cref="ArgumentNullException">Throws if parent <paramref name="parentPerformanceTracker"/> is null.</exception>
		public PerformanceTracker(PerformanceTracker parentPerformanceTracker, string className, string methodName) : this()
		{
			_collector = parentPerformanceTracker?.Collector ?? throw new ArgumentNullException(nameof(parentPerformanceTracker));

			if (string.IsNullOrWhiteSpace(className))
			{
				throw new ArgumentNullException(nameof(className));
			}

			if (string.IsNullOrWhiteSpace(methodName))
			{
				throw new ArgumentNullException(nameof(methodName));
			}

			PerformanceData methodData = Start(className, methodName, parentPerformanceTracker._threadId);
			methodData.Parent = parentPerformanceTracker._trackedMethod;

			if (Thread.CurrentThread.ManagedThreadId != parentPerformanceTracker._threadId && !_isSubMethod)
			{
				parentPerformanceTracker._trackedMethod.SubMethods.Add(methodData);
				_isSubMethod = true;
			}
		}

		private PerformanceTracker()
		{
			if (_perThreadStack.TryAdd(Thread.CurrentThread.ManagedThreadId, new Stack<PerformanceData>()))
			{
				_isMultiThreaded = _perThreadStack.Count > 1;
			}
		}

		/// <summary>
		/// Gets underlying <see cref="PerformanceCollector"/>.
		/// </summary>
		/// <exception cref="InvalidOperationException">Throws if collector is not initialized yet.</exception>
		public PerformanceCollector Collector => _collector;

		/// <summary>
		/// Gets <see cref="PerformanceData"/> of the tracked method.
		/// </summary>
		/// <exception cref="InvalidOperationException">Throws if tracked method is not initialized yet.</exception>
		public PerformanceData TrackedMethod => _trackedMethod ?? throw new InvalidOperationException(nameof(_trackedMethod));

		/// <summary>
		/// Gets elapsed time since the initialization of the underlying <see cref="PerformanceCollector"/>.
		/// </summary>
		/// <exception cref="InvalidOperationException">Throws if collector is not initialized yet.</exception>
		public TimeSpan Elapsed
		{
			get
			{
				if (_trackedMethod == null)
				{
					throw new InvalidOperationException("Performance tracking not started, call Start.");
				}

				return _collector.Clock.UtcNow - _trackedMethod.StartTime;
			}
		}

		private Stack<PerformanceData> Stack => _perThreadStack[_threadId];

		/// <summary>
		/// Adds metadata for the tracked method.
		/// </summary>
		/// <param name="key">Key of the metadata.</param>
		/// <param name="value">Value of the metadata.</param>
		/// <returns>Returns current instance of <see cref="PerformanceTracker"/>.</returns>
		public PerformanceTracker AddMetadata(string key, string value)
		{
			_trackedMethod.Metadata[key] = value;
			return this;
		}

		/// <summary>
		/// Adds metadata for the tracked method.
		/// </summary>
		/// <param name="metadata">Metadata to add or update.</param>
		/// <returns>Returns current instance of <see cref="PerformanceTracker"/>.</returns>
		public PerformanceTracker AddMetadata(IReadOnlyDictionary<string, string> metadata)
		{
			foreach (var data in metadata)
			{
				_trackedMethod.Metadata[data.Key] = data.Value;
			}

			return this;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private PerformanceData Start(int parentThreadId)
		{
			MethodBase methodMemberInfo = new StackTrace().GetFrames()?.Where(frame => frame.GetMethod().Name != ".ctor").Skip(1).FirstOrDefault()?.GetMethod() ?? throw new InvalidOperationException("Unable to retrieve the stack information.");
			string className = methodMemberInfo.DeclaringType.Name;
			string methodName = methodMemberInfo.Name;

			return Start(className, methodName, parentThreadId);
		}

		private PerformanceData Start(string className, string methodName, int threadId)
		{
			if (_isStarted)
			{
				return Stack.Peek();
			}

			var methodData = new PerformanceData(className, methodName);

			if (Stack.Any())
			{
				Stack.Peek().SubMethods.Add(methodData);
				methodData.Parent = Stack.Peek();
				_isSubMethod = true;
			}

			Stack.Push(_collector.Start(methodData, threadId));

			_trackedMethod = methodData;
			_isStarted = true;

			return methodData;
		}

		private void End()
		{
			if (_trackedMethod == null)
			{
				throw new InvalidOperationException(nameof(_trackedMethod));
			}

			if (_isCompleted)
			{
				return;
			}

			if (Stack.Any())
			{
				_collector.Stop(Stack.Pop());
				_isCompleted = true;
			}
		}

		/// <summary>
		/// Completes performance tracking of the method and adds the data to the collector for logging.
		/// </summary>
#pragma warning disable SA1202 // Elements should be ordered by access
		public void Dispose()
#pragma warning restore SA1202 // Elements should be ordered by access
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		private void Dispose(bool disposing)
		{
			if (!_disposed && disposing)
			{
				End();

				if (!Stack.Any())
				{
					if (_isMultiThreaded)
					{
						_perThreadStack.TryRemove(Thread.CurrentThread.ManagedThreadId, out _);
					}

					_collector.Dispose();

					_disposed = true;
				}
			}
		}
	}
}
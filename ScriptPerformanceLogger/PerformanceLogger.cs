﻿namespace Skyline.DataMiner.Utils.ScriptPerformanceLogger
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;
	using System.Runtime.CompilerServices;

	using Newtonsoft.Json;

	public class PerformanceLogger
	{
		const string DirectoryPath = @"C:\Skyline_Data\ScriptPerformanceLogger";

		private readonly Stack<MethodInvocation> _runningMethods = new Stack<MethodInvocation>();

		public Result Result { get; private set; } = new Result();

		public void SetProperty(string name, string value)
		{
			Result.Properties[name] = value;
		}

		// no inlining to make sure the retrieved method name is correct
		[MethodImpl(MethodImplOptions.NoInlining)]
		public Measurement StartMeasurement()
		{
			var methodBase = new StackTrace().GetFrame(1).GetMethod();
			var className = methodBase.ReflectedType?.FullName;

			return StartMeasurement(className, methodBase.Name);
		}

		public Measurement StartMeasurement(string className, string methodName)
		{
			var invocation = StartMethodCallMetric(className, methodName);
			var measurement = new Measurement(this, invocation);

			return measurement;
		}

		public void RegisterResult(MethodInvocation methodInvocation)
		{
			if (methodInvocation == null)
				throw new ArgumentNullException(nameof(methodInvocation));

			if (_runningMethods.Count > 0)
			{
				_runningMethods.Peek().ChildInvocations.Add(methodInvocation);
			}
			else
			{
				Result.MethodInvocations.Add(methodInvocation);
			}
		}

		public void RegisterResult(string className, string methodName, DateTime timeStamp, TimeSpan executionTime)
		{
			RegisterResult(new MethodInvocation(className, methodName, timeStamp, executionTime));
		}

		/// <summary>Moves results from memory to file.</summary>
		/// <param name="title">Will be used to create the file name.</param>
		/// <exception cref="ArgumentException">When <paramref name="title"/> would violate file path constraints.</exception>
		/// <exception cref="SystemException">When writing the file fails.</exception>
		public void PerformCleanUpAndStoreResult(string title)
		{
			var result = PerformCleanupAndReturn();
			if (result == null)
			{
				return;
			}

			Store(result, title);
		}

		internal Result PerformCleanupAndReturn()
		{
			var result = Result;
			Result = new Result();
			return result;
		}

		private void Store(Result result, string title)
		{
			// todo get rid of old results?

			Directory.CreateDirectory(DirectoryPath);

			var fileName = $"{DateTime.UtcNow:yyyy-MM-dd hh-mm-ss.fff}_{title ?? "Untitled"}.json";

			using (var fileStream = File.CreateText(Path.Combine(DirectoryPath, fileName)))
			{
				var jsonSerializer = new JsonSerializer
				{
					NullValueHandling = NullValueHandling.Include,
					ReferenceLoopHandling = ReferenceLoopHandling.Serialize,
					DateFormatHandling = DateFormatHandling.IsoDateFormat,
				};

				jsonSerializer.Serialize(fileStream, result);
			}
		}

		private MethodInvocation StartMethodCallMetric(string className, string methodName)
		{
			var invocation = new MethodInvocation(className, methodName);

			if (_runningMethods.Count > 0)
			{
				_runningMethods.Peek().ChildInvocations.Add(invocation);
			}

			_runningMethods.Push(invocation);

			return invocation;
		}

		internal void CompleteMethodCallMetric(Measurement measurement)
		{
			var runningMethodInvocation = _runningMethods.Pop();

			if (runningMethodInvocation != measurement.Invocation)
			{
				throw new InvalidOperationException("Result of incorrect invocation received!");
			}

			if (_runningMethods.Count == 0)
			{
				RegisterResult(runningMethodInvocation);
			}
		}
	}
}
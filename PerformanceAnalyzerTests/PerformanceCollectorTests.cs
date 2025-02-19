﻿namespace Skyline.DataMiner.Utils.PerformanceAnalyzerTests
{
	using System;
	using System.Collections.Generic;
	using System.Threading;
	using System.Threading.Tasks;

	using Microsoft.VisualStudio.TestTools.UnitTesting;
	using Moq;

	using Skyline.DataMiner.Utils.PerformanceAnalyzer;
	using Skyline.DataMiner.Utils.PerformanceAnalyzer.Loggers;
	using Skyline.DataMiner.Utils.PerformanceAnalyzer.Models;

	[TestClass]
	public class PerformanceCollectorTests
	{
		private Mock<IPerformanceLogger> mockLogger;
		private PerformanceCollector collector;

		[TestInitialize]
		public void Setup()
		{
			mockLogger = new Mock<IPerformanceLogger>();
			collector = new PerformanceCollector(mockLogger.Object);
		}

		[TestMethod]
		public void PerformanceCollector_Start_ShouldSetStartTime()
		{
			// Arrange
			var methodData = new PerformanceData();

			// Act
			var result = collector.Start(methodData, Thread.CurrentThread.ManagedThreadId);

			// Assert
			Assert.IsTrue(result.IsStarted);
			Assert.AreNotEqual(default(DateTime), result.StartTime);
		}

		[TestMethod]
		public void PerformanceCollector_Start_ShouldSetIsStarted()
		{
			// Arrange
			var methodData = new PerformanceData();

			// Act
			var result = collector.Start(methodData, Thread.CurrentThread.ManagedThreadId);

			// Assert
			Assert.IsTrue(result.IsStarted);
		}

		[TestMethod]
		public void PerformanceCollector_Start_ShouldNotOverrideAlreadyStartedMethod()
		{
			// Arrange
			var methodData = new PerformanceData { IsStarted = true, StartTime = DateTime.UtcNow.AddSeconds(-10) };

			// Act
			var result = collector.Start(methodData, Thread.CurrentThread.ManagedThreadId);

			// Assert
			Assert.AreEqual(methodData.StartTime, result.StartTime);
		}

		[TestMethod]
		public void PerformanceCollector_Stop_ShouldSetExecutionTime()
		{
			// Arrange
			var methodData = new PerformanceData { StartTime = DateTime.UtcNow };

			// Act
			var result = collector.Stop(methodData);

			// Assert
			Assert.AreNotEqual(default(TimeSpan), result.ExecutionTime);
		}

		[TestMethod]
		public void PerformanceCollector_Stop_ShouldSetIsStopped()
		{
			// Arrange
			var methodData = new PerformanceData { StartTime = DateTime.UtcNow };

			// Act
			var result = collector.Stop(methodData);

			// Assert
			Assert.IsTrue(result.IsStopped);
		}

		[TestMethod]
		public void PerformanceCollector_Stop_ShouldNotModifyAlreadyStoppedMethod()
		{
			// Arrange
			var methodData = new PerformanceData { IsStopped = true, StartTime = DateTime.UtcNow.AddSeconds(-10) };

			// Act
			var result = collector.Stop(methodData);

			// Assert
			Assert.AreEqual(methodData.ExecutionTime, result.ExecutionTime);
		}

		[TestMethod]
		public void PerformanceCollector_Dispose_ShouldLogMethodsAndClear()
		{
			// Arrange
			var methodData = new PerformanceData();
			collector.Start(methodData, Thread.CurrentThread.ManagedThreadId);

			// Act
			collector.Dispose();

			// Assert
			mockLogger.Verify(l => l.Report(It.IsAny<List<PerformanceData>>()), Times.Once);
		}

		[TestMethod]
		public void PerformanceCollector_Dispose_ShouldNotLogMethodsWhileOtherThreadsAreRunning()
		{
			// Arrange
			var methodData = new PerformanceData();
			collector.Start(methodData, Thread.CurrentThread.ManagedThreadId);

			Task.Run(() =>
			{
				collector.Start(new PerformanceData(), Thread.CurrentThread.ManagedThreadId);

				Thread.Sleep(200);
			});

			// Act
			Thread.Sleep(100);
			collector.Dispose();

			// Assert
			mockLogger.Verify(l => l.Report(It.IsAny<List<PerformanceData>>()), Times.Never);
		}

		[TestMethod]
		public void PerformanceCollector_ParallelDispose_ShouldWork()
		{
			// Arrange
			var list = new List<int>();
			for (int i = 0; i < 1000; i++)
			{
				list.Add(i);
			}

			// Act
			for (int i = 0; i < 30; i++)
			{
				using (var parentTracker = new PerformanceTracker(collector))
				{
					Parallel.ForEach(list, x =>
					{
						using (new PerformanceTracker(parentTracker))
						{
							Thread.Sleep(2);
						}
					});
				}
			}

			// Assert
			mockLogger.Verify(l => l.Report(It.IsAny<List<PerformanceData>>()), Times.Exactly(30));
		}
	}
}

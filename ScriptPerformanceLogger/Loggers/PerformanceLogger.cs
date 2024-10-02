﻿namespace Skyline.DataMiner.Utils.ScriptPerformanceLogger.Loggers
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Text;

	using Newtonsoft.Json;
	using Skyline.DataMiner.Net.Helper;
	using Skyline.DataMiner.Utils.ScriptPerformanceLogger.Models;
	using Skyline.DataMiner.Utils.ScriptPerformanceLogger.Tools;

	public class PerformanceLogger : IPerformanceLogger
	{
		private const string DirectoryPath = @"C:\Skyline_Data\PerformanceLogger";
		private static readonly JsonSerializerSettings _jsonSerializerSettings = new JsonSerializerSettings
		{
			NullValueHandling = NullValueHandling.Ignore,
			ReferenceLoopHandling = ReferenceLoopHandling.Serialize,
			DateFormatHandling = DateFormatHandling.IsoDateFormat,
		};

		private static readonly object _fileLock = new object();

		public PerformanceLogger(string fileName, string filePath = DirectoryPath) : this(new LogFileInfo(fileName, filePath))
		{
		}

		public PerformanceLogger(params LogFileInfo[] logFileInfo)
		{
			logFileInfo.ForEach(x => LogFiles.Add(x));
		}

		public List<LogFileInfo> LogFiles { get; private set; } = new List<LogFileInfo>();

		public bool IncludeDate { get; set; } = false;

		public void Report(List<PerformanceData> data)
		{
			Retry.Execute(
				() => Store(data),
				TimeSpan.FromMilliseconds(100),
				tryCount: 10);
		}

		private static long GetStartPosition(FileStream fileStream)
		{
			char searchChar = ']';
			bool charFoundOnce = false;

			long position = fileStream.Length - 1;

			while (position >= 0)
			{
				fileStream.Seek(position, SeekOrigin.Begin);

				int currentByte = fileStream.ReadByte();

				if (currentByte == -1)
				{
					return 0;
				}

				if ((char)currentByte == searchChar)
				{
					if (!charFoundOnce)
					{
						charFoundOnce = true;
					}
					else
					{
						return position + 1;
					}
				}

				position--;
			}

			return 0;
		}

		private void Store(List<PerformanceData> data)
		{
			LogFiles.ForEach(logFile => Store(data, logFile));
		}

		private void Store(List<PerformanceData> data, LogFileInfo logFileInfo)
		{
			Directory.CreateDirectory(logFileInfo.FilePath);

			string fileName = BuildFileName(logFileInfo);
			string fullPath = Path.Combine(logFileInfo.FilePath, fileName);

			lock (_fileLock)
			{
				using (var fileStream = new FileStream(fullPath, FileMode.OpenOrCreate))
				{
					fileStream.Position = GetStartPosition(fileStream);

					using (var writer = new StreamWriter(fileStream))
					{
						string prefix = fileStream.Position == 0 ? "[" : ",";
						string postfix = "]";

						IEnumerable<PerformanceData> dataToSerialize = data.Where(d => d != null);

						if (dataToSerialize.Any())
						{
							writer.Write(prefix + JsonConvert.SerializeObject(dataToSerialize, _jsonSerializerSettings) + postfix);
						}
					}
				}
			}
		}

		private string BuildFileName(LogFileInfo logFileInfo)
		{
			var sb = new StringBuilder();

			if (IncludeDate)
			{
				sb.Append($"{DateTime.UtcNow:yyyy-MM-dd hh-mm-ss.fff}_");
			}

			sb.Append($"{logFileInfo.FileName}.json");

			return sb.ToString();
		}
	}

	public class LogFileInfo
	{
		public LogFileInfo(string fileName, string filePath)
		{
			if (String.IsNullOrWhiteSpace(fileName))
			{
				throw new ArgumentException(nameof(fileName));
			}

			if (String.IsNullOrWhiteSpace(filePath))
			{
				throw new ArgumentException(nameof(filePath));
			}

			FileName = fileName;
			FilePath = filePath;
		}

		public string FileName { get; private set; }

		public string FilePath { get; private set; }
	}
}
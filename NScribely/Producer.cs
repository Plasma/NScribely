﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using NScribely.Scribe;
using Thrift.Protocol;
using Thrift.Transport;
using Timer = System.Timers.Timer;

namespace NScribely
{
	public delegate void ProducerQueueFlushedEventHandler(object sender, EventArgs e);

	public class Producer
	{
		public const int DefaultFlushInterval = 200;

		public Producer(string host, int port, int flushInterval = DefaultFlushInterval)
		{
			if (flushInterval < 1)
			{
				throw new ArgumentException("not a positive integer", "flushInterval");
			}

			Host = host;
			Port = port;
			Queue = Queue.Synchronized(new Queue());
			Timer = new Timer
				{
					Interval = flushInterval,
					Enabled = true
				};

			Timer.Elapsed += (sender, args) => FlushQueue();
		}

		public string Host { get; private set; }

		public int Port { get; private set; }

		private Queue Queue { get; set; }

		private Timer Timer { get; set; }

		public event ProducerQueueFlushedEventHandler QueueFlushed = (sender, args) => { };

		public Producer Send(string category, string message)
		{
			Queue.Enqueue(new Item
				{
					Category = category,
					Message = message
				});

			return this;
		}

		[MethodImplAttribute(MethodImplOptions.Synchronized)]
		private void FlushQueue()
		{
			var socket = new TSocket(Host, Port);
			var transport = new TFramedTransport(socket);
			var protocol = new TBinaryProtocol(transport, false, false);

			transport.Open();

			var client = new ScribeClient.Client(protocol);
			var retry = new List<Item>();

			while (Queue.Count > 0)
			{
				Item item;

				try
				{
					item = Queue.Dequeue() as Item;
				}
				catch (InvalidOperationException)
				{
					// No item available, while loop will ensure we fall out after this
					continue;
				}

				if (item == null)
				{
					continue;
				}

				try
				{
					client.Log(new List<LogEntry>
						{
							new LogEntry
								{
									Category = item.Category,
									Message = item.Message
								}
						});
				}
				catch (Exception)
				{
					// Retry next iteration
					retry.Add(item);
				}
			}

			transport.Close();

			// Add to the queue items to retry
			foreach (var item in retry)
			{
				Queue.Enqueue(item);
			}

			QueueFlushed(this, EventArgs.Empty);
		}
	}
}
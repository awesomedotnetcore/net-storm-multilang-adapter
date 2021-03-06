﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using Dotnet.Storm.Adapter.Channels;
using Dotnet.Storm.Adapter.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;

namespace Dotnet.Storm.Adapter.Components
{
    public abstract class BaseSpout : Component
    {
        #region Private part
        private static Random random = new Random();

        private const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";

        private MemoryCache PendingQueue = MemoryCache.Default;

        private CacheItemPolicy policy;

        internal override void Start()
        {
            Logger.Info($"Starting spout: {Context.ComponentId}.");

            policy = new CacheItemPolicy()
            {
                SlidingExpiration = new TimeSpan(0, 0, Timeout)
            };

            while (true)
            {
                InMessage message = Channel.Receive<CommandMessage>();
                if (message != null)
                {
                    // there are only two options: task_ids and command
                    if (message is TaskIdsMessage ids)
                    {
                        RiseTaskIds(new TaskIds(ids));
                    }
                    else
                    {
                        CommandMessage command = (CommandMessage)message;

                        switch (command.Command)
                        {
                            case "next":
                                DoNext();
                                break;
                            case "ack":
                                DoAck(command.Id);
                                break;
                            case "fail":
                                DoFail(command.Id);
                                break;
                            case "activate":
                                DoActivate();
                                break;
                            case "deactivate":
                                DoDeactivate();
                                break;
                        }

                    }
                    DoSync();
                }
            }
        }

        private void DoSync()
        {
            if (IsEnabled)
            {
                Sync();
            }
        }

        private void DoNext()
        {
            if (IsEnabled)
            {
                Next();
            }
        }

        private void DoAck(string id)
        {
            if (IsEnabled)
            {
                if (PendingQueue.Contains(id))
                {
                    PendingQueue.Remove(id);
                }
                else
                {
                    Logger.Warn($"Fail to ack message. Pending queue doesn't contain message: {id}.");
                }
            }
        }

        private void DoFail(string id)
        {
            if (IsEnabled)
            {
                if (PendingQueue.Contains(id))
                {
                    if (PendingQueue[id] is SpoutTuple message)
                    {
                        Channel.Send(message);
                    }
                }
                else
                {
                    Logger.Warn($"Fail to resend message. Pending queue doesn't contain message: {id}.");
                }
            }
        }

        private void DoActivate()
        {
            if (!IsEnabled)
            {
                try
                {
                    OnActivate?.Invoke(this, EventArgs.Empty);

                    IsEnabled = true;
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to activate component.", ex);
                }
            }
        }

        private void DoDeactivate()
        {
            if (IsEnabled)
            {
                try
                {
                    OnDeactivate?.Invoke(this, EventArgs.Empty);

                    IsEnabled = true;
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to deactivate component.", ex);
                }
            }
        }

        private static string NextId()
        {
            return "id" + new string(Enumerable.Repeat(chars, 6).Select(s => s[random.Next(s.Length)]).ToArray());
        }
        #endregion

        #region Spout interface
        protected event EventHandler OnActivate;

        protected event EventHandler OnDeactivate;

        protected bool IsEnabled { get; private set; } = false;

        protected void Emit(List<object> tuple, string stream = "default", long task = 0, bool needTaskIds = false)
        {
            string id = null;
            if (IsGuaranteed)
            {
                id = NextId();
            }

            VerificationResult result = VerifyOutput(stream, tuple);

            if (result.IsError)
            {
                Logger.Error($"{result} for next tuple: {tuple}.");
            }
            else
            {
                SpoutTuple message = new SpoutTuple()
                {
                    Id = id,
                    Task = task,
                    Stream = stream,
                    Tuple = tuple,
                    NeedTaskIds = needTaskIds
                };

                Channel.Send(message);

                if (id != null && message != null && policy != null)
                    PendingQueue.Set(id, message, policy);
            }
        }

        public abstract void Next();
        #endregion
    }
}

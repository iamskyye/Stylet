﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Stylet
{
    /// <summary>
    /// Marker for types which we might be interested in
    /// </summary>
    public interface IHandle
    {
    }

    /// <summary>
    /// Implement this to handle a particular message type
    /// </summary>
    /// <typeparam name="TMessageType">Message type to handle. Can be a base class of the messsage type(s) to handle</typeparam>
    public interface IHandle<TMessageType> : IHandle
    {
        /// <summary>
        /// Called whenever a message of type TMessageType is posted
        /// </summary>
        /// <param name="message">Message which was posted</param>
        void Handle(TMessageType message);
    }

    /// <summary>
    /// Centralised, weakly-binding publish/subscribe event manager
    /// </summary>
    public interface IEventAggregator
    {
        /// <summary>
        /// Register an instance as wanting to receive events. Implement IHandle{T} for each event type you want to receive.
        /// </summary>
        /// <param name="handler">Instance that will be registered with the EventAggregator</param>
        /// <param name="channels">Channel(s) which should be subscribed to. Defaults to EventAggregator.DefaultChannel if none given</param>
        void Subscribe(IHandle handler, params string[] channels);

        /// <summary>
        /// Unregister as wanting to receive events. The instance will no longer receive events after this is called.
        /// </summary>
        /// <param name="handler">Instance to unregister</param>
        /// <param name="channels">Channel(s) to unsubscribe from. Unsubscribes from everything if no channels given</param>
        void Unsubscribe(IHandle handler, params string[] channels);

        /// <summary>
        /// Publish an event to all subscribers, using the specified dispatcher
        /// </summary>
        /// <param name="message">Event to publish</param>
        /// <param name="dispatcher">Dispatcher to use to call each subscriber's handle method(s)</param>
        /// <param name="channels">Channel(s) to publish the message to. Defaults to EventAggregator.DefaultChannel none given</param>
        void PublishWithDispatcher(object message, Action<Action> dispatcher, params string[] channels);
    }

    /// <summary>
    /// Default implementation of IEventAggregator
    /// </summary>
    public class EventAggregator : IEventAggregator
    {
        /// <summary>
        /// Channel which handlers are subscribed to / messages are published to, if no channels are named
        /// </summary>
        public static readonly string DefaultChannel = "DefaultChannel";

        private readonly List<Handler> handlers = new List<Handler>();
        private readonly object handlersLock = new object();

        /// <summary>
        /// Register an instance as wanting to receive events. Implement IHandle{T} for each event type you want to receive.
        /// </summary>
        /// <param name="handler">Instance that will be registered with the EventAggregator</param>
        /// <param name="channels">Channel(s) which should be subscribed to. Defaults to EventAggregator.DefaultChannel if none given</param>
        public void Subscribe(IHandle handler, params string[] channels)
        {
            lock (this.handlersLock)
            {
                // Is it already subscribed?
                var subscribed = this.handlers.FirstOrDefault(x => x.IsHandlerForInstance(handler));
                if (subscribed == null)
                    this.handlers.Add(new Handler(handler, channels)); // Adds default topic if appropriate
                else
                    subscribed.SubscribeToChannels(channels);
            }
        }

        /// <summary>
        /// Unregister as wanting to receive events. The instance will no longer receive events after this is called.
        /// </summary>
        /// <param name="handler">Instance to unregister</param>
        /// <param name="channels">Channel(s) to unsubscribe from. Unsubscribes from everything if no channels given</param>
        public void Unsubscribe(IHandle handler, params string[] channels)
        {
            lock (this.handlersLock)
            {
                var existingHandler = this.handlers.FirstOrDefault(x => x.IsHandlerForInstance(handler));
                if (existingHandler != null)
                {
                    if (existingHandler.UnsubscribeFromChannels(channels)) // Handles default topic appropriately
                        this.handlers.Remove(existingHandler);
                }
                    
            }
        }

        /// <summary>
        /// Publish an event to all subscribers, using the specified dispatcher
        /// </summary>
        /// <param name="message">Event to publish</param>
        /// <param name="dispatcher">Dispatcher to use to call each subscriber's handle method(s)</param>
        /// <param name="channels">Channel(s) to publish the message to. Defaults to EventAggregator.DefaultChannel none given</param>
        public void PublishWithDispatcher(object message, Action<Action> dispatcher, params string[] channels)
        {
            lock (this.handlersLock)
            {
                var messageType = message.GetType();
                var deadHandlers = this.handlers.Where(x => !x.Handle(messageType, message, dispatcher, channels)).ToArray();
                foreach (var deadHandler in deadHandlers)
                {
                    this.handlers.Remove(deadHandler);
                }
            }
        }

        private class Handler
        {
            private readonly WeakReference target;
            private readonly List<HandlerInvoker> invokers = new List<HandlerInvoker>();
            private HashSet<string> channels = new HashSet<string>();

            public Handler(object handler, string[] channels)
            {
                var handlerType = handler.GetType();
                this.target = new WeakReference(handler);

                foreach (var implementation in handler.GetType().GetInterfaces().Where(x => x.IsGenericType && typeof(IHandle).IsAssignableFrom(x)))
                {
                    var messageType = implementation.GetGenericArguments()[0];
                    this.invokers.Add(new HandlerInvoker(handlerType, messageType, implementation.GetMethod("Handle")));
                }

                if (channels.Length == 0)
                    channels = new[] { EventAggregator.DefaultChannel };
                this.SubscribeToChannels(channels);
            }

            public bool IsHandlerForInstance(object subscriber)
            {
                return this.target.Target == subscriber;
            }

            public void SubscribeToChannels(string[] channels)
            {
                this.channels.UnionWith(channels);
            }

            public bool UnsubscribeFromChannels(string[] channels)
            {
                // If channels is empty, unsubscribe from everything
                if (channels.Length == 0)
                    return true;
                this.channels.ExceptWith(channels);
                return this.channels.Count == 0;
            }

            public bool Handle(Type messageType, object message, Action<Action> dispatcher, string[] channels)
            {
                var target = this.target.Target;
                if (target == null)
                    return false;

                if (channels.Length == 0)
                    channels = new[] { EventAggregator.DefaultChannel };

                // We're not subscribed to any of the channels
                if (!channels.All(x => this.channels.Contains(x)))
                    return true;

                foreach (var invoker in this.invokers)
                {
                    invoker.Invoke(target, messageType, message, dispatcher);
                }

                return true;
            }
        }

        private class HandlerInvoker
        {
            private readonly Type messageType;
            private readonly Action<object, object> invoker;

            public HandlerInvoker(Type targetType, Type messageType, MethodInfo invocationMethod)
            {
                this.messageType = messageType;
                var targetParam = Expression.Parameter(typeof(object), "target");
                var messageParam = Expression.Parameter(typeof(object), "message");
                var castTarget = Expression.Convert(targetParam, targetType);
                var castMessage = Expression.Convert(messageParam, messageType);
                var callExpression = Expression.Call(castTarget, invocationMethod, castMessage);
                this.invoker = Expression.Lambda<Action<object, object>>(callExpression, targetParam, messageParam).Compile();
            }

            public void Invoke(object target, Type messageType, object message, Action<Action> dispatcher)
            {
                if (this.messageType.IsAssignableFrom(messageType))
                    dispatcher(() => this.invoker(target, message));
            }
        }
    }

    /// <summary>
    /// Extension methods on IEventAggregator, to give more dispatching options
    /// </summary>
    public static class EventAggregatorExtensions
    {
        /// <summary>
        /// Publish an event to all subscribers, calling the handle methods on the UI thread
        /// </summary>
        /// <param name="eventAggregator">EventAggregator to publish the message with</param>
        /// <param name="message">Event to publish</param>
        /// <param name="channels">Channel(s) to publish the message to. Defaults to EventAggregator.DefaultChannel none given</param>
        public static void PublishOnUIThread(this IEventAggregator eventAggregator, object message, params string[] channels)
        {
            eventAggregator.PublishWithDispatcher(message, Execute.OnUIThread, channels);
        }

        /// <summary>
        /// Publish an event to all subscribers, calling the handle methods synchronously on the current thread
        /// </summary>
        /// <param name="eventAggregator">EventAggregator to publish the message with</param>
        /// <param name="message">Event to publish</param>
        /// <param name="channels">Channel(s) to publish the message to. Defaults to EventAggregator.DefaultChannel none given</param>
        public static void Publish(this IEventAggregator eventAggregator, object message, params string[] channels)
        {
            eventAggregator.PublishWithDispatcher(message, a => a(), channels);
        }
    }
}

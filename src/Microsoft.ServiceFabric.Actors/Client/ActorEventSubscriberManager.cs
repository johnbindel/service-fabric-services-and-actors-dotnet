// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------
namespace Microsoft.ServiceFabric.Actors.Client
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Actors.Remoting;
    using Microsoft.ServiceFabric.Actors.Remoting.Builder;
    using Microsoft.ServiceFabric.Services.Common;
    using Microsoft.ServiceFabric.Services.Remoting;
    using Microsoft.ServiceFabric.Services.Remoting.Builder;

    internal class ActorEventSubscriberManager : IServiceRemotingCallbackClient
    {
        public static readonly ActorEventSubscriberManager Singleton = new ActorEventSubscriberManager();

        private readonly ConcurrentDictionary<Subscriber, SubscriptionInfo> eventKeyToInfoMap;
        private readonly ConcurrentDictionary<Guid, SubscriptionInfo> subscriptionIdToInfoMap;
        private readonly ConcurrentDictionary<int, ActorMethodDispatcherBase> eventIdToDispatchersMap;
        
        private ActorEventSubscriberManager()
        {
            this.eventIdToDispatchersMap = new ConcurrentDictionary<int, ActorMethodDispatcherBase>();
            this.eventKeyToInfoMap = new ConcurrentDictionary<Subscriber, SubscriptionInfo>();
            this.subscriptionIdToInfoMap = new ConcurrentDictionary<Guid, SubscriptionInfo>();
        }

        public void RegisterEventDispatchers(IEnumerable<ActorMethodDispatcherBase> eventDispatchers)
        {
            if (eventDispatchers != null)
            {
                foreach (var dispatcher in eventDispatchers)
                {
                    this.eventIdToDispatchersMap.GetOrAdd(
                        dispatcher.InterfaceId,
                        dispatcher);
                }
            }
        }

        public Task<byte[]> RequestResponseAsync(ServiceRemotingMessageHeaders messageHeaders, byte[] requestBody)
        {
            throw new NotImplementedException();
        }

        public void OneWayMessage(ServiceRemotingMessageHeaders serviceMessageHeaders, byte[] requestBody)
        {
            ActorMessageHeaders actorHeaders;
            if (!ActorMessageHeaders.TryFromServiceMessageHeaders(serviceMessageHeaders, out actorHeaders))
            {
                return;
            }

            ActorMethodDispatcherBase eventDispatcher;
            if ((this.eventIdToDispatchersMap == null) ||
                (!this.eventIdToDispatchersMap.TryGetValue(actorHeaders.InterfaceId, out eventDispatcher)))
            {
                return;
            }

            SubscriptionInfo info;
            if (!this.subscriptionIdToInfoMap.TryGetValue(actorHeaders.ActorId.GetGuidId(), out info))
            {
                return;
            }

            if (info.Subscriber.EventId != actorHeaders.InterfaceId)
            {
                return;
            }

            try
            {
                var eventMsgBody = eventDispatcher.DeserializeRequestMessageBody(requestBody);
                eventDispatcher.Dispatch(info.Subscriber.Instance, actorHeaders.MethodId, eventMsgBody);
            }
            catch
            {
                // ignored
            }
        }

        public SubscriptionInfo RegisterSubscriber(ActorId actorId, Type eventInterfaceType, object instance)
        {
            var eventId = this.GetAndEnsureEventId(eventInterfaceType);

            var key = new Subscriber(actorId, eventId, instance);
            var info = this.eventKeyToInfoMap.GetOrAdd(key, k => new SubscriptionInfo(k));
            this.subscriptionIdToInfoMap.GetOrAdd(info.Id, i => info);

            return info;
        }

        public bool TryUnregisterSubscriber(ActorId actorId, Type eventInterfaceType, object instance, out SubscriptionInfo info)
        {
            var eventId = this.GetAndEnsureEventId(eventInterfaceType);

            var key = new Subscriber(actorId, eventId, instance);
            if (this.eventKeyToInfoMap.TryRemove(key, out info))
            {
                info.IsActive = false;

                SubscriptionInfo info2;
                this.subscriptionIdToInfoMap.TryRemove(info.Id, out info2);
                return true;
            }

            return false;
        }

        private int GetAndEnsureEventId(Type eventInterfaceType)
        {
            if (this.eventIdToDispatchersMap != null)
            {
                var eventId = IdUtil.ComputeId(eventInterfaceType);
                if (this.eventIdToDispatchersMap.ContainsKey(eventId))
                {
                    return eventId;
                }
            }

            throw new ArgumentException();
        }

        internal class SubscriptionInfo
        {
            public readonly Guid Id;
            public readonly Subscriber Subscriber;
            public bool IsActive;

            public SubscriptionInfo(Subscriber subscriber)
            {
                this.Subscriber = subscriber;
                this.Id = Guid.NewGuid();
                this.IsActive = true;
            }
        }

        internal class Subscriber
        {
            public readonly ActorId ActorId;
            public readonly int EventId;
            public readonly object Instance;

            public Subscriber(ActorId actorId, int eventId, object instance)
            {
                this.ActorId = actorId;
                this.EventId = eventId;
                this.Instance = instance;
            }

            public override bool Equals(object obj)
            {
                var other = obj as Subscriber;
                return (
                    (other != null) &&
                    (this.EventId.Equals(other.EventId)) &&
                    (this.ActorId.Equals(other.ActorId)) &&
                    (ReferenceEquals(this.Instance, other.Instance)));
            }

            public override int GetHashCode()
            {
                var hash = this.ActorId.GetHashCode();
                hash = IdUtil.HashCombine(hash, this.EventId.GetHashCode());
                return IdUtil.HashCombine(hash, this.Instance.GetHashCode());
            }
        }
    }
}

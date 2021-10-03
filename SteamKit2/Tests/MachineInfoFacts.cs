using System;
using System.IO;
using System.Text;
using System.Threading;
using SteamKit2;
using Xunit;

namespace Tests
{
    public class MachineInfoFacts
    {
        [Fact]
        public void ResultIsCached()
        {
            var provider = new CountingMachineInfoProvider();

            HardwareUtils.Init(provider);
            HardwareUtils.GetMachineID(provider);

            var invocations = provider.TotalInvocations;

            for (var i = 0; i < 100; i++)
            {
                HardwareUtils.Init(provider);
                HardwareUtils.GetMachineID(provider);
            }

            Assert.Equal(invocations, provider.TotalInvocations);
        }

        [Fact]
        public void ResultIsCachedByInstance()
        {
            var provider = new CountingMachineInfoProvider();
            HardwareUtils.Init(provider);
            HardwareUtils.GetMachineID(provider);

            var invocations = provider.TotalInvocations;

            for (var i = 0; i < 100; i++)
            {
                var newProvider = new CountingMachineInfoProvider();
                Assert.Equal(0, newProvider.TotalInvocations);

                HardwareUtils.Init(newProvider);
                HardwareUtils.GetMachineID(newProvider);

                Assert.Equal(invocations, newProvider.TotalInvocations);
                Assert.Equal(invocations, provider.TotalInvocations);
            }

            Assert.Equal(invocations, provider.TotalInvocations);
        }

        [Fact]
        public void MachineInfoIsProcessedInBackground()
        {
            var provider = new ThreadRejectingMachineInfoProvider(Thread.CurrentThread.ManagedThreadId);
            HardwareUtils.Init(provider);

            // Should not throw
            HardwareUtils.GetMachineID(provider);
        }

        [Fact]
        public void ProviderIsNotRetained()
        {
            WeakReference Setup()
            {
                var provider = new CountingMachineInfoProvider();
                HardwareUtils.Init(provider);
                HardwareUtils.GetMachineID(provider);
                return new WeakReference(provider);
            }

            var provider = Setup();
            GC.Collect();
            Assert.False(provider.IsAlive);
        }

        [Fact]
        public void GenerationIsThreadSafe()
        {
            var provider = new CountingMachineInfoProvider();
            var trigger = new ManualResetEventSlim();

            var threads = new Thread[100];
            for (var i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread(state =>
                {
                   var provider = (IMachineInfoProvider)state;
                   trigger.Wait();
                   HardwareUtils.Init(provider);
                   HardwareUtils.GetMachineID(provider);
                });
                threads[i].Start(provider);
            }

            trigger.Set();

            for (var i = 0; i < threads.Length; i++)
            {
                threads[i].Join();
            }

            Assert.Equal(3, provider.TotalInvocations);
        }

        [Fact]
        public void GeneratesMessageObject()
        {
            var provider = new StaticMachineInfoProvider();
            HardwareUtils.Init(provider);
            var messageObjectData = HardwareUtils.GetMachineID(provider);
            
            var kv = new KeyValue();
            using (var ms = new MemoryStream(messageObjectData))
            {
                Assert.True(kv.TryReadAsBinary(ms));
            }

            Assert.Equal("MessageObject", kv.Name);
            Assert.Equal("3018ba91fc5a72f8b3f74501af6dd6da331b6cbc", kv["BB3"].AsString());
            Assert.Equal("5d7e7734714b64bd6d88fef3ddbbf7ab4a749c5e", kv["FF2"].AsString());
            Assert.Equal("a1807176456746ba2a3f5574bf47677a919dab49", kv["3B3"].AsString());
        }

        sealed class CountingMachineInfoProvider : IMachineInfoProvider
        {
            public int TotalInvocations { get; private set; }

            public byte[] GetDiskId()
            {
                TotalInvocations++;
                return Array.Empty<byte>();
            }

            public byte[] GetMacAddress()
            {
                TotalInvocations++;
                return Array.Empty<byte>();
            }
            public byte[] GetMachineGuid()
            {
                TotalInvocations++;
                return Array.Empty<byte>();
            }
        }

        sealed class ThreadRejectingMachineInfoProvider : IMachineInfoProvider
        {
            public ThreadRejectingMachineInfoProvider(int threadId)
            {
                ThreadIdToReject = threadId;
            }

            public int ThreadIdToReject { get; }

            public byte[] GetDiskId()
            {
                EnsureNotOnRejectedThread();
                return Array.Empty<byte>();
            }

            public byte[] GetMacAddress()
            {
                EnsureNotOnRejectedThread();
                return Array.Empty<byte>();
            }

            public byte[] GetMachineGuid()
            {
                EnsureNotOnRejectedThread();
                return Array.Empty<byte>();
            }

            void EnsureNotOnRejectedThread()
            {
                if (Thread.CurrentThread.ManagedThreadId == ThreadIdToReject)
                {
                    throw new InvalidOperationException("Operation must not be run on rejected thread.");
                }
            }
        }

        sealed class StaticMachineInfoProvider : IMachineInfoProvider
        {
            public byte[] GetDiskId() => Encoding.UTF8.GetBytes("DiskId");
            public byte[] GetMacAddress() => Encoding.UTF8.GetBytes("MacAddress");
            public byte[] GetMachineGuid() => Encoding.UTF8.GetBytes("MachineGuid");
        }
    }
}
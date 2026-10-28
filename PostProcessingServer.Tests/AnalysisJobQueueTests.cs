using System;
using System.Collections.Generic;
using PostProcessingServer.Services;
using TilerCrossServerResources;
using Xunit;
using AnalysisJob = PostProcessingServer.Models.AnalysisJob;

namespace PostProcessingServer.Tests
{
    public sealed class AnalysisJobQueueTests
    {
        private static AnalysisJob Job(string id) => new AnalysisJob { Id = id, TilerUserId = "fixture", JobType = AnalysisJobType.ProcessRequest };

        [Fact]
        public void PersistFailureMustNotPublishExecutableWork()
        {
            var queue = new AnalysisJobQueue(job => throw new InvalidOperationException("database unavailable"), () => new AnalysisJob[0]);
            Assert.Throws<InvalidOperationException>(() => queue.Enqueue(Job("failed")));
            Assert.Equal(0, queue.QueueDepth);
            Assert.Equal(0, queue.ActiveJobCount);
            Assert.False(queue.TryDequeue(out _));
        }

        [Fact]
        public void JobIsNotVisibleUntilPersistenceSucceeds()
        {
            AnalysisJobQueue queue = null;
            int writes = 0;
            queue = new AnalysisJobQueue(job => {
                Assert.Equal(0, queue.QueueDepth);
                Assert.False(queue.TryDequeue(out _));
                writes++;
            }, () => new AnalysisJob[0]);
            queue.Enqueue(Job("saved"));
            Assert.Equal(1, writes);
            Assert.True(queue.TryDequeue(out var claimed));
            Assert.Equal("saved", claimed.Id);
            Assert.Equal(AnalysisJobStatus.Processing, claimed.Status);
            Assert.False(queue.TryDequeue(out _));
        }

        [Fact]
        public void RecoveryFailureIsRetriedAndSuccessfulRecoveryIsNotDuplicated()
        {
            int loads = 0;
            var queue = new AnalysisJobQueue(job => { }, () => {
                if (++loads == 1) throw new InvalidOperationException("database unavailable");
                return new[] { Job("recovered") };
            });
            Assert.Throws<InvalidOperationException>(() => queue.LoadIncompleteJobsFromDatabase());
            Assert.Equal(0, queue.QueueDepth);
            Assert.Equal(1, queue.LoadIncompleteJobsFromDatabase());
            Assert.Equal(0, queue.LoadIncompleteJobsFromDatabase());
            Assert.Equal(2, loads);
            Assert.True(queue.TryDequeue(out var recovered));
            Assert.Equal("recovered", recovered.Id);
            Assert.False(queue.TryDequeue(out _));
        }

        [Fact]
        public void FirstEnqueueRecoversExistingJobsBeforePublishingNewWork()
        {
            var stored = new List<AnalysisJob> { Job("old") };
            var queue = new AnalysisJobQueue(job => stored.Add(job), () => stored.ToArray());
            queue.Enqueue(Job("new"));
            queue.LoadIncompleteJobsFromDatabase();
            Assert.True(queue.TryDequeue(out var first));
            Assert.True(queue.TryDequeue(out var second));
            Assert.Equal("old", first.Id);
            Assert.Equal("new", second.Id);
            Assert.False(queue.TryDequeue(out _));
        }
    }
}

using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;
using squad.CopilotSdk;

namespace squad.Specs;

[TestFixture]
public sealed class AgentEventChannelTests
{
    [Test]
    public async Task AgentEventChannel_BoundsDepth_UnderSustainedProducer()
    {
        const int capacity = 5;
        await using var channel = new AgentEventChannel(capacity, TimeSpan.FromSeconds(5));

        for (var index = 1; index <= 20; index++)
        {
            channel.Publish(new AgentUserMessageEvent(DateTimeOffset.UtcNow, $"msg-{index}"));
        }

        Assert.That(channel.Depth, Is.LessThanOrEqualTo(capacity));
    }

    [Test]
    public async Task AgentEventChannel_PreservesStrictOrdering_WithSlowConsumer()
    {
        const int capacity = 5;
        const int totalEvents = 50;
        await using var channel = new AgentEventChannel(capacity, TimeSpan.FromSeconds(5));

        var readEvents = new List<string>();

        var producerTask = Task.Run(async () =>
        {
            for (var index = 1; index <= totalEvents; index++)
            {
                await channel.PublishAsync(new AgentUserMessageEvent(DateTimeOffset.UtcNow, $"msg-{index}"));
            }
            channel.Complete();
        });

        await foreach (var evt in channel.ReadAllAsync())
        {
            if (evt is AgentUserMessageEvent userMsg)
            {
                readEvents.Add(userMsg.Content);
            }
            await Task.Delay(2);
        }

        await producerTask;

        Assert.That(readEvents.Count, Is.EqualTo(totalEvents));
        for (var index = 1; index <= totalEvents; index++)
        {
            Assert.That(readEvents[index - 1], Is.EqualTo($"msg-{index}"));
        }
    }

    [Test]
    public async Task AgentEventChannel_SurfacesSustainedOverload_WhenConsumerStops()
    {
        const int capacity = 3;
        var writeTimeout = TimeSpan.FromMilliseconds(200);
        Exception? capturedOverload = null;

        await using var channel = new AgentEventChannel(capacity, writeTimeout, ex => capturedOverload = ex);

        for (var index = 1; index <= capacity; index++)
        {
            channel.Publish(new AgentUserMessageEvent(DateTimeOffset.UtcNow, $"msg-{index}"));
        }

        channel.Publish(new AgentUserMessageEvent(DateTimeOffset.UtcNow, "overflow-msg"));

        await Task.Delay(400);

        var readCount = 0;
        var exceptionThrown = false;
        try
        {
            await foreach (var _ in channel.ReadAllAsync())
            {
                readCount++;
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("sustained overload"))
        {
            exceptionThrown = true;
        }

        Assert.That(exceptionThrown, Is.True);
        Assert.That(capturedOverload, Is.Not.Null);
        Assert.That(capturedOverload!.Message, Does.Contain("sustained overload"));
    }

    [Test]
    public async Task AgentEventChannel_Disposal_UnblocksWaitingWritersCleanly()
    {
        const int capacity = 3;
        var channel = new AgentEventChannel(capacity, TimeSpan.FromSeconds(10));

        for (var index = 1; index <= capacity; index++)
        {
            channel.Publish(new AgentUserMessageEvent(DateTimeOffset.UtcNow, $"msg-{index}"));
        }

        var writeTask = channel.PublishAsync(new AgentUserMessageEvent(DateTimeOffset.UtcNow, "blocked-msg"));

        Assert.That(writeTask.IsCompleted, Is.False);

        await channel.DisposeAsync();

        Assert.DoesNotThrowAsync(async () => await writeTask);
    }

    [Test]
    public async Task AgentEventChannel_RejectsConcurrentOverflowWithoutQueuingAnotherWriter()
    {
        const int capacity = 1;
        Exception? capturedOverload = null;
        await using var channel = new AgentEventChannel(
            capacity,
            TimeSpan.FromSeconds(5),
            exception => capturedOverload = exception);

        channel.Publish(new AgentUserMessageEvent(DateTimeOffset.UtcNow, "first"));
        var waitingWrite = channel.PublishAsync(
            new AgentUserMessageEvent(DateTimeOffset.UtcNow, "waiting"));
        var rejectedWrite = channel.PublishAsync(
            new AgentUserMessageEvent(DateTimeOffset.UtcNow, "rejected"));

        Assert.That(rejectedWrite.IsCompleted, Is.True);
        Assert.That(
            async () => await rejectedWrite,
            Throws.InstanceOf<InvalidOperationException>()
                .With.Message.Contains("sustained overload"));
        Assert.That(capturedOverload, Is.Not.Null);
        Assert.That(
            async () => await waitingWrite,
            Throws.InstanceOf<System.Threading.Channels.ChannelClosedException>());
    }

    [Test]
    public async Task CopilotSdkAgentSession_UsesBoundedChannel_AndPreservesOrder()
    {
        const int capacity = 5;
        const int totalEvents = 30;
        await using var session = new CopilotSdkAgentSession("coder", capacity, TimeSpan.FromSeconds(5));

        var receivedEvents = new List<string>();

        var producerTask = Task.Run(async () =>
        {
            for (var index = 1; index <= totalEvents; index++)
            {
                await session.PublishAsync(new AgentUserMessageEvent(DateTimeOffset.UtcNow, $"sdk-msg-{index}"));
            }
        });

        var consumerTask = Task.Run(async () =>
        {
            var count = 0;
            await foreach (var evt in session.Events())
            {
                if (evt is AgentUserMessageEvent msg)
                {
                    receivedEvents.Add(msg.Content);
                    count++;
                    if (count == totalEvents)
                        break;
                }
                await Task.Delay(2);
            }
        });

        await Task.WhenAll(producerTask, consumerTask);

        Assert.That(receivedEvents.Count, Is.EqualTo(totalEvents));
        for (var index = 1; index <= totalEvents; index++)
        {
            Assert.That(receivedEvents[index - 1], Is.EqualTo($"sdk-msg-{index}"));
        }
    }

    [Test]
    public async Task CopilotSdkAgentSession_RejectsPromptsAfterEventChannelFailure()
    {
        await using var session = new CopilotSdkAgentSession(
            "coder",
            capacity: 1,
            writeTimeout: TimeSpan.FromSeconds(5));

        session.Publish(new AgentUserMessageEvent(DateTimeOffset.UtcNow, "first"));
        session.Publish(new AgentUserMessageEvent(DateTimeOffset.UtcNow, "waiting"));
        session.Publish(new AgentUserMessageEvent(DateTimeOffset.UtcNow, "rejected"));

        Assert.That(
            async () => await session.Completion,
            Throws.InstanceOf<InvalidOperationException>()
                .With.Message.Contains("sustained overload"));
        Assert.That(
            async () => await session.SendAsync("must not run"),
            Throws.InstanceOf<InvalidOperationException>()
                .With.Message.Contains("sustained overload"));
    }
}




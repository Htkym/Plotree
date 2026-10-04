using Plotree.Services;

namespace Plotree.Tests;

[TestClass]
public sealed class DeferredGraphRendererTests
{
    [TestMethod]
    public void RequestsBeforeLoadingAndRepeatedNotifications_ProduceOneQueuedRender()
    {
        var callbacks = new Queue<Action>(); var count = 0;
        var renderer = Create(callbacks, () => count++);
        renderer.Request(); renderer.Request(); Assert.IsEmpty(callbacks);
        renderer.SetLoaded(true); renderer.Request(); renderer.Request();
        Assert.HasCount(1, callbacks); Assert.AreEqual(0, count);
        callbacks.Dequeue()(); Assert.AreEqual(1, count); Assert.IsEmpty(callbacks);
    }

    [TestMethod]
    public void NotificationsRaisedDuringRender_AreDeferredAndNeverReenterCollectionMutation()
    {
        var callbacks = new Queue<Action>(); var count = 0; var depth = 0; var maxDepth = 0;
        DeferredGraphRenderer? renderer = null;
        renderer = Create(callbacks, () =>
        {
            depth++; maxDepth = Math.Max(maxDepth, depth); count++;
            if (count == 1) { renderer!.Request(); renderer.Request(); Assert.IsEmpty(callbacks); }
            depth--;
        });
        renderer.SetLoaded(true); renderer.Request(); callbacks.Dequeue()();
        Assert.AreEqual(1, count); Assert.HasCount(1, callbacks);
        callbacks.Dequeue()(); Assert.AreEqual(2, count); Assert.AreEqual(1, maxDepth);
    }

    [TestMethod]
    public void DragSuspension_DefersAlreadyQueuedWorkUntilCaptureHasBeenReleased()
    {
        var callbacks = new Queue<Action>(); var count = 0; var captureReleased = false;
        var renderer = Create(callbacks, () => { Assert.IsTrue(captureReleased); count++; });
        renderer.SetLoaded(true); renderer.Request(); renderer.SetSuspended(true);
        callbacks.Dequeue()(); renderer.Request(); renderer.Request();
        Assert.AreEqual(0, count); Assert.IsEmpty(callbacks);
        captureReleased = true; renderer.SetSuspended(false);
        Assert.HasCount(1, callbacks); callbacks.Dequeue()(); Assert.AreEqual(1, count);
    }

    [TestMethod]
    public void UnloadedAndReloadedControl_DiscardsCallbacksFromItsPreviousLifetime()
    {
        var callbacks = new Queue<Action>(); var count = 0;
        var renderer = Create(callbacks, () => count++);
        renderer.SetLoaded(true); renderer.Request();
        renderer.SetLoaded(false); renderer.Request(); renderer.SetLoaded(true);
        Assert.HasCount(2, callbacks);
        callbacks.Dequeue()(); Assert.AreEqual(0, count);
        renderer.Request(); Assert.HasCount(1, callbacks);
        callbacks.Dequeue()(); Assert.AreEqual(1, count);
    }

    [TestMethod]
    public void UnloadDuringRender_StopsFollowupWorkUntilReload()
    {
        var callbacks = new Queue<Action>(); var count = 0;
        DeferredGraphRenderer? renderer = null;
        renderer = Create(callbacks, () => { count++; if (count == 1) { renderer!.Request(); renderer.SetLoaded(false); } });
        renderer.SetLoaded(true); renderer.Request(); callbacks.Dequeue()(); Assert.IsEmpty(callbacks);
        renderer.SetLoaded(true); callbacks.Dequeue()(); Assert.AreEqual(2, count);
    }

    [TestMethod]
    public void DispatcherEnqueueFailure_RetainsPendingWorkForTheNextRequest()
    {
        var callbacks = new Queue<Action>(); var allowEnqueue = false; var count = 0;
        var renderer = new DeferredGraphRenderer(action =>
        {
            if (!allowEnqueue) return false;
            callbacks.Enqueue(action); return true;
        }, () => count++);
        renderer.SetLoaded(true); renderer.Request(); Assert.IsEmpty(callbacks);
        allowEnqueue = true; renderer.Request(); callbacks.Dequeue()(); Assert.AreEqual(1, count);
    }

    [TestMethod]
    public void RenderFailure_IsReportedAndDoesNotLeaveTheRendererPermanentlyLocked()
    {
        var callbacks = new Queue<Action>(); var fail = true; var count = 0;
        var renderer = Create(callbacks, () => { if (fail) throw new InvalidOperationException("render failure"); count++; });
        renderer.SetLoaded(true); renderer.Request();
        Assert.ThrowsExactly<InvalidOperationException>(() => callbacks.Dequeue()());
        fail = false; renderer.Request(); callbacks.Dequeue()(); Assert.AreEqual(1, count);
    }

    private static DeferredGraphRenderer Create(Queue<Action> callbacks, Action render) =>
        new(action => { callbacks.Enqueue(action); return true; }, render);
}

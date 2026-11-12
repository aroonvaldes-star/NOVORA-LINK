namespace NOVORA.VisionEngine.Events;

public sealed class VEEventsDispatcherEvent
{
    public event EventHandler<VEEventsMessageEvent>? EventRaisedVE;

    public void PublishVE(VEEventsMessageEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);
        EventHandler<VEEventsMessageEvent>? handler = EventRaisedVE;
        if (handler is null) return;

        foreach (Delegate subscriber in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<VEEventsMessageEvent>)subscriber)(this, message);
            }
            catch
            {
                // Un observador nunca debe romper el productor del evento.
            }
        }
    }
}
